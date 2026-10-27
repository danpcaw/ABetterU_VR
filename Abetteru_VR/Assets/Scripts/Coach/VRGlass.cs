using System;
using UnityEngine;

namespace ABetterU.Coach
{
    public enum GlassStyle { Panel, Card, Primary, Secondary, Purple, Disabled }

    /// Glass-style UI built from rounded-rectangle textures on unlit quads. Unlit (Sprites/Default) keeps the
    /// panels readable regardless of scene lighting and gives real translucency over the environment.
    public static class VRGlass
    {
        struct Look { public Color top, bottom, border; public float borderM; }

        static Shader sprite;
        static Shader Sprite => sprite != null ? sprite : (sprite = Shader.Find("Sprites/Default"));

        static Look LookOf(GlassStyle s, bool hover)
        {
            switch (s)
            {
                case GlassStyle.Panel:
                    return new Look { top = new Color(0.05f, 0.08f, 0.14f, 0.86f), bottom = new Color(0.03f, 0.05f, 0.10f, 0.90f), border = new Color(0.25f, 0.75f, 0.85f, 0.55f), borderM = 0.008f };
                case GlassStyle.Card:
                    return new Look { top = new Color(0.07f, 0.11f, 0.18f, 0.90f), bottom = new Color(0.05f, 0.08f, 0.14f, 0.93f), border = new Color(0.30f, 0.70f, 0.85f, 0.45f), borderM = 0.006f };
                case GlassStyle.Primary:
                    return hover
                        ? new Look { top = new Color(0.22f, 0.92f, 0.82f, 1f), bottom = new Color(0.20f, 0.62f, 1.00f, 1f), border = Color.white, borderM = 0.007f }
                        : new Look { top = new Color(0.10f, 0.80f, 0.70f, 1f), bottom = new Color(0.10f, 0.50f, 0.92f, 1f), border = new Color(0.65f, 1f, 0.95f, 0.9f), borderM = 0.006f };
                case GlassStyle.Secondary:
                    return hover
                        ? new Look { top = new Color(0.19f, 0.28f, 0.40f, 0.96f), bottom = new Color(0.14f, 0.21f, 0.31f, 0.96f), border = new Color(0.45f, 0.88f, 0.95f, 1f), borderM = 0.007f }
                        : new Look { top = new Color(0.12f, 0.17f, 0.25f, 0.92f), bottom = new Color(0.09f, 0.13f, 0.20f, 0.92f), border = new Color(0.30f, 0.62f, 0.74f, 0.75f), borderM = 0.005f };
                case GlassStyle.Purple:
                    return hover
                        ? new Look { top = new Color(0.55f, 0.32f, 0.95f, 1f), bottom = new Color(0.36f, 0.18f, 0.72f, 1f), border = new Color(0.92f, 0.70f, 1f, 1f), borderM = 0.007f }
                        : new Look { top = new Color(0.40f, 0.20f, 0.78f, 0.95f), bottom = new Color(0.25f, 0.12f, 0.55f, 0.95f), border = new Color(0.80f, 0.50f, 1f, 1f), borderM = 0.006f };
                default: // Disabled
                    return new Look { top = new Color(0.10f, 0.12f, 0.16f, 0.70f), bottom = new Color(0.08f, 0.10f, 0.14f, 0.70f), border = new Color(0.22f, 0.28f, 0.34f, 0.60f), borderM = 0.004f };
            }
        }

        static Texture2D TextureFor(GlassStyle s, bool hover, Vector2 size, float radius)
        {
            var look = LookOf(s, hover);
            return VRArt.RoundedRect(size.x, size.y, radius, look.top, look.bottom, look.border, look.borderM);
        }

        static Material NewMaterial(Texture tex)
        {
            var m = new Material(Sprite);
            m.mainTexture = tex;
            return m;
        }

        /// A non-interactive glass rectangle. sortingOrder keeps overlapping transparent layers in a stable order.
        public static Transform Panel(Transform parent, string name, Vector3 localPos, Vector2 size, float radius, GlassStyle style, int order = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = NewMaterial(TextureFor(style, false, size, radius));
            r.sortingOrder = order;
            return go.transform;
        }

        /// A flat image (e.g. the logo) on an unlit quad, keeping the image's own transparency.
        public static Transform Image(Transform parent, string name, Texture tex, Vector3 localPos, Vector2 size, int order = 3)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = NewMaterial(tex);
            r.sortingOrder = order;
            return go.transform;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 localPos, float charSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, TextAlignment alignment = TextAlignment.Center, int order = 6)
        {
            var t = VRUi.Label(parent, text, localPos, charSize, color, anchor, alignment);
            t.GetComponent<MeshRenderer>().sortingOrder = order;
            return t;
        }

        /// A clickable glass button with its label in front. Disabled buttons look unavailable and never fire.
        public static VRButton Button(Transform parent, string text, Vector3 localPos, Vector2 size, GlassStyle style, float charSize,
            Action onClick, float radius = 0.045f, int order = 2)
        {
            var quad = Panel(parent, "Button: " + text, localPos, size, radius, style == GlassStyle.Disabled ? GlassStyle.Disabled : style, order);
            var box = quad.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.02f);

            bool disabled = style == GlassStyle.Disabled;
            var label = Label(parent, text, localPos + new Vector3(0f, 0f, -0.012f), charSize, disabled ? VRUi.Muted : VRUi.Ink, order: order + 4);

            var button = quad.gameObject.AddComponent<VRButton>();
            button.OnClick = onClick;
            button.SetupTextures(quad.GetComponent<MeshRenderer>().sharedMaterial, label,
                TextureFor(style, false, size, radius),
                TextureFor(style, true, size, radius),
                TextureFor(GlassStyle.Disabled, false, size, radius));
            if (disabled) button.Interactable = false;
            return button;
        }
    }
}
