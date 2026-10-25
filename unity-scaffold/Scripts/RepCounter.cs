using UnityEngine;

namespace ABetterU.Coach
{
    /// Counts squat reps from the headset's vertical position (the Quest has no body tracking,
    /// but head height tracks squat depth well). Feeds VoiceLoopController with set context.
    public class RepCounter : MonoBehaviour
    {
        public Transform head;              // the XR camera
        public VoiceLoopController voice;

        public int RepCount { get; private set; }
        public float SecondsPerRep { get; private set; }

        float standingHeight;
        float lastRepTime;
        bool inBottom;

        void Start()
        {
            standingHeight = head.localPosition.y; // user should stand upright when the scene starts
            lastRepTime = Time.time;
        }

        void Update()
        {
            float drop = standingHeight - head.localPosition.y;
            if (DetectRepStep(drop, ref inBottom))
            {
                RepCount++;
                SecondsPerRep = (Time.time - lastRepTime) / 1f;
                lastRepTime = Time.time;
                if (voice != null && RepCount % 5 == 0)
                    voice.CoachProactively($"The user just completed rep {RepCount}.");
            }
        }

        /// Called every frame with how far the head has dropped below standing height (metres).
        /// Return true on the frame a full rep completes (down, then back up). `inBottom` is state
        /// you own across frames.
        bool DetectRepStep(float drop, ref bool inBottom)
        {
            // TODO(human): implement squat rep detection.
            return false;
        }
    }
}
