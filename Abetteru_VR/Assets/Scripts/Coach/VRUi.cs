using System;
using UnityEngine;

namespace ABetterU.Coach
{
    /// A clickable 3D button: a box with a collider that highlights when the pointer is over it.
    public class VRButton : MonoBehaviour
    {
        public Action OnClick;

        Material material;
        TextMesh label;
        Color normal, hover;
        bool hovering, interactable = true;

        static readonly Color Disabled = new Color(0.20f, 0.23f, 0.28f);

        public void Setup(Material material, TextMesh label, Color normal, Color hover)
        {
            this.material = material;
            this.label = label;
            this.normal = normal;
            this.hover = hover;
            Apply();
        }

        // Glass buttons swap textures instead of tinting a colour.
        Texture2D texNormal, texHover, texDisabled;

        public void SetupTextures(Material material, TextMesh label, Texture2D normal, Texture2D hover, Texture2D disabled)
        {
            this.material = material;
            this.label = label;
            texNormal = normal; texHover = hover; texDisabled = disabled;
            Apply();
        }

        public bool Interactable
        {
            get => interactable;
            set { interactable = value; Apply(); }
        }

        public void SetHover(bool on) { hovering = on; Apply(); }

        public void SetLabel(string text) { if (label != null) label.text = text; }

        void Apply()
        {
            if (material == null) return;
            if (texNormal != null)
            {
                material.mainTexture = !interactable ? texDisabled : hovering ? texHover : texNormal;
                return;
            }
            Color c = !interactable ? Disabled : hovering ? hover : normal;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            if (material.HasProperty("_Color")) material.SetColor("_Color", c);
        }
    }

    /// Small helpers to build flat, readable panels in world space from primitives and legacy TextMesh.
    /// Panels face -Z (towards a user standing at the origin and looking along +Z); content that should
    /// sit in front of a panel gets a smaller (more negative) local Z.
    public static class VRUi
    {
        public static readonly Color Teal = new Color(0.078f, 0.722f, 0.651f);
        public static readonly Color TealHover = new Color(0.18f, 0.82f, 0.74f);
        public static readonly Color Slate = new Color(0.16f, 0.19f, 0.24f);
        public static readonly Color SlateHover = new Color(0.26f, 0.31f, 0.38f);
        public static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.13f);
        public static readonly Color Ink = new Color(0.93f, 0.95f, 0.98f);
        public static readonly Color Muted = new Color(0.62f, 0.68f, 0.75f);

        static Font font;
        static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Transform Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            if (!collider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());

            var m = GymRoom.MakeMaterial(GymRoom.AcquireTemplate(), color, 0.2f, 0f);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 localPos, float charSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, TextAlignment alignment = TextAlignment.Center)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var t = go.AddComponent<TextMesh>();
            t.text = text;
            t.font = Font;
            t.fontSize = 96;                         // rendered at a higher resolution for crisper text in the headset...
            t.characterSize = charSize * (64f / 96f); // ...and scaled so the on-screen size matches the old 64 px values
            t.anchor = anchor;
            t.alignment = alignment;
            t.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
            return t;
        }

        /// A box button with its label floating just in front of it. Returns the button so callers can relabel or disable it.
        public static VRButton Button(Transform parent, string text, Vector3 localPos, Vector2 size, Color color, Color hoverColor,
            float charSize, Action onClick)
        {
            var box = Box(parent, "Button: " + text, localPos, new Vector3(size.x, size.y, 0.03f), color, collider: true);
            var label = Label(parent, text, localPos + new Vector3(0f, 0f, -0.025f), charSize, Ink);

            var button = box.gameObject.AddComponent<VRButton>();
            button.OnClick = onClick;
            button.Setup(box.GetComponent<Renderer>().sharedMaterial, label, color, hoverColor);
            return button;
        }
    }

    /// A laser pointer from the right controller (or from the head, if no controller is tracked).
    /// Pull the right trigger while it rests on a VRButton to click it.
    public class VRPointer : MonoBehaviour
    {
        Transform head, leftHand, rightHand;
        bool useLeft;                    // the pointer follows whichever hand you used last
        LineRenderer line;
        Transform dot;
        VRButton hovered;

        /// True while the laser rests on a button. Voice capture checks this so a click never starts recording.
        public bool HoveringButton => hovered != null;

        /// Starts the laser on the left hand (e.g. when the menu was opened with the left Menu button).
        public void PreferLeftHand(bool left) { useLeft = left; }

        public void Init(Transform head)
        {
            this.head = head;
            leftHand = FindAny("LeftControllerAnchor", "LeftHandAnchor");
            rightHand = FindAny("RightControllerAnchor", "RightHandAnchor");

            var lineGo = new GameObject("PointerLine");
            lineGo.transform.SetParent(transform, false);
            line = lineGo.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.006f;
            line.endWidth = 0.002f;
            line.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) line.material = new Material(shader);
            line.startColor = VRUi.Teal;
            line.endColor = new Color(VRUi.Teal.r, VRUi.Teal.g, VRUi.Teal.b, 0.2f);

            var dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotGo.name = "PointerDot";
            dotGo.transform.SetParent(transform, false);
            dotGo.transform.localScale = Vector3.one * 0.025f;
            Destroy(dotGo.GetComponent<Collider>());
            var m = GymRoom.MakeMaterial(GymRoom.AcquireTemplate(), VRUi.Teal);
            if (m != null) dotGo.GetComponent<Renderer>().sharedMaterial = m;
            dot = dotGo.transform;
        }

        void Update()
        {
            if (head == null) return;

            // Either trigger works: pulling one makes that hand the pointer, then clicks whatever it is resting on.
            bool leftPulled = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
            bool rightPulled = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
            if (leftPulled) useLeft = true;
            else if (rightPulled) useLeft = false;

            Transform hand = useLeft ? leftHand : rightHand;
            Transform from = hand != null && hand.gameObject.activeInHierarchy ? hand : head;
            var ray = new Ray(from.position, from.forward);

            Vector3 end = ray.origin + ray.direction * 4f;
            VRButton hit = null;
            if (Physics.Raycast(ray, out RaycastHit h, 6f))
            {
                end = h.point;
                hit = h.collider.GetComponentInParent<VRButton>();
            }

            if (hit != hovered)
            {
                if (hovered != null) hovered.SetHover(false);
                hovered = hit;
                if (hovered != null) hovered.SetHover(true);
            }

            line.SetPosition(0, ray.origin + ray.direction * 0.05f);
            line.SetPosition(1, end);
            dot.position = end;

            if ((leftPulled || rightPulled) && hovered != null && hovered.Interactable) hovered.OnClick?.Invoke();
        }

        void OnDestroy()
        {
            if (hovered != null) hovered.SetHover(false);
            if (line != null) Destroy(line.gameObject);
            if (dot != null) Destroy(dot.gameObject);
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
