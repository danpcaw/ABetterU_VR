using System.Collections;
using UnityEngine;

namespace ABetterU.Coach
{
    /// The sounds and controller buzzes that make a workout feel responsive. Every sound is generated in code, so there are
    /// no audio files to license or ship. Kept quieter than Atlas's voice, and the buzzes are short so they never annoy.
    public class FeedbackCues : MonoBehaviour
    {
        const int Rate = 24000;
        AudioSource source;
        AudioClip rep, milestone, reject, setDone, countdown, go;
        Coroutine pulsing;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.55f;

            rep = Tones("rep", 0.22f, (660f, 0f, 0.5f), (990f, 0.08f, 0.5f));
            milestone = Tones("milestone", 0.42f, (784f, 0f, 0.45f), (988f, 0.09f, 0.45f), (1319f, 0.18f, 0.5f));
            reject = Tones("reject", 0.2f, (170f, 0f, 0.5f), (150f, 0.09f, 0.5f));
            setDone = Tones("set-done", 0.9f, (523f, 0f, 0.4f), (659f, 0.14f, 0.4f), (784f, 0.28f, 0.4f), (1047f, 0.42f, 0.5f));
            countdown = Tones("countdown", 0.07f, (1000f, 0f, 0.4f));
            go = Tones("go", 0.28f, (1400f, 0f, 0.5f));
        }

        // ── The moments ──────────────────────────────────────────────────────────

        public void RepCounted(int count)
        {
            bool five = count > 0 && count % 5 == 0;
            source.PlayOneShot(five ? milestone : rep);
            Pulse((0.7f, 0.10f), (0f, 0.05f), (five ? 0.9f : 0f, five ? 0.12f : 0f));
        }

        public void RepRejected()
        {
            source.PlayOneShot(reject);
            Pulse((1f, 0.08f), (0f, 0.06f), (1f, 0.08f));
        }

        public void SetComplete()
        {
            source.PlayOneShot(setDone);
            Pulse((0.6f, 0.15f), (0f, 0.08f), (0.8f, 0.30f));
        }

        public void CountdownTick() { source.PlayOneShot(countdown); Pulse((0.35f, 0.05f)); }
        public void CountdownGo() { source.PlayOneShot(go); Pulse((0.8f, 0.2f)); }

        // ── Haptics: a short pattern of (strength, seconds) steps; a new pattern replaces one still running. ──

        void Pulse(params (float amp, float secs)[] steps)
        {
            if (pulsing != null) { StopCoroutine(pulsing); Vibrate(0f); }
            pulsing = StartCoroutine(PulseRoutine(steps));
        }

        IEnumerator PulseRoutine((float amp, float secs)[] steps)
        {
            foreach (var step in steps)
            {
                if (step.secs <= 0f) continue;
                Vibrate(step.amp);
                for (float t = 0f; t < step.secs; t += Time.unscaledDeltaTime) yield return null;
            }
            Vibrate(0f);
            pulsing = null;
        }

        static void Vibrate(float amp)
        {
            OVRInput.SetControllerVibration(amp > 0f ? 0.6f : 0f, amp, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(amp > 0f ? 0.6f : 0f, amp, OVRInput.Controller.RTouch);
        }

        void OnDisable() { if (pulsing != null) { StopCoroutine(pulsing); pulsing = null; Vibrate(0f); } }

        // ── Sound synthesis: soft sine notes with a touch of the octave and a quick attack and fade, no clicks. ──

        static AudioClip Tones(string name, float length, params (float hz, float start, float gain)[] notes)
        {
            var data = new float[(int)(Rate * length)];
            foreach (var n in notes)
            {
                int from = (int)(n.start * Rate);
                float noteLen = Mathf.Min(length - n.start, 0.35f);
                int count = Mathf.Min((int)(noteLen * Rate), data.Length - from);
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / Rate;
                    float env = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * 9f / noteLen);
                    float w = Mathf.Sin(2f * Mathf.PI * n.hz * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * n.hz * t);
                    data[from + i] += w * env * n.gain * 0.6f;
                }
            }
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
