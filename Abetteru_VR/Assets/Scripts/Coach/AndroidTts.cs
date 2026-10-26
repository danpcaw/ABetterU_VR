using System;
using System.Collections;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Spoken output through the headset's own text-to-speech engine (Meta's API has no TTS model).
    /// Quest ships "HzOS Text-to-Speech Service". In the editor this just logs and finishes instantly.
    public class AndroidTts : MonoBehaviour, ITextToSpeech
    {
        public float speechRate = 1.05f;

        public bool Ready { get; private set; }
        public string InitError { get; private set; }
        public bool IsSpeaking { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject tts;
        volatile int initStatus = -999;   // set from the Android thread; 0 == SUCCESS

        class InitListener : AndroidJavaProxy
        {
            readonly Action<int> callback;
            public InitListener(Action<int> callback) : base("android.speech.tts.TextToSpeech$OnInitListener") => this.callback = callback;
            public void onInit(int status) => callback(status);
        }

        public IEnumerator Initialise()
        {
            try
            {
                var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(s => initStatus = s));
            }
            catch (Exception e) { InitError = e.Message; yield break; }

            float waited = 0f;
            while (initStatus == -999 && waited < 8f) { waited += Time.unscaledDeltaTime; yield return null; }

            if (initStatus != 0) { InitError = $"TTS init status {initStatus}"; yield break; }

            try
            {
                var uk = new AndroidJavaClass("java.util.Locale").GetStatic<AndroidJavaObject>("UK");
                tts.Call<int>("setLanguage", uk);
                tts.Call<int>("setSpeechRate", speechRate);
            }
            catch (Exception e) { Debug.LogWarning("[TTS] Could not set locale/rate: " + e.Message); }

            Ready = true;
        }

        public void Speak(string text, Action onDone = null)
        {
            if (!Ready || string.IsNullOrWhiteSpace(text)) { onDone?.Invoke(); return; }
            StartCoroutine(SpeakRoutine(text, onDone));
        }

        IEnumerator SpeakRoutine(string text, Action onDone)
        {
            IsSpeaking = true;
            tts.Call<int>("speak", text, 0 /* QUEUE_FLUSH */, (AndroidJavaObject)null, "coach");
            yield return new WaitForSeconds(0.3f);   // give the engine time to start
            while (tts.Call<bool>("isSpeaking")) yield return null;
            IsSpeaking = false;
            onDone?.Invoke();
        }

        void OnDestroy() { try { tts?.Call("shutdown"); } catch { } }
#else
        public IEnumerator Initialise() { Ready = true; yield break; }

        public void Speak(string text, Action onDone = null)
        {
            Debug.Log("[TTS editor stub] " + text);
            onDone?.Invoke();
        }
#endif
    }
}
