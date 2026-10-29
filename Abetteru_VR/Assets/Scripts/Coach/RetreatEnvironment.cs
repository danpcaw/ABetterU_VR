using UnityEngine;

namespace ABetterU.Coach
{
    /// The warm "wellness retreat at sunset" shown behind the login and dashboard screens: a timber-and-stone terrace
    /// over a lake, mountains and a low sun painted onto the sky sphere. Everything is built from primitives plus one
    /// generated sky texture. The user stands at the origin, facing +Z, on the stone inlay.
    public class RetreatEnvironment : MonoBehaviour
    {
        // Rotates the painted sky around the player; tune this if the sun is not where you want it.
        const float SkyYaw = 0f;

        static readonly Color Timber = new Color(0.46f, 0.30f, 0.17f);
        static readonly Color TimberDark = new Color(0.30f, 0.19f, 0.11f);
        static readonly Color Stone = new Color(0.62f, 0.58f, 0.54f);
        static readonly Color StoneDark = new Color(0.34f, 0.32f, 0.32f);
        static readonly Color Sofa = new Color(0.72f, 0.67f, 0.60f);
        static readonly Color Cushion = new Color(0.88f, 0.82f, 0.70f);
        static readonly Color Foliage = new Color(0.14f, 0.36f, 0.22f);
        static readonly Color Amber = new Color(1.0f, 0.68f, 0.30f);

        Transform root;

        /// Hides or shows the whole backdrop (sky, hills, ground, sofa), so passthrough can show the real room behind the screens.
        public void SetVisible(bool on) { if (root != null) root.gameObject.SetActive(on); }

        public void Build()
        {
            root = new GameObject("Retreat").transform;
            BuildLighting();
            BuildSky();
            BuildDeck();
            BuildFraming();
            BuildSeating();
        }

        public void Teardown()
        {
            if (root != null) Destroy(root.gameObject);
        }

        // ── Light: a low warm sun and a soft warm ambient, no shadows (cheap on the headset) ─────────────

        void BuildLighting()
        {
            var sun = FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.color = new Color(1.0f, 0.72f, 0.50f);
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.None;
                sun.transform.rotation = Quaternion.Euler(18f, 200f, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.40f, 0.46f);
        }

        // ── Sky: a big sphere you are inside, painted with the sunset, mountains and lake ──────────────────

        void BuildSky()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sky";
            sphere.transform.SetParent(root, false);
            sphere.transform.localScale = Vector3.one * 100f;
            sphere.transform.rotation = Quaternion.Euler(0f, SkyYaw, 0f);
            Destroy(sphere.GetComponent<Collider>());

            var shader = Shader.Find("Sprites/Default");     // unlit and draws both faces, so it works from inside the sphere
            var r = sphere.GetComponent<MeshRenderer>();
            if (shader != null)
            {
                var m = new Material(shader) { mainTexture = VRArt.Sky() };
                m.renderQueue = 1000;                          // behind everything
                r.sharedMaterial = m;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ── Terrace: timber deck, stone edge, a warm light ring and a stone inlay under your feet ───────────

        void BuildDeck()
        {
            Prim(PrimitiveType.Cylinder, "Deck", new Vector3(0, -0.08f, 0), new Vector3(7.0f, 0.08f, 7.0f), Timber, 0.35f);
            Prim(PrimitiveType.Cylinder, "Deck edge", new Vector3(0, -0.12f, 0), new Vector3(7.4f, 0.08f, 7.4f), StoneDark, 0.2f);
            Prim(PrimitiveType.Cylinder, "Light ring", new Vector3(0, -0.005f, 0), new Vector3(7.05f, 0.012f, 7.05f), Amber, 0.1f);
            Prim(PrimitiveType.Cylinder, "Inlay", new Vector3(0, 0.002f, 0), new Vector3(3.0f, 0.006f, 3.0f), Stone, 0.3f);
        }

        // ── Timber pillars and partial roof framing ─────────────────────────────────────────────────────

        void BuildFraming()
        {
            float[] xs = { -3.1f, 3.1f };
            float[] zs = { 1.8f, -1.2f };
            foreach (float x in xs)
                foreach (float z in zs)
                {
                    Prim(PrimitiveType.Cube, "Pillar", new Vector3(x, 1.6f, z), new Vector3(0.26f, 3.2f, 0.26f), Timber, 0.3f);
                    Planter(new Vector3(x * 0.88f, 0f, z * 0.86f));
                }

            Prim(PrimitiveType.Cube, "Beam front", new Vector3(0, 3.3f, 1.8f), new Vector3(6.5f, 0.24f, 0.30f), TimberDark, 0.3f);
            Prim(PrimitiveType.Cube, "Beam rear", new Vector3(0, 3.3f, -1.2f), new Vector3(6.5f, 0.24f, 0.30f), TimberDark, 0.3f);
            Prim(PrimitiveType.Cube, "Beam left", new Vector3(-3.1f, 3.3f, 0.3f), new Vector3(0.30f, 0.24f, 3.3f), TimberDark, 0.3f);
            Prim(PrimitiveType.Cube, "Beam right", new Vector3(3.1f, 3.3f, 0.3f), new Vector3(0.30f, 0.24f, 3.3f), TimberDark, 0.3f);

            // Rafters over the back half only, so the sky stays open in front of you.
            for (int i = -2; i <= 2; i++)
                Prim(PrimitiveType.Cube, "Rafter", new Vector3(i * 1.2f, 3.45f, -0.9f), new Vector3(0.14f, 0.18f, 4.4f), Timber, 0.3f);
        }

        void Planter(Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, "Pot", pos + new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.25f, 0.5f), StoneDark, 0.2f);
            Prim(PrimitiveType.Sphere, "Plant", pos + new Vector3(0, 0.75f, 0), new Vector3(0.75f, 0.7f, 0.75f), Foliage, 0.1f);
        }

        // ── Seating behind and beside you ───────────────────────────────────────────────────────────────

        void BuildSeating()
        {
            SofaAt(new Vector3(0f, 0f, -2.7f), 0f);
            SofaAt(new Vector3(-2.6f, 0f, -0.2f), 90f);
            SofaAt(new Vector3(2.6f, 0f, -0.2f), -90f);
        }

        void SofaAt(Vector3 pos, float yaw)
        {
            var group = new GameObject("Sofa").transform;
            group.SetParent(root, false);
            group.position = pos;
            group.rotation = Quaternion.Euler(0, yaw, 0);

            Part(group, "Base", new Vector3(0, 0.22f, 0), new Vector3(2.0f, 0.40f, 0.85f), Sofa);
            Part(group, "Back", new Vector3(0, 0.55f, -0.34f), new Vector3(2.0f, 0.46f, 0.18f), Sofa);
            Part(group, "Cushion L", new Vector3(-0.5f, 0.46f, 0.05f), new Vector3(0.9f, 0.12f, 0.62f), Cushion);
            Part(group, "Cushion R", new Vector3(0.5f, 0.46f, 0.05f), new Vector3(0.9f, 0.12f, 0.62f), Cushion);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

        Transform Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, float smoothness)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            var m = GymRoom.MakeMaterial(GymRoom.AcquireTemplate(), color, smoothness, 0f);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        void Part(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            var m = GymRoom.MakeMaterial(GymRoom.AcquireTemplate(), color, 0.15f, 0f);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
