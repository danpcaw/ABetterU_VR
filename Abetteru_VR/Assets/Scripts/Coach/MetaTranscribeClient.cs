using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Speech-to-text via Muse Voice Transcribe (HTTP file upload, push-to-talk mode).
    /// The realtime WebSocket (wss://api.meta.ai/v1/asr/realtime, ENDPOINTING mode) is the
    /// lower-latency upgrade if round-trip time needs to come down.
    ///
    /// The multipart body is built by hand: UnityWebRequest's own multipart form was rejected by
    /// the API with "Malformed multipart body" (verified on Quest), while this exact layout is accepted.
    public class MetaTranscribeClient
    {
        const string Boundary = "----ABetterUBoundary7d1f2c";

        readonly ModelApiConfig config;

        public MetaTranscribeClient(ModelApiConfig config) => this.config = config;

        [Serializable] class AsrRequest
        {
            public string mode = "PUSH_TO_TALK";
            public string model;
            public string audioEncoding = "WAV";
            public string[] languageBias;
            public string[] keywords;
        }
        [Serializable] class AsrResponse { public string transcript; public int audioDurationMs; }

        public IEnumerator Transcribe(byte[] wav, Action<string> onText, Action<string> onError)
        {
            var requestJson = JsonUtility.ToJson(new AsrRequest
            {
                model = config.transcribeModel,
                languageBias = config.transcribeLanguageBias,
                keywords = KeywordsWithWakeName(),
            });
            var body = BuildMultipart(requestJson, wav);

            using var req = new UnityWebRequest(config.transcribeEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("Content-Type", "multipart/form-data; boundary=" + Boundary);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Transcribe {req.responseCode} {req.error}: {req.downloadHandler.text}");
                yield break;
            }

            var parsed = JsonUtility.FromJson<AsrResponse>(req.downloadHandler.text);
            onText?.Invoke(parsed?.transcript?.Trim() ?? "");
        }

        // The saved settings asset keeps its old keyword list, so add the wake name here: biasing fixed "Ember" and "Nova" in testing.
        string[] KeywordsWithWakeName()
        {
            var list = new System.Collections.Generic.List<string>(config.transcribeKeywords ?? new string[0]);
            if (!string.IsNullOrEmpty(config.wakeName) && !list.Contains(config.wakeName)) list.Add(config.wakeName);
            return list.ToArray();
        }

        static byte[] BuildMultipart(string requestJson, byte[] wav)
        {
            var utf8 = new UTF8Encoding(false);
            using var ms = new MemoryStream(wav.Length + 1024);

            void Text(string s) { var b = utf8.GetBytes(s); ms.Write(b, 0, b.Length); }

            Text($"--{Boundary}\r\nContent-Disposition: form-data; name=\"request\"\r\nContent-Type: application/json\r\n\r\n");
            Text(requestJson);
            Text($"\r\n--{Boundary}\r\nContent-Disposition: form-data; name=\"audio\"; filename=\"speech.wav\"\r\nContent-Type: audio/wav\r\n\r\n");
            ms.Write(wav, 0, wav.Length);
            Text($"\r\n--{Boundary}--\r\n");

            return ms.ToArray();
        }
    }
}
