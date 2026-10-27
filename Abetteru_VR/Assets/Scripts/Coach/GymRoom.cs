using UnityEngine;

namespace ABetterU.Coach
{
    /// Builds the virtual gym at startup from Unity primitives, so there is nothing to place by hand in the scene.
    /// The user stands at the origin on the squat mat, facing +Z (towards the mirror wall). Floor is y = 0, which
    /// matches the FloorLevel tracking origin set in RepCounter. Units are metres.
    public class GymRoom : MonoBehaviour
    {
        // A Better U palette. Teal is the brand accent used across the web app.
        static readonly Color Teal = new Color(0.078f, 0.722f, 0.651f);       // #14B8A6
        static readonly Color Floor = new Color(0.13f, 0.15f, 0.18f);         // dark rubber
        static readonly Color Wall = new Color(0.86f, 0.89f, 0.92f);
        static readonly Color Ceiling = new Color(0.20f, 0.23f, 0.28f);
        static readonly Color Metal = new Color(0.55f, 0.58f, 0.63f);
        static readonly Color Black = new Color(0.07f, 0.08f, 0.10f);
        static readonly Color Glass = new Color(0.50f, 0.62f, 0.72f);

        const float Half = 3.5f;       // room is 7 m x 7 m
        const float Height = 3.2f;

        Transform root;

        void Start()
        {
            // The scene's original test objects would poke through the new room.
            foreach (var name in new[] { "Cube", "Plane" })
            {
                var old = GameObject.Find(name);
                if (old != null) old.SetActive(false);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);

            root = new GameObject("Gym").transform;
            BuildShell();
            BuildMirrorWall();
            BuildMat();
            BuildDumbbellRack(-Half + 0.45f);
            BuildSquatRack(Half - 0.9f, 1.4f);
            BuildBenches();
            BuildSign();
        }

        // ── Shell ────────────────────────────────────────────────────────────────

        void BuildShell()
        {
            Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(Half * 2, 0.1f, Half * 2), Floor, 0.35f);
            Box("Ceiling", new Vector3(0, Height + 0.05f, 0), new Vector3(Half * 2, 0.1f, Half * 2), Ceiling);
            Box("Wall front", new Vector3(0, Height / 2, -Half), new Vector3(Half * 2, Height, 0.1f), Wall);
            Box("Wall left", new Vector3(-Half, Height / 2, 0), new Vector3(0.1f, Height, Half * 2), Wall);
            Box("Wall right", new Vector3(Half, Height / 2, 0), new Vector3(0.1f, Height, Half * 2), Wall);
            Box("Wall back", new Vector3(0, Height / 2, Half), new Vector3(Half * 2, Height, 0.1f), Wall);

            // Teal stripe around the room at chest height.
            Box("Stripe left", new Vector3(-Half + 0.06f, 1.3f, 0), new Vector3(0.02f, 0.12f, Half * 2), Teal);
            Box("Stripe right", new Vector3(Half - 0.06f, 1.3f, 0), new Vector3(0.02f, 0.12f, Half * 2), Teal);
            Box("Stripe front", new Vector3(0, 1.3f, -Half + 0.06f), new Vector3(Half * 2, 0.12f, 0.02f), Teal);

            // Ceiling light panels (bright flat colour; a real light comes from the scene's directional light).
            for (int i = -1; i <= 1; i++)
                Box("Light panel " + i, new Vector3(i * 2f, Height - 0.01f, 0), new Vector3(1.4f, 0.02f, 3.6f), new Color(1f, 1f, 0.96f));
        }

        // ── Mirror wall (a glossy panel; real reflections are too heavy for a standalone headset) ──

        void BuildMirrorWall()
        {
            float z = Half - 0.07f;
            Box("Mirror frame", new Vector3(0, 1.3f, z + 0.02f), new Vector3(5.2f, 2.3f, 0.04f), Black);
            Box("Mirror", new Vector3(0, 1.3f, z), new Vector3(5.0f, 2.1f, 0.03f), Glass, 0.95f, 0.6f);
            // Soft diagonal highlights so the panel reads as glass.
            for (int i = 0; i < 3; i++)
            {
                var streak = Box("Mirror shine " + i, new Vector3(-1.4f + i * 1.4f, 1.3f, z - 0.02f), new Vector3(0.18f, 2.0f, 0.005f), new Color(0.85f, 0.92f, 0.98f), 1f, 0f);
                streak.localRotation = Quaternion.Euler(0, 0, 14f);
            }
        }

        // ── Squat mat under the user's feet ─────────────────────────────────────

