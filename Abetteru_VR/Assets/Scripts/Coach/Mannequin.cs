using UnityEngine;

namespace ABetterU.Coach
{
    /// A stylised body for the user, built from primitives and driven by what the Quest really tracks:
    /// the head and the two controllers. Everything else (torso lean, hips, knees, elbows) is inferred.
    /// Proportions scale with the user's measured standing height, so it fits the squat counter's numbers.
    public class Mannequin : MonoBehaviour
    {
        public const int MirrorOnlyLayer = 30;     // head and visor: seen in the mirror, hidden from your own eyes

        // Dark glossy suit, pearl head and hands, and A Better U teal that glows at the joints and visor.
        static readonly Color Suit = new Color(0.085f, 0.115f, 0.15f);
        static readonly Color Joint = new Color(0.30f, 0.95f, 0.85f);       // the glowing accent
        static readonly Color Pearl = new Color(0.86f, 0.90f, 0.95f);
        static readonly Color Dark = new Color(0.03f, 0.04f, 0.055f);

        Transform head, leftAnchor, rightAnchor;
        Material template;
        Transform root;

        Transform headBall, visor, neck, torso, hipBall, shoulders;
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], knee = new Transform[2], foot = new Transform[2];
        readonly Transform[] upperArm = new Transform[2], foreArm = new Transform[2], elbow = new Transform[2], hand = new Transform[2];

        float standing;                  // standing neck height, tracked as a slowly decaying maximum
        Vector3 bodyForward = Vector3.forward;
        Vector3 feetCenter;
        bool feetPlaced;

