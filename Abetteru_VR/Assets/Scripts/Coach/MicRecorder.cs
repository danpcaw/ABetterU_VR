using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ABetterU.Coach
{
    /// Push-to-talk microphone capture. StartRecording() on button down, StopRecording() on
    /// button up returns a 16 kHz mono WAV (or null if too short / no mic).
    public class MicRecorder : MonoBehaviour
    {
        public int sampleRate = 16000;
        public int maxSeconds = 20;
        public float minSeconds = 0.4f;

        AudioClip clip;
        string device;

        public bool IsRecording { get; private set; }

        /// Asks for the Android microphone permission (a dialog appears in the headset).
        public IEnumerator EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                float t = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && t < 30f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
#endif
            yield break;
        }

        public bool StartRecording()
        {
            if (IsRecording) return false;
            if (Microphone.devices.Length == 0) { Debug.LogWarning("[Mic] No microphone found."); return false; }
            device = Microphone.devices[0];
            clip = Microphone.Start(device, false, maxSeconds, sampleRate);
            IsRecording = clip != null;
            return IsRecording;
        }

        public byte[] StopRecording()
        {
            if (!IsRecording) return null;
            IsRecording = false;

            int position = Microphone.GetPosition(device);
            Microphone.End(device);
            if (clip == null || position < sampleRate * minSeconds) return null;

            var samples = new float[position * clip.channels];
            clip.GetData(samples, 0);
            return WavEncoder.FromSamples(samples, sampleRate);
        }
    }
}
