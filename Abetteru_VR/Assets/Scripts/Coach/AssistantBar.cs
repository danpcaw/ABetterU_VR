using UnityEngine;

namespace ABetterU.Coach
{
    public enum AssistantState { Ready, Listening, Processing, Speaking, Done, Error }

    /// The persistent voice assistant on the dashboard screens: one glass bar with a status orb, a headline and a short
    /// line of detail. It only displays state; VoiceLoopTest drives it. It never claims "Saved" by itself.
    public class AssistantBar : MonoBehaviour
    {
        Transform root;
        TextMesh headline, detail;
        Renderer orbRenderer;
        Transform orb;
        AssistantState state = AssistantState.Ready;

        public static AssistantBar Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("AssistantBar");
            go.transform.SetPositionAndRotation(position, rotation);
            var bar = go.AddComponent<AssistantBar>();
            bar.Build(go.transform);
            return bar;
        }

        void Build(Transform parent)
        {
            root = parent;
            VRGlass.Panel(root, "Bar", new Vector3(0, 0, 0.02f), new Vector2(3.1f, 0.62f), 0.12f, GlassStyle.Panel, 0);

            // The orb sits in the left corner and the text is centred in the bar, so the whole bar reads as centred.
            orb = VRGlass.Image(root, "Orb", VRArt.Orb(Color.cyan, Color.cyan), new Vector3(-1.27f, 0f, -0.01f), new Vector2(0.46f, 0.46f), 2);
            orbRenderer = orb.GetComponent<Renderer>();

            headline = VRGlass.Label(root, "", new Vector3(0.15f, 0.14f, -0.02f), 0.0125f, VRUi.Ink);
            detail = VRGlass.Label(root, "", new Vector3(0.15f, -0.07f, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.UpperCenter, TextAlignment.Center);
            Set(AssistantState.Ready, "Ask A Better U", "Hold right trigger to speak");
        }

        public void Set(AssistantState s, string title, string body)
        {
            state = s;
            if (headline == null) return;
            headline.text = VRUi.Wrap(title, 52, 1);
            detail.text = VRUi.Wrap(body, 66, 2);

            Color core;
            switch (s)
            {
                case AssistantState.Listening: core = new Color(1.00f, 0.38f, 0.32f); break;
                case AssistantState.Processing: core = new Color(1.00f, 0.76f, 0.26f); break;
                case AssistantState.Speaking: core = new Color(0.25f, 0.95f, 0.78f); break;
                case AssistantState.Done: core = new Color(0.38f, 0.95f, 0.48f); break;
                case AssistantState.Error: core = new Color(0.95f, 0.28f, 0.34f); break;
                default: core = new Color(0.25f, 0.85f, 0.98f); break;
            }
            orbRenderer.sharedMaterial.mainTexture = VRArt.Orb(core, core);
        }

        void Update()
        {
            if (orb == null) return;
            // A gentle pulse while it is listening or working, so the orb reads as "alive".
            float pulse = state == AssistantState.Listening || state == AssistantState.Processing
                ? 1f + 0.10f * Mathf.Sin(Time.unscaledTime * (state == AssistantState.Listening ? 9f : 5f))
                : 1f;
            orb.localScale = new Vector3(0.46f * pulse, 0.46f * pulse, 1f);
        }

        void OnDestroy()
        {
            if (root != null && root.gameObject != gameObject) Destroy(root.gameObject);
        }
    }
}
