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
        public bool InBottom => inBottom;                // true from passing the "down" line until back up
        public float DeepestDrop { get; private set; }   // deepest drop this set, as a fraction (for the summary card)
        public float SetSeconds => Time.time - setStartedAt;

        // Depth thresholds as a FRACTION of the user's standing head height, so they scale with the person.
        // About 0.20 is a half squat (35 cm on a 1.65 m headset height), about 0.33 is a full squat (55 cm).
        public float downFraction = 0.16f;   // must drop this far to count as "down" (about 23 cm for an average adult)
        public float upFraction = 0.06f;     // and come back within this to count as "back up" (smaller = hysteresis gap)

        // Height of the neck pivot, not the eyes: looking up or down swings the headset around the neck by a few cm,
        // which made borderline squats count only when the user tilted their head (found on-device Oct 27).
        Vector3 NeckPoint()
        {
            Vector3 neck = head.TransformPoint(new Vector3(0f, -0.10f, -0.08f));   // ~10 cm below and ~8 cm behind the eyes
            return head.parent != null ? head.parent.InverseTransformPoint(neck) : neck;
        }

        float HeadHeight() => NeckPoint().y;

        // ── Lean and look-down: form feedback from head position only (the Quest cannot see the back or knees). ──
        // Rep counting above is unchanged; these only drive the on-screen cue and the summary. Starting thresholds, tune on-device.
        public float leanLimitMetres = 0.20f;     // neck this far forward of where you stood = leaning forward
        public float lookDownLimitDeg = 40f;      // head pitched this far below level = looking down at the floor
        public float LeanMetres { get; private set; }
        public float LookDownDegrees { get; private set; }
        public bool LeaningForward => LeanMetres > leanLimitMetres;
        public bool LeaningBack => LeanMetres < -leanLimitMetres;
        public bool LookingDown => LookDownDegrees > lookDownLimitDeg;
        /// True while the body is out of line (leaning either way or head tilted down) during a squat.
        public bool BadForm => CurrentDrop > 0.04f && (LeaningForward || LeaningBack || LookingDown);
        public int FlaggedReps { get; private set; }     // movements NOT counted because the head leaned or dipped
        Vector3 standingPos, standingForward = Vector3.forward;
        bool repFlagged;
        float repMaxLean, repMaxLookDown;     // peaks within the current rep, logged to tune the limits

        float standingHeight;
        float lastRepTime, setStartedAt;
        bool inBottom;

        // The Meta camera rig prefab defaults to EyeLevel tracking, where head height reads ~0 at app start
        // instead of the real height above the floor (found on-device Oct 27). Ask for FloorLevel so heights are real.
        void EnsureFloorLevel()
        {
            if (OVRManager.instance != null && OVRManager.instance.trackingOriginType != OVRManager.TrackingOrigin.FloorLevel)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }

        // Fractions need a believable denominator. If the origin is still eye level (height ~0), assume 1.6 m
        // instead of dividing by nearly zero.
        float Scale => standingHeight > 0.8f ? standingHeight : 1.6f;

        void Start()
        {
            EnsureFloorLevel();
            standingHeight = HeadHeight();   // rough default; call Calibrate() for a proper reading
            lastRepTime = Time.time;
        }

        /// Samples head height for a moment while the user stands tall (tracking origin is the floor on Quest, so
        /// localPosition.y is their real head height). Call at the start of each set, e.g. after "stand tall".
        public System.Collections.IEnumerator Calibrate(float seconds = 2f)
        {
            EnsureFloorLevel();
            yield return null;               // let the origin change take effect before sampling
            float sum = 0f; int n = 0;
            Vector3 posSum = Vector3.zero;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                sum += HeadHeight(); n++;
                posSum += NeckPoint();
                yield return null;
            }
            if (n > 0) { standingHeight = sum / n; standingPos = posSum / n; }

            // "Forward" is the way you faced while standing, in the rig's own space so it matches the neck points.
            Vector3 f = head.parent != null ? head.parent.InverseTransformDirection(head.forward) : head.forward;
            f.y = 0f;
            standingForward = f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
            FlaggedReps = 0; repFlagged = false;
            Debug.Log($"[Squat] calibrated standing neck height {standingHeight:0.00} m (origin {(OVRManager.instance != null ? OVRManager.instance.trackingOriginType.ToString() : "n/a")})");
            RepCount = 0;
            DeepestDrop = 0f;
            inBottom = false;
            lastRepTime = setStartedAt = Time.time;
        }

        void Update()
        {
            if (!Active) return;
            float dropFraction = (standingHeight - HeadHeight()) / Scale;
            CurrentDrop = dropFraction;
            if (dropFraction > DeepestDrop) DeepestDrop = dropFraction;

            // Re-baseline while upright, so stepping back or forward between reps cannot hide a lean (or fake one).
            // It only follows small drift (under 10 cm), so a slow lean forward is never absorbed into "standing".
            Vector3 neck = NeckPoint();
            Vector3 away = neck - standingPos; away.y = 0f;
            float lean = Vector3.Dot(away, standingForward);
            if (!inBottom && dropFraction < 0.03f && Mathf.Abs(lean) < 0.10f)
            {
                float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
                standingPos = Vector3.Lerp(standingPos, new Vector3(neck.x, standingPos.y, neck.z), k);
                Vector3 f = head.parent != null ? head.parent.InverseTransformDirection(head.forward) : head.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.01f) standingForward = Vector3.Slerp(standingForward, f.normalized, k).normalized;
                away = neck - standingPos; away.y = 0f;
                lean = Vector3.Dot(away, standingForward);
            }
            LeanMetres = lean;
            LookDownDegrees = -Mathf.Asin(Mathf.Clamp(head.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (dropFraction > 0.04f)
            {
                repMaxLean = Mathf.Max(repMaxLean, Mathf.Abs(LeanMetres));
                repMaxLookDown = Mathf.Max(repMaxLookDown, LookDownDegrees);
            }
            if (BadForm) repFlagged = true;   // judged only while actually squatting
            else if (!inBottom && dropFraction < 0.03f) repFlagged = false;   // stood back up without a rep: start the next one clean

            bool wasInBottom = inBottom;
            bool repDone = DetectRepStep(dropFraction, ref inBottom);
            if (!wasInBottom && inBottom && !BadForm) DepthCue();   // the moment the squat is deep enough to count
            if (repDone)
            {
                bool rejected = repFlagged;     // leaned or tilted at any point: bending over is not a squat
                repFlagged = false;
                Debug.Log($"[Squat] {(rejected ? "REJECTED" : "counted")}: deepest {DeepestDrop * 100f:0}%, peak lean {repMaxLean:0.00} m (limit {leanLimitMetres:0.00}), peak look-down {repMaxLookDown:0} deg (limit {lookDownLimitDeg:0})");
                repMaxLean = repMaxLookDown = 0f;
                if (rejected) { FlaggedReps++; lastRepTime = Time.time; return; }
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
