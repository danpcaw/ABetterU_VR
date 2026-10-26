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

            float peak = 0f;
            foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            Debug.Log($"[Mic] {position} samples, {clip.channels} ch, clip {clip.frequency} Hz, wav {sampleRate} Hz, peak {peak:0.000}");

            // The API only accepts 16 or 24 kHz mono. Downmix and resample if the device differs.
            samples = Downmix(samples, clip.channels);
            if (clip.frequency != sampleRate) samples = Resample(samples, clip.frequency, sampleRate);
            return WavEncoder.FromSamples(samples, sampleRate);
        }

        static float[] Downmix(float[] interleaved, int channels)
        {
            if (channels <= 1) return interleaved;
            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        // Linear interpolation: good enough for speech going to a transcriber.
        static float[] Resample(float[] input, int fromRate, int toRate)
        {
            int outLength = (int)((long)input.Length * toRate / fromRate);
            var output = new float[outLength];
            double step = (double)fromRate / toRate;
            for (int i = 0; i < outLength; i++)
            {
                double pos = i * step;
                int i0 = (int)pos;
                int i1 = Mathf.Min(i0 + 1, input.Length - 1);
                float frac = (float)(pos - i0);
                output[i] = input[i0] * (1f - frac) + input[i1] * frac;
            }
            return output;
        }
    }
}
