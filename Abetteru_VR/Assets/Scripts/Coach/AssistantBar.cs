using UnityEngine;

namespace ABetterU.Coach
{
    public enum AssistantState { Ready, Listening, Processing, Speaking, Done, Error }

    /// The persistent voice assistant on the dashboard screens: one glass bar with a status orb, a headline and a short
    /// line of detail. It only displays state; VoiceLoopTest drives it. It never claims "Saved" by itself.
    public class AssistantBar : MonoBehaviour
    {
        const float OrbSize = 0.34f;

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
            // A compact, centred stack: the orb on top, the headline under it, then up to two lines of detail.
            VRGlass.Panel(root, "Bar", new Vector3(0, 0, 0.02f), new Vector2(2.3f, 0.78f), 0.12f, GlassStyle.Panel, 0);

            orb = VRGlass.Image(root, "Orb", VRArt.Orb(Color.cyan, Color.cyan), new Vector3(0f, 0.22f, -0.01f), new Vector2(OrbSize, OrbSize), 2);
            orbRenderer = orb.GetComponent<Renderer>();

            headline = VRGlass.Label(root, "", new Vector3(0f, -0.02f, -0.02f), 0.0118f, VRUi.Ink);
            detail = VRGlass.Label(root, "", new Vector3(0f, -0.10f, -0.02f), 0.0078f, VRUi.Muted, TextAnchor.UpperCenter, TextAlignment.Center);
            Set(AssistantState.Ready, "Ask A Better U", "Hold right trigger to speak");
        }

        public void Set(AssistantState s, string title, string body)
        {
            state = s;
            if (headline == null) return;
            headline.text = VRUi.Wrap(title, 44, 1);
            detail.text = VRUi.Wrap(body, 52, 2);

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

        // ── "Try saying" strip: a second, smaller glass bar under the assistant that cycles example phrases ──
        // Shown to the judge login until they speak once. It only suggests; nothing here talks to the backend.
        GameObject hintPanel;
        TextMesh hintLabel;
        string[] hints = new string[0];
        int hintIndex;
        float nextHintAt;

        public void SetHints(string[] phrases)
        {
            hints = phrases ?? new string[0];
            hintIndex = 0;
            if (hints.Length == 0) { HideHints(); return; }

            if (hintPanel == null)
            {
                hintPanel = new GameObject("TrySaying");
                hintPanel.transform.SetParent(root, false);
                hintPanel.transform.localPosition = new Vector3(0f, -0.55f, 0f);
                VRGlass.Panel(hintPanel.transform, "HintBar", new Vector3(0, 0, 0.02f), new Vector2(2.3f, 0.30f), 0.12f, GlassStyle.Panel, 0);
                VRGlass.Label(hintPanel.transform, "TRY SAYING", new Vector3(0f, 0.065f, -0.02f), 0.0066f, VRUi.Muted, TextAnchor.UpperCenter, TextAlignment.Center);
                hintLabel = VRGlass.Label(hintPanel.transform, "", new Vector3(0f, -0.02f, -0.02f), 0.0105f, VRUi.Ink);
            }
            hintPanel.SetActive(true);
            ShowHint();
        }

        public void HideHints()
        {
            if (hintPanel != null) hintPanel.SetActive(false);
            hints = new string[0];
        }

        void ShowHint()
        {
            if (hintLabel == null || hints.Length == 0) return;
            hintLabel.text = "“" + hints[hintIndex % hints.Length] + "”";
            nextHintAt = Time.unscaledTime + 5f;
        }

        void Update()
        {
            if (hints.Length > 1 && hintPanel != null && Time.unscaledTime >= nextHintAt)
            {
                hintIndex++;
                ShowHint();
            }

            if (orb == null) return;
            // A gentle pulse while it is listening or working, so the orb reads as "alive".
            float pulse = state == AssistantState.Listening || state == AssistantState.Processing
                ? 1f + 0.10f * Mathf.Sin(Time.unscaledTime * (state == AssistantState.Listening ? 9f : 5f))
                : 1f;
            orb.localScale = new Vector3(OrbSize * pulse, OrbSize * pulse, 1f);
        }

        void OnDestroy()
        {
            if (root != null && root.gameObject != gameObject) Destroy(root.gameObject);
        }
    }
}
