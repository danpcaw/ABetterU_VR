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
        Material template;     // a known-good URP Lit material; CreatePrimitive's default renders magenta on device

        void Start()
        {
            // Borrow a working URP material from the scene's original test objects, then hide them
            // (they would poke through the new room).
            foreach (var name in new[] { "Cube", "Plane" })
            {
                var old = GameObject.Find(name);
                if (old == null) continue;
                var r = old.GetComponent<Renderer>();
                if (template == null && r != null && r.sharedMaterial != null) template = r.sharedMaterial;
                old.SetActive(false);
            }
            if (template == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) template = new Material(lit);
            }
            if (template == null) Debug.LogError("[Gym] No URP material available; the room will render magenta.");
            else Debug.Log($"[Gym] using material template '{template.name}' with shader '{template.shader.name}'");

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
            BuildWallScreen();
            BuildBodyAndReflection();
        }

        // ── Coach screen on the mirror wall: all StatusBoard text (replies, rep count, depth meter) appears here ──

        void BuildWallScreen()
        {
            const float cy = 2.4f, w = 3.0f, h = 0.9f;
            float z = Half - 0.22f;
            Box("Screen bezel", new Vector3(0, cy, z + 0.02f), new Vector3(w + 0.14f, h + 0.14f, 0.05f), Black, 0.6f);
            Box("Screen glass", new Vector3(0, cy, z), new Vector3(w, h, 0.02f), new Color(0.04f, 0.06f, 0.10f), 0.9f);
            Box("Screen accent", new Vector3(0, cy + h / 2 - 0.02f, z - 0.012f), new Vector3(w, 0.03f, 0.005f), Teal);

            var board = FindAnyObjectByType<StatusBoard>();   // only one exists, so "any" is deterministic here
            if (board != null) board.PinToWall(new Vector3(0, cy - 0.02f, z - 0.02f), 1.8f);
            else Debug.LogWarning("[Gym] No StatusBoard found; coach text stays head-locked.");
        }

        // The user's own stylised body, and the mirror that shows it.
        void BuildBodyAndReflection()
        {
            var eye = Camera.main;
            if (eye == null) { Debug.LogWarning("[Gym] No main camera; skipping body and mirror."); return; }

            // The head ball is on a mirror-only layer: visible in the reflection, hidden from the user's own eyes.
            eye.cullingMask &= ~(1 << Mannequin.MirrorOnlyLayer);

            gameObject.AddComponent<Mannequin>().Init(template, eye.transform);
            gameObject.AddComponent<MirrorReflection>().Init(eye.transform, mirrorDisplay);
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

            // The glass is a quad that shows a second camera's view (MirrorReflection), not a painted panel.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Mirror";
            quad.transform.SetParent(root, false);
            quad.transform.position = new Vector3(0, 1.3f, MirrorPlaneZ - 0.005f);
            // Negative X scale flips the image left-right, as a mirror does (Sprites/Default draws both sides, so culling is no issue).
            quad.transform.localScale = new Vector3(-MirrorXMax * 2f, MirrorYMax - MirrorYMin, 1f);
            quad.layer = MirrorDisplayLayer;                     // hidden from the reflection camera so it cannot reflect itself
            Destroy(quad.GetComponent<Collider>());

            // Sprites/Default is always in builds and unlit, so the reflection is not darkened by scene lighting.
            var shader = Shader.Find("Sprites/Default");
            var rend = quad.GetComponent<Renderer>();
            rend.sharedMaterial = shader != null ? new Material(shader) : MakeMaterial(template, Glass, 0.9f, 0.5f);
            mirrorDisplay = rend;
        }

        // Mirror geometry shared with MirrorReflection. The plane is the glass surface the user faces.
        public const float MirrorXMax = 2.5f, MirrorYMin = 0.25f, MirrorYMax = 2.35f;
        public const int MirrorDisplayLayer = 29;
        public static float MirrorPlaneZ => Half - 0.085f;
        Renderer mirrorDisplay;

        // ── Squat mat under the user's feet ─────────────────────────────────────

        void BuildMat()
        {
            Box("Mat", new Vector3(0, 0.01f, 0), new Vector3(1.4f, 0.02f, 2.2f), Teal, 0.2f);
            Box("Mat edge", new Vector3(0, 0.0105f, 0), new Vector3(1.3f, 0.02f, 2.1f), new Color(0.05f, 0.55f, 0.50f), 0.2f);
        }

        // ── Dumbbell rack along the left wall ───────────────────────────────────

        void BuildDumbbellRack(float x)
        {
            // Open two-tier rack: four legs, two shelves, dumbbells resting on top of each shelf.
            const float zc = 0.4f, len = 3.0f;
            float[] shelves = { 0.38f, 0.78f };

            foreach (float y in shelves)
                Box("Rack shelf", new Vector3(x, y, zc), new Vector3(0.5f, 0.04f, len), Metal, 0.8f, 0.5f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box("Rack leg", new Vector3(x + sx * 0.22f, 0.4f, zc + sz * (len / 2 - 0.04f)), new Vector3(0.05f, 0.8f, 0.05f), Black, 0.6f);

            foreach (float y in shelves)
                for (int i = 0; i < 6; i++)
                {
                    float r = 0.07f + i * 0.012f;                       // heavier dumbbells get bigger discs
                    Dumbbell(new Vector3(x, y + 0.02f + r, zc - 1.25f + i * 0.5f), r);
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

            // Base beams so the frame stands on the floor.
            Box("Rack base front", new Vector3(x, 0.05f, z - 0.45f), new Vector3(2.0f, 0.1f, 0.1f), Black, 0.6f);
            Box("Rack base back", new Vector3(x, 0.05f, z + 0.45f), new Vector3(2.0f, 0.1f, 0.1f), Black, 0.6f);

            // The bar rests on hooks fixed to the two FRONT uprights (the ones nearest the user), with plates outside the frame.
            float barZ = z - 0.45f, barY = 1.45f;
            for (int sx = -1; sx <= 1; sx += 2)
                Box("Rack hook", new Vector3(x + sx * 0.55f, barY - 0.05f, barZ), new Vector3(0.16f, 0.05f, 0.12f), Metal, 0.8f, 0.5f);

            var bar = Shape(PrimitiveType.Cylinder, "Barbell", new Vector3(x, barY, barZ), new Vector3(0.03f, 1.1f, 0.03f), Metal, 0.95f, 0.8f);
            bar.localRotation = Quaternion.Euler(0, 0, 90f);
            for (int s = -1; s <= 1; s += 2)
            {
                var plate = Shape(PrimitiveType.Cylinder, "Plate", new Vector3(x + s * 0.85f, barY, barZ), new Vector3(0.42f, 0.04f, 0.42f), Teal, 0.5f);
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
            go.transform.position = new Vector3(0, 3.02f, Half - 0.1f);   // above the coach screen, under the ceiling
            // TextMesh text is readable when viewed along +Z, which is how the user faces the mirror wall; no rotation needed.

            var text = go.AddComponent<TextMesh>();
            text.text = "A BETTER U";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 120;
            text.characterSize = 0.035f;
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

            var m = MakeMaterial(template, color, smoothness, metallic);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        /// A tinted copy of the known-good URP material (shared with the mannequin).
        public static Material MakeMaterial(Material template, Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            if (template == null) return null;
            var m = new Material(template);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);   // URP Lit
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);           // built-in naming, harmless on URP
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }
    }
}