        public void Init(Material template, Transform head)
        {
            this.template = template;
            this.head = head;
            leftAnchor = FindAny("LeftControllerAnchor", "LeftHandAnchor");
            rightAnchor = FindAny("RightControllerAnchor", "RightHandAnchor");

            root = new GameObject("Mannequin").transform;

            headBall = Part(PrimitiveType.Sphere, "Head", Pearl, MirrorOnlyLayer, 0.9f, 0.1f);
            visor = Part(PrimitiveType.Sphere, "Visor", Dark, MirrorOnlyLayer, 0.95f, 0.5f, Joint * 0.9f);
            neck = Part(PrimitiveType.Capsule, "Neck", Pearl, 0, 0.85f, 0.1f);
            torso = Part(PrimitiveType.Capsule, "Torso", Suit);
            hipBall = Part(PrimitiveType.Sphere, "Hips", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
            shoulders = Part(PrimitiveType.Capsule, "Shoulders", Suit);

            for (int i = 0; i < 2; i++)
            {
                thigh[i] = Part(PrimitiveType.Capsule, "Thigh", Suit);
                shin[i] = Part(PrimitiveType.Capsule, "Shin", Suit);
                knee[i] = Part(PrimitiveType.Sphere, "Knee", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
                foot[i] = Part(PrimitiveType.Sphere, "Foot", Dark, 0, 0.8f, 0.4f);
                upperArm[i] = Part(PrimitiveType.Capsule, "Upper arm", Suit);
                foreArm[i] = Part(PrimitiveType.Capsule, "Forearm", Suit);
                elbow[i] = Part(PrimitiveType.Sphere, "Elbow", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
                hand[i] = Part(PrimitiveType.Sphere, "Hand", Pearl, 0, 0.9f, 0.1f);
            }
        }

        void LateUpdate()
        {
            if (head == null || root == null) return;
            float dt = Time.deltaTime;

            // The neck pivot, not the eyes: it barely moves when you nod, so the body does not wobble.
            Vector3 neckPos = head.TransformPoint(new Vector3(0f, -0.10f, -0.08f));

            // Standing height: slow-decaying maximum, with a floor so proportions are sane before anything is measured.
            standing = Mathf.Max(neckPos.y, standing - 0.004f * dt);
            float n = Mathf.Max(standing, 0.9f);

            // Body faces where the head faces on the ground plane, smoothed so it does not twitch.
            Vector3 flat = head.forward; flat.y = 0f;
            if (flat.sqrMagnitude > 0.05f)
                bodyForward = Vector3.Slerp(bodyForward, flat.normalized, 1f - Mathf.Exp(-6f * dt)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, bodyForward);

            // The deeper the squat, the more the torso leans forward (hips go back and down).
            float drop = Mathf.Clamp01((standing - neckPos.y) / (0.45f * n));
            float lean = Mathf.Lerp(0f, 0.8f, drop);
            float torsoLen = 0.36f * n;
            Vector3 shoulder = neckPos - Vector3.up * (0.04f * n);
            Vector3 hip = shoulder - (Vector3.up * Mathf.Cos(lean) + bodyForward * Mathf.Sin(lean)) * torsoLen;

            // Feet stay planted; they drift under you slowly if you walk, and snap if you move far.
            Vector3 target = new Vector3(neckPos.x, 0.03f, neckPos.z);
            if (!feetPlaced) { feetCenter = target; feetPlaced = true; }
            float away = Vector2.Distance(new Vector2(feetCenter.x, feetCenter.z), new Vector2(target.x, target.z));
            feetCenter = away > 0.5f ? target : Vector3.Lerp(feetCenter, target, 1f - Mathf.Exp(-1.2f * dt));

            // Head, neck, torso.
            Place(headBall, head.TransformPoint(new Vector3(0f, 0.02f, -0.07f)), head.rotation, Vector3.one * 0.22f);
            Place(visor, head.TransformPoint(new Vector3(0f, 0.0f, 0.035f)), head.rotation, new Vector3(0.19f, 0.06f, 0.05f));
            Bone(neck, head.TransformPoint(new Vector3(0f, -0.09f, -0.07f)), shoulder, 0.045f);
            Bone(torso, hip, shoulder, 0.13f);
            Place(hipBall, hip, Quaternion.identity, Vector3.one * 0.2f);
            Bone(shoulders, shoulder - right * (0.125f * n), shoulder + right * (0.125f * n), 0.06f);
            shoulders.rotation = Quaternion.FromToRotation(Vector3.up, right);

            // Legs: two-bone IK from the hips down to the planted feet; knees point the way you face.
            float leg = 0.30f * n;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 hipJoint = hip + right * (side * 0.07f * n);
                Vector3 footTarget = feetCenter + right * (side * 0.09f * n);
                Vector3 kneePos = SolveJoint(hipJoint, footTarget, leg, leg, bodyForward, out Vector3 ankle);
                Bone(thigh[i], hipJoint, kneePos, 0.06f);
                Bone(shin[i], kneePos, ankle, 0.05f);
                Place(knee[i], kneePos, Quaternion.identity, Vector3.one * 0.11f);
                Place(foot[i], ankle + bodyForward * 0.06f + Vector3.down * 0.02f, Quaternion.LookRotation(bodyForward), new Vector3(0.09f, 0.05f, 0.24f));
            }

            // Arms: shoulder to controller (or hanging at the sides if a controller is not tracked).
            float upper = 0.18f * n, fore = 0.17f * n;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Transform anchor = i == 0 ? leftAnchor : rightAnchor;
                Vector3 shoulderJoint = shoulder + right * (side * 0.125f * n);
                Vector3 handTarget = anchor != null && anchor.gameObject.activeInHierarchy
                    ? anchor.position
                    : shoulderJoint - Vector3.up * ((upper + fore) * 0.96f);
                Vector3 pole = (-Vector3.up * 0.8f + right * (side * 0.4f) - bodyForward * 0.2f).normalized;
                Vector3 elbowPos = SolveJoint(shoulderJoint, handTarget, upper, fore, pole, out Vector3 wrist);
                Bone(upperArm[i], shoulderJoint, elbowPos, 0.045f);
                Bone(foreArm[i], elbowPos, wrist, 0.04f);
                Place(elbow[i], elbowPos, Quaternion.identity, Vector3.one * 0.08f);
                Place(hand[i], wrist, Quaternion.identity, Vector3.one * 0.09f);
            }
        }

        // ── Maths helpers ────────────────────────────────────────────────────────

        /// Two-bone IK. Returns the middle joint; `end` is where the second bone really ends (clamped to reach).
        /// `pole` says which way the joint bends (forward for knees, down and out for elbows).
        static Vector3 SolveJoint(Vector3 start, Vector3 target, float l1, float l2, Vector3 pole, out Vector3 end)
        {
            Vector3 d = target - start;
            if (d.sqrMagnitude < 1e-8f) d = Vector3.down * 0.01f;
            float dist = Mathf.Clamp(d.magnitude, 0.01f, l1 + l2 - 0.001f);
            Vector3 dir = d.normalized;
            end = start + dir * dist;

            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);       // distance along the line to the joint's foot point
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));            // how far the joint sticks out sideways

            Vector3 bend = pole - dir * Vector3.Dot(pole, dir);              // pole direction made perpendicular to the limb
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.Cross(dir, Vector3.right).normalized;
            return start + dir * a + bend * h;
        }

        /// Stretches a unit cylinder (height 2, radius 0.5) between two points.
        static void Bone(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            t.position = (a + b) * 0.5f;
            if (len > 1e-4f) t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            t.localScale = new Vector3(radius * 2f, Mathf.Max(len * 0.5f, 0.001f), radius * 2f);
        }

        static void Place(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            t.SetPositionAndRotation(pos, rot);
            t.localScale = scale;
        }

        Transform Part(PrimitiveType type, string name, Color color, int layer = 0, float smooth = 0.8f, float metal = 0.35f, Color? glow = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(root, false);
            Destroy(go.GetComponent<Collider>());
            var m = GymRoom.MakeMaterial(template, color, smooth, metal);
            if (m != null)
            {
                // Emission may be stripped from a device build's shader variants; the base colours still read well without it.
                if (glow.HasValue && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow.Value); }
                go.GetComponent<Renderer>().sharedMaterial = m;
            }
            return go.transform;
        }

        static Transform FindAny(params string[] names)
        {
            foreach (var n in names)
            {
                var go = GameObject.Find(n);
                if (go != null) return go.transform;
            }
            return null;
        }
    }
}
