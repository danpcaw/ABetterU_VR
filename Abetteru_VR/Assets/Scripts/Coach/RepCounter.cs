using UnityEngine;

namespace ABetterU.Coach
{
    /// Counts squat reps from the headset's vertical position (the Quest has no body tracking,
    /// but head height tracks squat depth well). Feeds VoiceLoopController with set context.
    public class RepCounter : MonoBehaviour
    {
        public Transform head;              // the XR camera
        public VoiceLoopController voice;

        public bool Active;                 // reps are only counted while a set is in progress

        public float CurrentDrop { get; private set; }   // live drop below standing, as a fraction (for the depth meter)
        public int RepCount { get; private set; }
        public float SecondsPerRep { get; private set; }

        // Depth thresholds as a FRACTION of the user's standing head height, so they scale with the person.
        // About 0.20 is a half squat (35 cm on a 1.65 m headset height), about 0.33 is a full squat (55 cm).
        public float downFraction = 0.16f;   // must drop this far to count as "down" (about 23 cm for an average adult)
        public float upFraction = 0.06f;     // and come back within this to count as "back up" (smaller = hysteresis gap)

        // Height of the neck pivot, not the eyes: looking up or down swings the headset around the neck by a few cm,
        // which made borderline squats count only when the user tilted their head (found on-device Oct 27).
        float HeadHeight()
        {
            Vector3 neck = head.TransformPoint(new Vector3(0f, -0.10f, -0.08f));   // ~10 cm below and ~8 cm behind the eyes
            return head.parent != null ? head.parent.InverseTransformPoint(neck).y : neck.y;
        }

        float standingHeight;
        float lastRepTime;
        bool inBottom;

        void Start()
        {
            standingHeight = HeadHeight();   // rough default; call Calibrate() for a proper reading
            lastRepTime = Time.time;
        }

        /// Samples head height for a moment while the user stands tall (tracking origin is the floor on Quest, so
        /// localPosition.y is their real head height). Call at the start of each set, e.g. after "stand tall".
        public System.Collections.IEnumerator Calibrate(float seconds = 2f)
        {
            float sum = 0f; int n = 0;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                sum += HeadHeight(); n++;
                yield return null;
            }
            if (n > 0) standingHeight = sum / n;
            RepCount = 0;
            inBottom = false;
            lastRepTime = Time.time;
        }

        void Update()
        {
            if (!Active) return;
            float dropFraction = (standingHeight - HeadHeight()) / standingHeight;
            CurrentDrop = dropFraction;
            bool wasInBottom = inBottom;
            bool repDone = DetectRepStep(dropFraction, ref inBottom);
            if (!wasInBottom && inBottom) DepthCue();   // the moment the squat is deep enough to count
            if (repDone)
            {
                RepCount++;
                SecondsPerRep = (Time.time - lastRepTime) / 1f;
                lastRepTime = Time.time;
                if (voice != null && RepCount % 5 == 0)
                    voice.CoachProactively($"The user just completed rep {RepCount}.");
            }
        }

        // ── Depth cue: a short tick plus a controller buzz when you pass the "down" line, so you know the rep will count.
        AudioSource beeper;
        AudioClip tick;

        void Awake()
        {
            const int rate = 24000;
            var samples = new float[(int)(rate * 0.09f)];
            for (int i = 0; i < samples.Length; i++)
            {
                float fade = 1f - (float)i / samples.Length;            // quick fade-out avoids a click
                samples[i] = Mathf.Sin(2f * Mathf.PI * 880f * i / rate) * 0.35f * fade;
            }
            tick = AudioClip.Create("depth-tick", samples.Length, 1, rate, false);
            tick.SetData(samples, 0);
            beeper = gameObject.AddComponent<AudioSource>();
            beeper.playOnAwake = false;
            beeper.spatialBlend = 0f;
        }

        void DepthCue()
        {
            beeper.PlayOneShot(tick);
            StartCoroutine(Buzz(0.12f));
        }

        System.Collections.IEnumerator Buzz(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0.6f, 0.6f, OVRInput.Controller.RTouch);
                yield return null;
            }
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        /// Called every frame with how far the head has dropped below standing height, as a FRACTION of that
        /// height (0.20 = 20% down). Return true on the frame a full rep completes (down, then back up).
        /// `inBottom` is state you own across frames. Use the downFraction / upFraction fields above.
        bool DetectRepStep(float dropFraction, ref bool inBottom)
        {
if (!inBottom && dropFraction >= downFraction)
{
    inBottom = true;      // you've gone down: remember it
    return false;         // not a full rep yet
}

if (inBottom && dropFraction <= upFraction)
{
    inBottom = false;     // you're back up: clear the note
    return true;          // that's one rep
}

return false;             // nothing happened this frame
        }
    }
}
