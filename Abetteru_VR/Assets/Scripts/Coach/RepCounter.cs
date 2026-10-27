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

        public int RepCount { get; private set; }
        public float SecondsPerRep { get; private set; }

        // Depth thresholds as a FRACTION of the user's standing head height, so they scale with the person.
        // About 0.20 is a half squat (35 cm on a 1.65 m headset height), about 0.33 is a full squat (55 cm).
        public float downFraction = 0.20f;   // head must drop this far to count as "down"
        public float upFraction = 0.08f;     // and come back within this to count as "back up" (smaller = hysteresis gap)

        float standingHeight;
        float lastRepTime;
        bool inBottom;

        void Start()
        {
            standingHeight = head.localPosition.y;   // rough default; call Calibrate() for a proper reading
            lastRepTime = Time.time;
        }

        /// Samples head height for a moment while the user stands tall (tracking origin is the floor on Quest, so
        /// localPosition.y is their real head height). Call at the start of each set, e.g. after "stand tall".
        public System.Collections.IEnumerator Calibrate(float seconds = 2f)
        {
            float sum = 0f; int n = 0;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                sum += head.localPosition.y; n++;
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
            float dropFraction = (standingHeight - head.localPosition.y) / standingHeight;
            if (DetectRepStep(dropFraction, ref inBottom))
            {
                RepCount++;
                SecondsPerRep = (Time.time - lastRepTime) / 1f;
                lastRepTime = Time.time;
                if (voice != null && RepCount % 5 == 0)
                    voice.CoachProactively($"The user just completed rep {RepCount}.");
            }
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
