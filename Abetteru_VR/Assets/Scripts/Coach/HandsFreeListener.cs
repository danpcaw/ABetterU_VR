using System;
using System.Collections.Generic;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Hands-free listening. Keeps the microphone open, notices when someone speaks, and hands back one finished phrase as a
    /// 16 kHz mono WAV once they pause. It does no recognition itself: VoiceLoopTest sends the phrase to Muse Voice Transcribe
    /// and only acts if it starts with the wake name. Pause() it whenever the assistant is busy or speaking, so it cannot hear itself.
    public class HandsFreeListener : MonoBehaviour
    {
        const int OutRate = 16000;
        const int LoopSeconds = 30;
        const float ChunkSeconds = 0.05f;
        const float StartLoudSeconds = 0.15f;        // sound must last this long to count as speech starting
        const float EndSilenceSeconds = 0.8f;        // this much quiet ends the phrase
        const float MaxPhraseSeconds = 12f;
        const float MinSpeechSeconds = 0.5f;         // shorter than this is a click or a cough, not a phrase
        const float PreRollSeconds = 0.4f;           // audio kept from before the speech was noticed, so the first word is not clipped

        public event Action<byte[]> UtteranceReady;
        public event Action SpeechStarted;           // raised when sound that looks like speech begins (before it is known to be for us)
        public bool Listening { get; private set; }

        AudioClip clip;
        string device;
        int srcRate = OutRate, channels = 1, readPos;

        float noiseFloor = 0.01f;
        float peakRms, nextLog;                      // diagnostics: loudest frame since the last log line                    // learned from quiet frames; speech must stand clear of it
        bool inSpeech;
        int loudRun;
        float silentFor, speechSeconds;
        readonly List<float> preRoll = new List<float>();
        readonly List<float> phrase = new List<float>();

        public void Resume()
        {
            if (Listening || Microphone.devices.Length == 0) return;
            device = Microphone.devices[0];
            clip = Microphone.Start(device, true, LoopSeconds, OutRate);
            if (clip == null) return;
            srcRate = clip.frequency;
            channels = Mathf.Max(1, clip.channels);
            readPos = 0;
            ResetState();
            Listening = true;
        }

        public void Pause()
        {
            if (!Listening) return;
            Microphone.End(device);
            Listening = false;
            clip = null;
            ResetState();
        }

        void ResetState()
        {
            inSpeech = false; loudRun = 0; silentFor = 0f; speechSeconds = 0f;
            preRoll.Clear(); phrase.Clear();
        }

        void OnDestroy() => Pause();

        void Update()
        {
            if (!Listening || clip == null) return;
            int pos = Microphone.GetPosition(device);
            if (pos < 0) return;

            int chunk = Mathf.Max(1, (int)(srcRate * ChunkSeconds));
            int available = pos >= readPos ? pos - readPos : clip.samples - readPos + pos;
            while (available >= chunk && Listening)
            {
                Process(ReadChunk(chunk));
                available -= chunk;
            }
        }

        // Reads n mono samples from the looping microphone clip, handling the wrap-around at its end.
        float[] ReadChunk(int n)
        {
            var interleaved = new float[n * channels];
            int end = readPos + n;
            if (end <= clip.samples)
                clip.GetData(interleaved, readPos);
            else
            {
                int first = clip.samples - readPos;
                var a = new float[first * channels];
                var b = new float[(n - first) * channels];
                clip.GetData(a, readPos);
                clip.GetData(b, 0);
                Array.Copy(a, 0, interleaved, 0, a.Length);
                Array.Copy(b, 0, interleaved, a.Length, b.Length);
            }
            readPos = end % clip.samples;

            if (channels == 1) return interleaved;
            var mono = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        void Process(float[] chunk)
        {
            double sumSq = 0;
            foreach (var x in chunk) sumSq += x * x;
            float rms = Mathf.Sqrt((float)(sumSq / chunk.Length));
            float threshold = Mathf.Max(0.008f, noiseFloor * 2.5f);
            bool loud = rms > threshold;

            peakRms = Mathf.Max(peakRms, rms);
            if (Time.unscaledTime >= nextLog)
            {
                Debug.Log($"[HandsFree] listening: loudest {peakRms:0.000}, background {noiseFloor:0.000}, speech threshold {threshold:0.000}, {srcRate} Hz x{channels}");
                peakRms = 0f;
                nextLog = Time.unscaledTime + 4f;
            }

            if (!inSpeech)
            {
                if (!loud) noiseFloor = Mathf.Clamp(Mathf.Lerp(noiseFloor, rms, 0.05f), 0.002f, 0.05f);
                preRoll.AddRange(chunk);
                int keep = (int)(srcRate * PreRollSeconds);
                if (preRoll.Count > keep) preRoll.RemoveRange(0, preRoll.Count - keep);

                loudRun = loud ? loudRun + 1 : Mathf.Max(0, loudRun - 1);     // a brief dip inside a word does not reset it
                if (loudRun * ChunkSeconds >= StartLoudSeconds)
                {
                    inSpeech = true;
                    phrase.Clear();
                    phrase.AddRange(preRoll);
                    preRoll.Clear();
                    silentFor = 0f;
                    speechSeconds = loudRun * ChunkSeconds;
                    Debug.Log($"[HandsFree] speech started (level {rms:0.000}, threshold {threshold:0.000})");
                    SpeechStarted?.Invoke();
                }
                return;
            }

            phrase.AddRange(chunk);
            if (rms > threshold * 0.7f) { silentFor = 0f; speechSeconds += ChunkSeconds; }
            else silentFor += ChunkSeconds;

            float total = phrase.Count / (float)srcRate;
            if (silentFor >= EndSilenceSeconds || total >= MaxPhraseSeconds) Finish();
        }

        void Finish()
        {
            float speech = speechSeconds, silence = silentFor;
            var samples = phrase.ToArray();
            ResetState();
            Debug.Log($"[HandsFree] phrase ended: {speech:0.0}s of speech{(speech < MinSpeechSeconds ? ", too short, discarded" : ", sending to transcribe")}");
            if (speech < MinSpeechSeconds) return;

            // Keep only a short tail of the trailing silence.
            int trim = (int)(Mathf.Max(0f, silence - 0.3f) * srcRate);
            if (trim > 0 && trim < samples.Length) Array.Resize(ref samples, samples.Length - trim);

            if (srcRate != OutRate) samples = MicRecorder.Resample(samples, srcRate, OutRate);
            float peak = 0f;
            foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            MicRecorder.Normalise(samples, peak);

            UtteranceReady?.Invoke(WavEncoder.FromSamples(samples, OutRate));
        }
    }
}
