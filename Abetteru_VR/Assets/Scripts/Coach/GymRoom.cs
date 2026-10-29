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
        static readonly Color Floor = new Color(0.18f, 0.14f, 0.10f);
        static readonly Color Wall = new Color(0.78f, 0.74f, 0.67f);        // warm mineral plaster
        static readonly Color Ceiling = new Color(0.19f, 0.20f, 0.19f);
        static readonly Color Oak = new Color(0.66f, 0.48f, 0.30f);
        static readonly Color Rubber = new Color(0.085f, 0.115f, 0.12f);
        static readonly Color WarmLight = new Color(1f, 0.78f, 0.52f);
        static readonly Color Metal = new Color(0.55f, 0.58f, 0.63f);
        static readonly Color Black = new Color(0.07f, 0.08f, 0.10f);
        static readonly Color Glass = new Color(0.50f, 0.62f, 0.72f);

        const float Half = 3.5f;       // room is 7 m x 7 m
        const float Height = 3.2f;

        Transform root;
        Material template;     // a known-good URP Lit material; CreatePrimitive's default renders magenta on device

        void Start()
        {
            template = AcquireTemplate();
            HideSceneTestObjects();   // the scene's original cube and plane would poke through the new room
            if (template == null) Debug.LogError("[Gym] No URP material available; the room will render magenta.");
            else Debug.Log($"[Gym] using material template '{template.name}' with shader '{template.shader.name}'");

            root = new GameObject("Gym").transform;
            BuildStudioLighting();
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
            // Above the mirror, with a gap: branding, replies and controls have separate rows.
            const float cy = 2.65f, w = 3.6f, h = 0.52f;
            float z = Half - 0.22f;
            Box("Screen bezel", new Vector3(0, cy, z + 0.025f), new Vector3(w + 0.10f, h + 0.06f, 0.045f), Black, 0.35f);
            var screen = new GameObject("Atlas coach display").transform;
            screen.SetParent(root, false);
            screen.localPosition = new Vector3(0, cy, z);
            VRGlass.Panel(screen, "Screen", Vector3.zero, new Vector2(w, h), 0.045f, GlassStyle.Panel);
            VRGlass.Label(screen, "Say Hey Atlas to Talk or Pull Right Trigger", new Vector3(0, 0.19f, -0.015f), 0.0085f, VRUi.TealHover);
            VRGlass.Label(screen, "LEFT TRIGGER · Start / finish set     LEFT MENU · Options",
                new Vector3(0, -0.205f, -0.015f), 0.0075f, VRUi.Muted);
            var board = FindAnyObjectByType<StatusBoard>();
            if (board != null)
            {
                board.PinToWall(new Vector3(0, cy - 0.015f, z - 0.02f), 1f);
                board.SetDisplayBounds(3.2f, 0.25f);
            }
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
            var wood = SurfaceTexture(true);
            var rubber = SurfaceTexture(false);
            var plaster = StudioMaterial("Mineral plaster", Wall, 0.12f);
            var dark = StudioMaterial("Architectural graphite", Ceiling, 0.20f);
            var pad = StudioMaterial("Fine rubber", Rubber, 0.14f, rubber);
            var oak = StudioMaterial("Acoustic oak", Oak, 0.28f, wood);
            var glow = StudioMaterial("Warm linear diffuser", WarmLight, 0.15f);
            if (glow != null && glow.HasProperty("_EmissionColor"))
            {
                glow.EnableKeyword("_EMISSION");
                glow.SetColor("_EmissionColor", WarmLight * 1.4f);
            }

            SurfaceBox("Floor foundation", new Vector3(0, -0.06f, 0), new Vector3(7f, 0.10f, 7f), dark);
            // Plank tops remain at floor level; calibration and squat measurements stay unchanged.
            const int planks = 22;
            float pitch = 7f / planks;
            var floorFinishes = new Material[4];
            for (int i = 0; i < floorFinishes.Length; i++)
            {
                floorFinishes[i] = StudioMaterial("Oak floor " + i, Oak * (0.91f + i * 0.035f), 0.30f, wood);
                if (floorFinishes[i] != null)
                {
                    floorFinishes[i].mainTextureScale = new Vector2(1f, 3f);
                    floorFinishes[i].mainTextureOffset = new Vector2(i * 0.17f, i * 0.23f);
                }
            }
            for (int i = 0; i < planks; i++)
                SurfaceBox("Oak plank", new Vector3(-Half + pitch * (i + 0.5f), -0.015f, 0),
                    new Vector3(pitch - 0.006f, 0.03f, 7f), floorFinishes[(i * 3) % 4]);

            SurfaceBox("Ceiling", new Vector3(0, Height + 0.05f, 0), new Vector3(7f, 0.1f, 7f), dark);
            SurfaceBox("Wall front", new Vector3(0, Height / 2, -Half), new Vector3(7f, Height, 0.1f), plaster);
            SurfaceBox("Wall left", new Vector3(-Half, Height / 2, 0), new Vector3(0.1f, Height, 7f), plaster);
            SurfaceBox("Wall right", new Vector3(Half, Height / 2, 0), new Vector3(0.1f, Height, 7f), plaster);
            SurfaceBox("Wall back", new Vector3(0, Height / 2, Half), new Vector3(7f, Height, 0.1f), plaster);

            // Rubber islands ground the equipment while keeping the central oak walkway open.
            SurfaceBox("Dumbbell rubber zone", new Vector3(-2.78f, 0.004f, 0.4f), new Vector3(1.25f, 0.008f, 3.7f), pad);
            SurfaceBox("Rack rubber zone", new Vector3(2.55f, 0.004f, 1.35f), new Vector3(1.75f, 0.008f, 3.3f), pad);

            // Recessed timber panels: shared materials and meshes keep the repeated slats inexpensive.
            foreach (int side in new[] { -1, 1 })
            {
                SurfaceBox("Acoustic backing", new Vector3(side * 3.425f, 1.66f, -0.65f), new Vector3(0.035f, 2.45f, 2.4f), dark);
                for (int slat = 0; slat < 16; slat++)
                    SurfaceBox("Oak wall slat", new Vector3(side * 3.39f, 1.66f, -1.78f + slat * 0.15f),
                        new Vector3(0.055f, 2.45f, 0.085f), oak);
                SurfaceBox("Skirting", new Vector3(side * 3.425f, 0.08f, 0), new Vector3(0.045f, 0.16f, 6.9f), dark);
                SurfaceBox("Ceiling oak border", new Vector3(side * 3.22f, 3.06f, 0), new Vector3(0.44f, 0.24f, 6.9f), oak);
                SurfaceBox("Warm ceiling cove", new Vector3(side * 2.97f, 3.015f, 0), new Vector3(0.035f, 0.025f, 6.75f), glow);
                SurfaceBox("Low teal detail", new Vector3(side * 3.39f, 0.19f, 0), new Vector3(0.02f, 0.018f, 6.85f),
                    StudioMaterial("Muted teal inlay " + side, new Color(0.10f, 0.40f, 0.36f), 0.25f));
            }
            SurfaceBox("Front skirting", new Vector3(0, 0.08f, -3.425f), new Vector3(6.9f, 0.16f, 0.045f), dark);
            SurfaceBox("Front oak feature", new Vector3(0, 1.6f, -3.425f), new Vector3(3.6f, 2.5f, 0.045f), oak);
            for (int i = -1; i <= 1; i += 2)
            {
                SurfaceBox("Linear light housing", new Vector3(i * 1.55f, 3.13f, 0), new Vector3(0.16f, 0.10f, 4.8f), dark);
                SurfaceBox("Linear warm light", new Vector3(i * 1.55f, 3.07f, 0), new Vector3(0.075f, 0.018f, 4.65f), glow);
            }
        }

        void BuildStudioLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.49f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.32f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.16f, 0.13f);
            var sun = FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.color = new Color(1f, 0.86f, 0.70f);
                sun.intensity = 0.9f;
                sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                sun.shadows = LightShadows.Hard;
                sun.shadowStrength = 0.55f;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.25f;
                sun.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Medium;
            }
            for (int i = -1; i <= 1; i += 2)
            {
                var fixture = new GameObject("Warm studio fill");
                fixture.transform.SetParent(root, false);
                fixture.transform.localPosition = new Vector3(i * 1.8f, 2.8f, -0.5f);
                var light = fixture.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.82f, 0.63f);
                light.intensity = 0.75f;
                light.range = 5.5f;
                light.shadows = LightShadows.None;
            }
        }

        readonly System.Collections.Generic.List<Material> studioMaterials = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<Texture2D> studioTextures = new System.Collections.Generic.List<Texture2D>();

        Material StudioMaterial(string name, Color colour, float smoothness, Texture2D texture = null)
        {
            var material = MakeMaterial(template, colour, smoothness);
            if (material == null) return null;
            material.name = name;
            material.enableInstancing = true;
            if (texture != null) material.mainTexture = texture;
            studioMaterials.Add(material);
            return material;
        }

        Transform SurfaceBox(string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            // Exterior walls/roof must not block the key light from reaching the room.
            // Existing equipment casts the directional shadows; surfaces only receive them.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        Texture2D SurfaceTexture(bool wood)
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float noise = Mathf.PerlinNoise(x * (wood ? 0.035f : 0.43f), y * (wood ? 0.007f : 0.43f));
                    float fine = Mathf.PerlinNoise(x * 0.73f + 17f, y * 0.73f + 13f);
                    float grain = wood ? Mathf.Sin(x * 0.34f + noise * 8f) * 0.045f : 0f;
                    float value = wood ? 0.78f + noise * 0.14f + grain + fine * 0.04f : 0.72f + noise * 0.11f + fine * 0.14f;
                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = wood ? "Oak grain" : "Fine rubber grain",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            studioTextures.Add(texture);
            return texture;
        }

        void OnDestroy()
        {
            foreach (var material in studioMaterials) if (material != null) Destroy(material);
            foreach (var texture in studioTextures) if (texture != null) Destroy(texture);
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
            Box("Mat", new Vector3(0, 0.01f, 0), new Vector3(1.4f, 0.02f, 2.2f), new Color(0.10f, 0.42f, 0.38f), 0.15f);
            Box("Mat edge", new Vector3(0, 0.0105f, 0), new Vector3(1.3f, 0.02f, 2.1f), new Color(0.085f, 0.13f, 0.14f), 0.12f);
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
            var sign = new GameObject("Studio branding").transform;
            sign.SetParent(root, false);
            sign.localPosition = new Vector3(0, 3.08f, Half - 0.24f);
            var logo = Resources.Load<Texture2D>("Branding/logo");
            if (logo != null)
                VRGlass.Image(sign, "A Better U logo", logo, new Vector3(-0.77f, 0, -0.01f), new Vector2(0.72f, 0.24f));
            else
                VRGlass.Label(sign, "A Better U", new Vector3(-0.77f, 0, -0.01f), 0.015f, VRUi.TealHover);
            VRGlass.Label(sign, "MOVEMENT STUDIO", new Vector3(0.47f, 0, -0.01f), 0.0105f, VRUi.Ink);
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

        /// A URP material that is known to render on the device, borrowed from the scene's original test objects
        /// (CreatePrimitive's default is magenta in this project). Shared by the lobby, the gym and the mannequin.
        public static Material SharedTemplate { get; private set; }

        public static Material AcquireTemplate()
        {
            if (SharedTemplate != null) return SharedTemplate;
            foreach (var name in new[] { "Cube", "Plane" })
            {
                var old = GameObject.Find(name);
                var r = old != null ? old.GetComponent<Renderer>() : null;
                if (r != null && r.sharedMaterial != null) { SharedTemplate = r.sharedMaterial; break; }
            }
            if (SharedTemplate == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) SharedTemplate = new Material(lit);
            }
            return SharedTemplate;
        }

        public static void HideSceneTestObjects()
        {
            foreach (var name in new[] { "Cube", "Plane" })
            {
                var old = GameObject.Find(name);
                if (old != null) old.SetActive(false);
            }
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
