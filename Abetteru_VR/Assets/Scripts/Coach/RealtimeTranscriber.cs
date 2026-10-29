using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ABetterU.Coach
{
    /// Muse Voice Transcribe over the realtime WebSocket (wss://api.meta.ai/v1/asr/realtime, ENDPOINTING mode).
    ///
    /// Why it is built this way (all measured Oct 29 from the Mac, see docs/MODEL_API.md):
    ///  - The handshake takes 1.2 to 2.4 s, so a session is opened once and kept WARM instead of opening one per phrase.
    ///  - The server closes a session with 1008 "Ingress below real-time" if audio stops, so while nobody is speaking it is fed
    ///    real-time frames of silence (zeros). No microphone audio leaves the headset until the local detector hears speech.
    ///  - When speech starts, the buffered pre-roll is sent as a burst (up to 1.3 s was accepted), then live audio follows.
    ///  - One session handles many phrases (each gets its own turnId), and speechComplete arrives about 0.1 to 0.3 s after you stop.
    ///  - Billing is audio seconds at $0.18 per hour, so the silent stream costs about $0.003 a minute. Sessions last 60 minutes.
    ///
    /// Threading: the socket work runs on thread-pool threads so a slow Unity frame can never starve the stream.
    /// Call Pump() every frame; Partial and Complete are raised there, on the main thread.
    public class RealtimeTranscriber
    {
        const int Rate = 16000;                          // PCM_16KHZ: the Quest microphone is natively 16 kHz, so no resampling
        const int MaxSessionMinutes = 55;                // the server allows 60; reconnect before it ends the session
        const int IdleFrameMax = Rate / 2;               // at most half a second of silence per send when catching up

        readonly ModelApiConfig config;
        readonly string[] keywords;

        readonly ConcurrentQueue<byte[]> audio = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<(string kind, string text)> events = new ConcurrentQueue<(string, string)>();

        CancellationTokenSource cts;
        volatile bool ready;                             // handshake done and streaming
        volatile bool streamingSpeech;                   // real microphone audio is being sent (between Begin and End)
        float peak;                                      // loudest sample of the current phrase, for gain

        public event Action<string> Partial;             // main thread, via Pump()
        public event Action<string> Complete;            // main thread, via Pump(): a finished phrase
        public bool Ready => ready;
        public bool Wanted => cts != null;

        public RealtimeTranscriber(ModelApiConfig config, string[] keywords)
        {
            this.config = config;
            this.keywords = keywords ?? new string[0];
        }

        /// Opens the warm session (and keeps reopening it if it drops) while wanted, closes it when not.
        public void SetWanted(bool want)
        {
            if (want == Wanted) return;
            if (want)
            {
                cts = new CancellationTokenSource();
                var token = cts.Token;
                Task.Run(() => Supervise(token));
            }
            else
            {
                cts.Cancel();
                cts = null;
                ready = false;
                streamingSpeech = false;
                while (audio.TryDequeue(out _)) { }
            }
        }

        /// Raises queued results on the calling (main) thread.
        public void Pump()
        {
            while (events.TryDequeue(out var e))
            {
                if (e.kind == "partial") Partial?.Invoke(e.text);
                else if (e.kind == "complete") Complete?.Invoke(e.text);
                else UnityEngine.Debug.Log("[Realtime] " + e.text);
            }
        }

        /// Speech has started: send the pre-roll now, then live audio via Push. Returns false when the session is not ready,
        /// in which case the caller should rely on the upload path for this phrase.
        public bool BeginUtterance(float[] preRoll16k)
        {
            if (!ready) return false;
            peak = 0f;
            streamingSpeech = true;
            Push(preRoll16k);
            return true;
        }

        public void Push(float[] chunk16k)
        {
            if (!streamingSpeech || chunk16k == null || chunk16k.Length == 0) return;

            // The Quest microphone is quiet (peak about 0.17), and quiet audio hurt accuracy, so the upload path lifts each phrase to
            // a peak of 0.8. A stream cannot see the end of the phrase, so the gain follows the loudest sound heard so far.
            foreach (var s in chunk16k) { float a = s < 0 ? -s : s; if (a > peak) peak = a; }
            float gain = peak < 0.01f ? 1f : Math.Min(8f, Math.Max(1f, 0.8f / peak));

            var bytes = new byte[chunk16k.Length * 2];
            for (int i = 0; i < chunk16k.Length; i++)
            {
                float v = chunk16k[i] * gain;
                short s16 = (short)(v >= 1f ? short.MaxValue : v <= -1f ? short.MinValue : v * short.MaxValue);
                bytes[i * 2] = (byte)(s16 & 0xFF);
                bytes[i * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
            }
            audio.Enqueue(bytes);
        }

        /// Speech has ended locally: back to streaming silence (the server finishes the phrase by itself).
        public void EndUtterance() => streamingSpeech = false;

        // ── Connection lifecycle (thread pool) ───────────────────────────────────────────────────────────

        async Task Supervise(CancellationToken ct)
        {
            int failures = 0;
            while (!ct.IsCancellationRequested)
            {
                var started = Stopwatch.StartNew();
                try { await RunSession(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log("session ended: " + e.GetType().Name + " " + e.Message); }
                ready = false;
                if (ct.IsCancellationRequested) break;

                failures = started.Elapsed.TotalSeconds > 20 ? 0 : failures + 1;
                int waitMs = Math.Min(500 * (1 << Math.Min(failures, 4)), 6000);
                try { await Task.Delay(waitMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            ready = false;
        }

        async Task RunSession(CancellationToken outer)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(outer);
            limit.CancelAfter(TimeSpan.FromMinutes(MaxSessionMinutes));
            var ct = limit.Token;

            using var ws = new ClientWebSocket();
            var opened = Stopwatch.StartNew();
            var uri = new Uri(config.realtimeEndpoint + "?sessionId=ab-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectTimeout.CancelAfter(8000);
                await ws.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
            }

            var handshake = new JObject
            {
                ["authorization"] = new JObject { ["accessToken"] = "Bearer " + config.apiKey },
                ["audioEncoding"] = "PCM_16KHZ",
                ["model"] = config.transcribeModel,
                ["mode"] = "ENDPOINTING",
                ["languageBias"] = new JArray(config.transcribeLanguageBias ?? new[] { "en-GB" }),
                ["keywords"] = new JArray(keywords),
            };
            await SendText(ws, handshake.ToString(Newtonsoft.Json.Formatting.None), ct).ConfigureAwait(false);

            var acked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receiver = Task.Run(() => ReceiveLoop(ws, acked, ct));
            var first = await Task.WhenAny(acked.Task, Task.Delay(8000, ct)).ConfigureAwait(false);
            if (first != acked.Task) throw new TimeoutException("no handshake acknowledgement");
            Log($"connected in {opened.ElapsedMilliseconds} ms");

            // Stream at real time. Real microphone audio goes out as soon as it is queued (a burst is fine); whenever there is none,
            // silence fills the clock so the stream never falls below real time.
            var clock = Stopwatch.StartNew();
            long sentSamples = 0;
            ready = true;
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open && !receiver.IsCompleted)
            {
                bool sentReal = false;
                while (audio.TryDequeue(out var chunk))
                {
                    await ws.SendAsync(new ArraySegment<byte>(chunk), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                    sentSamples += chunk.Length / 2;
                    sentReal = true;
                }
                if (!sentReal && !streamingSpeech)                      // while speech streams, the microphone itself sets the pace
                {
                    long due = clock.ElapsedMilliseconds * Rate / 1000 - sentSamples;
                    if (due >= Rate / 25)                               // 40 ms or more behind: send silence to catch up
                    {
                        int n = (int)Math.Min(due, IdleFrameMax);
                        await ws.SendAsync(new ArraySegment<byte>(new byte[n * 2]), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                        sentSamples += n;
                    }
                }
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
            ready = false;

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await SendText(ws, "{\"type\":\"endStream\"}", CancellationToken.None).ConfigureAwait(false);
                    ws.Abort();
                }
            }
            catch { }
            if (receiver.IsCompleted && receiver.Exception != null) throw receiver.Exception.GetBaseException();
            if (!ct.IsCancellationRequested) throw new Exception($"closed by server ({ws.CloseStatus} {ws.CloseStatusDescription})");
        }

        async Task ReceiveLoop(ClientWebSocket ws, TaskCompletionSource<bool> acked, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            var text = new StringBuilder();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r;
                text.Clear();
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (r.MessageType == WebSocketMessageType.Text) text.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                } while (!r.EndOfMessage);

                if (text.Length == 0) continue;
                JObject e;
                try { e = JObject.Parse(text.ToString()); } catch { continue; }

                string type = (string)e["type"];
                if (type == null)
                {
                    if (e["sessionId"] != null) acked.TrySetResult(true);       // the handshake acknowledgement is the only frame without a type
                    continue;
                }
                switch (type)
                {
                    case "transcript":
                        if ((bool?)e["final"] != true) events.Enqueue(("partial", (string)e["transcript"] ?? ""));
                        break;
                    case "speechComplete":
                        events.Enqueue(("complete", ((string)e["transcript"] ?? "").Trim()));
                        break;
                    case "error":
                        Log("server error: " + text);
                        break;
                }
            }
        }

        static Task SendText(ClientWebSocket ws, string json, CancellationToken ct) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, ct);

        void Log(string message) => events.Enqueue(("log", message));
    }
}
