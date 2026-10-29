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
        readonly Transform[] thumb = new Transform[2], upperArm = new Transform[2], foreArm = new Transform[2], elbow = new Transform[2], hand = new Transform[2];

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
            neck = Part(PrimitiveType.Capsule, "Neck", Pearl, 0, 0.85f, 0.1f, null, NeckShape());
            torso = Part(PrimitiveType.Capsule, "Torso", Suit, 0, 0.8f, 0.35f, null, Trunk());
            hipBall = Part(PrimitiveType.Sphere, "Hips", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
            shoulders = Part(PrimitiveType.Capsule, "Shoulders", Suit);

            for (int i = 0; i < 2; i++)
            {
                thigh[i] = Part(PrimitiveType.Capsule, "Thigh", Suit, 0, 0.8f, 0.35f, null, Thigh());
                shin[i] = Part(PrimitiveType.Capsule, "Shin", Suit, 0, 0.8f, 0.35f, null, Calf());
                knee[i] = Part(PrimitiveType.Sphere, "Knee", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
                foot[i] = Part(PrimitiveType.Sphere, "Foot", Dark, 0, 0.8f, 0.4f);
                upperArm[i] = Part(PrimitiveType.Capsule, "Upper arm", Suit, 0, 0.8f, 0.35f, null, UpperArm());
                foreArm[i] = Part(PrimitiveType.Capsule, "Forearm", Suit, 0, 0.8f, 0.35f, null, Forearm());
                elbow[i] = Part(PrimitiveType.Sphere, "Elbow", Joint, 0, 0.9f, 0.3f, Joint * 0.6f);
                hand[i] = Part(PrimitiveType.Capsule, "Hand", Pearl, 0, 0.9f, 0.1f, null, Palm());
                thumb[i] = Part(PrimitiveType.Capsule, "Thumb", Pearl, 0, 0.9f, 0.1f, null, Thumb());
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
            Place(headBall, head.TransformPoint(new Vector3(0f, 0.02f, -0.07f)), head.rotation, new Vector3(0.17f, 0.235f, 0.20f));
            Place(visor, head.TransformPoint(new Vector3(0f, 0.0f, 0.035f)), head.rotation, new Vector3(0.19f, 0.06f, 0.05f));
            Bone(neck, head.TransformPoint(new Vector3(0f, -0.09f, -0.07f)), shoulder, 0.05f);
            Body(torso, hip, shoulder, 0.17f * n / 1.65f, 0.105f * n / 1.65f, bodyForward);
            Place(hipBall, hip, Quaternion.identity, Vector3.one * 0.09f);
            Bone(shoulders, shoulder - right * (0.115f * n), shoulder + right * (0.115f * n), 0.045f);
            shoulders.rotation = Quaternion.FromToRotation(Vector3.up, right);

            // Legs: two-bone IK from the hips down to the planted feet; knees point the way you face.
            float leg = 0.30f * n;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 hipJoint = hip + right * (side * 0.07f * n);
                Vector3 footTarget = feetCenter + right * (side * 0.09f * n);
                Vector3 kneePos = SolveJoint(hipJoint, footTarget, leg, leg, bodyForward, out Vector3 ankle);
                Bone(thigh[i], hipJoint, kneePos, 0.085f);
                Bone(shin[i], kneePos, ankle, 0.062f);
                Place(knee[i], kneePos, Quaternion.identity, Vector3.one * 0.065f);
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
                Bone(upperArm[i], shoulderJoint, elbowPos, 0.052f);
                Bone(foreArm[i], elbowPos, wrist, 0.045f);
                Place(elbow[i], elbowPos, Quaternion.identity, Vector3.one * 0.05f);
                // A flat palm with fingers continuing along the forearm, and a thumb angled out from the side facing the body.
                Vector3 along = (wrist - elbowPos).sqrMagnitude > 1e-6f ? (wrist - elbowPos).normalized : -Vector3.up;
                Vector3 inward = -right * side;
                Body(hand[i], wrist, wrist + along * 0.105f, 0.042f, 0.017f, inward);
                Vector3 thumbBase = wrist + along * 0.025f + inward * 0.03f;
                Bone(thumb[i], thumbBase, thumbBase + (along * 0.8f + inward * 0.45f).normalized * 0.06f, 0.0125f);
            }
        }

        // ── Body shapes ──────────────────────────────────────────────────────────
        // Each limb is a "lathe" mesh: a smooth profile (width along its length) spun round an axis, so a thigh is wide at the hip,
        // a calf bulges, and the torso swells at the chest. Meshes have the same size as the Capsule they replace (height 2,
        // radius 0.5), so Bone() stretches them exactly as before.
        static readonly System.Collections.Generic.Dictionary<string, Mesh> meshes = new System.Collections.Generic.Dictionary<string, Mesh>();

        static Mesh Shape(string key, params (float t, float r)[] profile)
        {
            if (meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            const int around = 20, rings = 28;
            const float cap = 0.07f;     // fraction of the length given to each rounded end
            float Radius(float t)
            {
                float first = profile[0].r, last = profile[profile.Length - 1].r;
                if (t < cap) return first * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((cap - t) / cap, 2f)));
                if (t > 1f - cap) return last * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - (1f - cap)) / cap, 2f)));
                float u = Mathf.InverseLerp(cap, 1f - cap, t);
                // Catmull-Rom through the profile points (u runs 0..1 across them).
                float f = u * (profile.Length - 1);
                int i = Mathf.Min((int)f, profile.Length - 2);
                float k = f - i;
                float p0 = profile[Mathf.Max(i - 1, 0)].r, p1 = profile[i].r, p2 = profile[i + 1].r, p3 = profile[Mathf.Min(i + 2, profile.Length - 1)].r;
                return Mathf.Max(0f, 0.5f * ((2f * p1) + (-p0 + p2) * k + (2f * p0 - 5f * p1 + 4f * p2 - p3) * k * k + (-p0 + 3f * p1 - 3f * p2 + p3) * k * k * k));
            }

            var verts = new Vector3[(rings + 1) * (around + 1)];
            var norms = new Vector3[verts.Length];
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings;
                float y = -1f + 2f * t;
                float r = 0.5f * Radius(t);
                float dr = 0.5f * (Radius(Mathf.Min(1f, t + 0.01f)) - Radius(Mathf.Max(0f, t - 0.01f))) / 0.02f / 2f;   // radius change per unit of y
                for (int j = 0; j <= around; j++)
                {
                    float a = 2f * Mathf.PI * j / around;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    int idx = i * (around + 1) + j;
                    verts[idx] = new Vector3(c * r, y, sn * r);
                    norms[idx] = new Vector3(c, -dr, sn).normalized;
                    if (i < rings && j < around)
                    {
                        int next = idx + around + 1;
                        tris.Add(idx); tris.Add(next); tris.Add(idx + 1);
                        tris.Add(idx + 1); tris.Add(next); tris.Add(next + 1);
                    }
                }
            }
            var mesh = new Mesh { name = key, vertices = verts, normals = norms };
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            meshes[key] = mesh;
            return mesh;
        }

        static Mesh Thigh() => Shape("thigh", (0f, 0.55f), (0.15f, 1f), (0.5f, 0.8f), (1f, 0.5f));
        static Mesh Calf() => Shape("calf", (0f, 0.55f), (0.3f, 1f), (0.7f, 0.55f), (1f, 0.4f));
        static Mesh UpperArm() => Shape("upperarm", (0f, 0.8f), (0.25f, 1f), (0.7f, 0.8f), (1f, 0.65f));
        static Mesh Forearm() => Shape("forearm", (0f, 0.75f), (0.2f, 1f), (0.85f, 0.55f), (1f, 0.5f));
        static Mesh Trunk() => Shape("trunk", (0f, 0.8f), (0.25f, 0.62f), (0.7f, 1f), (1f, 0.62f));
        static Mesh Palm() => Shape("palm", (0f, 0.5f), (0.3f, 1f), (0.65f, 0.9f), (1f, 0.62f));
        static Mesh Thumb() => Shape("thumb", (0f, 0.85f), (0.5f, 1f), (1f, 0.7f));
        static Mesh NeckShape() => Shape("neck", (0f, 0.9f), (0.5f, 0.7f), (1f, 0.8f));

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

        /// Like Bone, but faces the way the body faces and is wider than it is deep (for the trunk).
        static void Body(Transform t, Vector3 a, Vector3 b, float halfWidth, float halfDepth, Vector3 facing)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            t.position = (a + b) * 0.5f;
            if (len > 1e-4f)
            {
                Vector3 dir = d / len;
                Vector3 f = Vector3.ProjectOnPlane(facing, dir);
                t.rotation = Quaternion.LookRotation(f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward, dir);
            }
            t.localScale = new Vector3(halfWidth * 2f, Mathf.Max(len * 0.5f, 0.001f), halfDepth * 2f);
        }

        static void Place(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            t.SetPositionAndRotation(pos, rot);
            t.localScale = scale;
        }

        Transform Part(PrimitiveType type, string name, Color color, int layer = 0, float smooth = 0.8f, float metal = 0.35f, Color? glow = null, Mesh mesh = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(root, false);
            Destroy(go.GetComponent<Collider>());
            if (mesh != null) go.GetComponent<MeshFilter>().sharedMesh = mesh;
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
