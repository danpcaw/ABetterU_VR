using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Spoken output through Wit.ai's text-to-speech (Meta-owned). Meta's Model API has no TTS, and the
    /// Quest's built-in engine is switched off remotely ("TTS disabled by MobileConfig"), so we call
    /// POST https://api.wit.ai/synthesize with Accept: audio/raw (24 kHz, 16-bit, mono PCM) and play it.
    ///
    /// The reply is split into sentences, all fetched in parallel, and played in order so speech starts
    /// as soon as the first sentence is ready.
    public class WitSpeaker : MonoBehaviour, ITextToSpeech
    {
        const int SampleRate = 24000;
        const int MaxChars = 250;   // Wit limits the length of a single request

        ModelApiConfig config;
        AudioSource source;

        public bool IsSpeaking { get; private set; }
        public bool Ready => config != null && !string.IsNullOrEmpty(config.witToken);

        [Serializable] class SynthRequest { public string q; public string voice; public string style; }

        public void Init(ModelApiConfig modelConfig)
        {
            config = modelConfig;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // plain 2D audio in the headset
            source.volume = 1f;
        }

        // Short phrases fetched once at startup so they can play instantly (no network) while a slow reply is still being made.
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        public float FirstAudioDelay { get; private set; }   // seconds from Speak() to the first sentence starting to play

        public IEnumerator Prefetch(params string[] phrases)
        {
            foreach (var phrase in phrases)
            {
                if (cache.ContainsKey(phrase)) continue;
                AudioClip clip = null;
                yield return Fetch(phrase, c => clip = c);
                if (clip != null) cache[phrase] = clip;
            }
        }

        /// Plays a prefetched phrase immediately. Returns false if it is not cached or something is already playing.
        public bool PlayCached(string phrase)
        {
            if (!cache.TryGetValue(phrase, out var clip) || source.isPlaying) return false;
            source.clip = clip;
            source.Play();
            return true;
        }

        public void Speak(string text, Action onDone = null)
        {
            if (!Ready || string.IsNullOrWhiteSpace(text)) { onDone?.Invoke(); return; }
            StartCoroutine(SpeakRoutine(text, onDone));
        }

        IEnumerator SpeakRoutine(string text, Action onDone)
        {
            IsSpeaking = true;
            float tStart = Time.realtimeSinceStartup;
            while (source.isPlaying) yield return null;     // let a short acknowledgement finish first
            var parts = SplitIntoSentences(text);
            var clips = new AudioClip[parts.Count];
            var finished = new bool[parts.Count];

            for (int i = 0; i < parts.Count; i++)
            {
                int index = i;
                StartCoroutine(Fetch(parts[i], clip => { clips[index] = clip; finished[index] = true; }));
            }

            for (int i = 0; i < parts.Count; i++)
            {
                while (!finished[i]) yield return null;
                if (clips[i] == null) continue;
                source.clip = clips[i];
                source.Play();
                if (i == 0) FirstAudioDelay = Time.realtimeSinceStartup - tStart;
                while (source.isPlaying) yield return null;
            }

            IsSpeaking = false;
            onDone?.Invoke();
        }

        IEnumerator Fetch(string sentence, Action<AudioClip> onClip)
        {
            string url = $"https://api.wit.ai/synthesize?v={config.witVersion}";
            var json = JsonUtility.ToJson(new SynthRequest { q = sentence, voice = config.witVoice, style = config.witStyleV2 });

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Authorization", "Bearer " + config.witToken);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "audio/raw");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Wit TTS] {req.responseCode} {req.error}: {req.downloadHandler.text}");
                onClip(null);
                yield break;
            }

            onClip(PcmToClip(req.downloadHandler.data));
        }

        static AudioClip PcmToClip(byte[] pcm)
        {
            int count = pcm.Length / 2;
            if (count == 0) return null;
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
            var clip = AudioClip.Create("coach", count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static List<string> SplitIntoSentences(string text)
        {
            var result = new List<string>();
            foreach (var raw in Regex.Split(text.Trim(), @"(?<=[.!?])\s+"))
            {
                var s = raw.Trim();
                while (s.Length > MaxChars)
                {
                    int cut = s.LastIndexOf(' ', MaxChars);
                    if (cut <= 0) cut = MaxChars;
                    result.Add(s.Substring(0, cut).Trim());
                    s = s.Substring(cut).Trim();
                }
                if (s.Length > 0) result.Add(s);
            }
            return result;
        }
    }
}