        void BuildMat()
        {
            Box("Mat", new Vector3(0, 0.01f, 0), new Vector3(1.4f, 0.02f, 2.2f), Teal, 0.2f);
            Box("Mat edge", new Vector3(0, 0.0105f, 0), new Vector3(1.3f, 0.02f, 2.1f), new Color(0.05f, 0.55f, 0.50f), 0.2f);
        }

        // ── Dumbbell rack along the left wall ───────────────────────────────────

        void BuildDumbbellRack(float x)
        {
            Box("Rack frame", new Vector3(x, 0.45f, 0.4f), new Vector3(0.5f, 0.9f, 3.0f), Black);
            Box("Rack shelf", new Vector3(x, 0.62f, 0.4f), new Vector3(0.55f, 0.04f, 3.0f), Metal);

            for (int i = 0; i < 6; i++)
            {
                float z = -0.85f + i * 0.5f;
                float size = 0.07f + i * 0.012f;           // heavier dumbbells get bigger discs
                Dumbbell(new Vector3(x, 0.74f, 0.4f + z), size);
            }
        }

        void Dumbbell(Vector3 pos, float discRadius)
        {
            var bar = Shape(PrimitiveType.Cylinder, "Dumbbell bar", pos, new Vector3(0.035f, 0.17f, 0.035f), Metal, 0.9f);
            bar.localRotation = Quaternion.Euler(0, 0, 90f);
            for (int s = -1; s <= 1; s += 2)
            {
                var disc = Shape(PrimitiveType.Cylinder, "Dumbbell disc", pos + new Vector3(s * 0.16f, 0, 0), new Vector3(discRadius * 2f, 0.04f, discRadius * 2f), Black, 0.5f);
                disc.localRotation = Quaternion.Euler(0, 0, 90f);
            }
        }

        // ── Squat rack on the right ─────────────────────────────────────────────

        void BuildSquatRack(float x, float z)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box("Rack upright", new Vector3(x + sx * 0.55f, 1.1f, z + sz * 0.45f), new Vector3(0.08f, 2.2f, 0.08f), Black, 0.6f);
            Box("Rack top", new Vector3(x, 2.2f, z), new Vector3(1.2f, 0.08f, 1.0f), Black, 0.6f);

            var bar = Shape(PrimitiveType.Cylinder, "Barbell", new Vector3(x, 1.45f, z), new Vector3(0.03f, 1.1f, 0.03f), Metal, 0.95f);
            bar.localRotation = Quaternion.Euler(0, 0, 90f);
            for (int s = -1; s <= 1; s += 2)
            {
                var plate = Shape(PrimitiveType.Cylinder, "Plate", new Vector3(x + s * 0.5f, 1.45f, z), new Vector3(0.42f, 0.04f, 0.42f), Teal, 0.5f);
                plate.localRotation = Quaternion.Euler(0, 0, 90f);
            }
        }

        // ── Benches near the front wall ─────────────────────────────────────────

        void BuildBenches()
        {
            for (int i = -1; i <= 1; i += 2)
            {
                Box("Bench seat", new Vector3(i * 1.6f, 0.45f, -Half + 0.9f), new Vector3(1.2f, 0.1f, 0.4f), Black, 0.4f);
                Box("Bench leg", new Vector3(i * 1.6f - 0.45f, 0.2f, -Half + 0.9f), new Vector3(0.06f, 0.4f, 0.3f), Metal, 0.8f);
                Box("Bench leg", new Vector3(i * 1.6f + 0.45f, 0.2f, -Half + 0.9f), new Vector3(0.06f, 0.4f, 0.3f), Metal, 0.8f);
            }
        }

        // ── Sign above the mirror ───────────────────────────────────────────────

        void BuildSign()
        {
            var go = new GameObject("Sign");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0, 2.75f, Half - 0.1f);
            // TextMesh text is readable when viewed along +Z, which is how the user faces the mirror wall; no rotation needed.

            var text = go.AddComponent<TextMesh>();
            text.text = "A BETTER U";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 120;
            text.characterSize = 0.05f;
            text.color = Teal;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        Transform Box(string name, Vector3 pos, Vector3 scale, Color color, float smoothness = 0.15f, float metallic = 0f) =>
            Shape(PrimitiveType.Cube, name, pos, scale, color, smoothness, metallic);

        Transform Shape(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());                       // nothing needs physics; keeps the headset light

            var r = go.GetComponent<Renderer>();
            r.material.color = color;
            if (r.material.HasProperty("_Smoothness")) r.material.SetFloat("_Smoothness", smoothness);
            if (r.material.HasProperty("_Metallic")) r.material.SetFloat("_Metallic", metallic);
            return go.transform;
        }
    }
}
