using UnityEngine;

namespace ABetterU.Coach
{
    /// Orchestrates: mic -> speech-to-text -> Muse Spark -> text-to-speech.
    /// STT and TTS are behind interfaces so Muse Code can plug in whichever Quest-compatible
    /// engine works (Meta Voice SDK, Android SpeechRecognizer/TextToSpeech, etc.).
    public interface ISpeechToText { void StartListening(System.Action<string> onFinalText); void Stop(); }
    public interface ITextToSpeech { void Speak(string text, System.Action onDone = null); bool IsSpeaking { get; } }

    public class VoiceLoopController : MonoBehaviour
    {
        public MonoBehaviour sttBehaviour; // must implement ISpeechToText
        public MonoBehaviour ttsBehaviour; // must implement ITextToSpeech
        public RepCounter repCounter;

        ISpeechToText stt;
        ITextToSpeech tts;
        ModelApiClient client;
        bool busy;
        float turnStarted;

        void Start()
        {
            stt = (ISpeechToText)sttBehaviour;
            tts = (ITextToSpeech)ttsBehaviour;
            var config = ModelApiConfig.Load();
            if (config == null) { Debug.LogError("Missing Resources/ModelApiSecrets asset."); enabled = false; return; }
            client = new ModelApiClient(config);
            stt.StartListening(OnUserSpoke);
        }

        void OnUserSpoke(string text)
        {
            if (busy || string.IsNullOrWhiteSpace(text)) return;
            Coach($"The user said: \"{text}\". {SetContext()}");
        }

        /// Call from RepCounter events for proactive coaching without the user speaking.
        public void CoachProactively(string reason)
        {
            if (busy || tts.IsSpeaking) return;
            Coach($"{reason} {SetContext()}");
        }

        string SetContext() =>
            repCounter == null ? "" : $"Set so far: {repCounter.RepCount} reps, avg {repCounter.SecondsPerRep:0.0}s per rep.";

        void Coach(string prompt)
        {
            busy = true;
            turnStarted = Time.realtimeSinceStartup;
            StartCoroutine(client.Ask(prompt,
                reply =>
                {
                    // Latency log: the number to tune on evenings 3-4.
                    Debug.Log($"[VoiceLoop] Muse Spark replied in {Time.realtimeSinceStartup - turnStarted:0.00}s");
                    tts.Speak(reply, () => busy = false);
                },
                err => { Debug.LogError("[VoiceLoop] " + err); busy = false; }));
        }
    }
}
