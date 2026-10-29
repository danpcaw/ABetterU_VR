using UnityEngine;

namespace ABetterU.Coach
{
    public enum AssistantState { Ready, Listening, Processing, Speaking, Done, Error }

    /// Presentation only. The voice loop supplies state and confirmed outcomes.
    public class AssistantBar : MonoBehaviour
    {
        const float OrbSize = 0.30f;
        Transform root, panel, orb;
        TextMesh headline, detail, hintLabel;
        Renderer orbRenderer;
        AssistantState state = AssistantState.Ready;
        string[] hints = new string[0];
        int hintIndex;
        float nextHintAt;

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
            panel = VRGlass.Panel(root, "Atlas bar", new Vector3(0, 0, 0.02f), new Vector2(2.6f, 0.50f), 0.08f, GlassStyle.Panel);
            orb = VRGlass.Image(root, "Orb", VRArt.Orb(Color.cyan, Color.cyan), new Vector3(0f, 0.09f, -0.01f), new Vector2(OrbSize, OrbSize), 2);
            orbRenderer = orb.GetComponent<Renderer>();
            headline = VRGlass.Label(root, "", new Vector3(0f, -0.09f, -0.02f), 0.011f, VRUi.Ink, TextAnchor.MiddleCenter, TextAlignment.Center);
            detail = VRGlass.Label(root, "", new Vector3(0f, -0.17f, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.UpperCenter, TextAlignment.Center);
            hintLabel = VRGlass.Label(root, "", new Vector3(0f, -0.28f, -0.02f), 0.0072f, VRUi.Muted, TextAnchor.MiddleCenter, TextAlignment.Center);
            Set(AssistantState.Ready, VoiceLoopTest.ReadyHeadline, VoiceLoopTest.ReadyDetail);
        }

        public void Set(AssistantState s, string title, string body)
        {
            state = s;
            if (headline == null) return;
            bool ready = s == AssistantState.Ready;
            headline.text = VRUi.Wrap(ready ? VoiceLoopTest.WakeName + " · How can I help?" : title, 48, 1);
            // Preserve actionable Ready messages (for example a too-short recording).
            detail.text = VRUi.Wrap(ready && body == VoiceLoopTest.ReadyDetail
                ? VoiceLoopTest.ReadyHeadline + " or hold the right trigger to speak"
                : body, 64, ready ? 2 : 3);
            Color core;
            switch (s)
            {
                case AssistantState.Listening: core = new Color(1f, 0.38f, 0.32f); break;
                case AssistantState.Processing: core = new Color(1f, 0.76f, 0.26f); break;
                case AssistantState.Speaking: core = new Color(0.25f, 0.95f, 0.78f); break;
                case AssistantState.Done: core = new Color(0.38f, 0.95f, 0.48f); break;
                case AssistantState.Error: core = new Color(0.95f, 0.28f, 0.34f); break;
                default: core = new Color(0.25f, 0.85f, 0.98f); break;
            }
            orbRenderer.sharedMaterial.mainTexture = VRArt.Orb(core, core);
            Layout();
        }

        void Layout()
        {
            if (panel == null) return;
            bool ready = state == AssistantState.Ready;
            bool showHint = ready && hints.Length > 0;
            float height = ready ? (showHint ? 0.58f : 0.50f) : 0.60f;
            // Keep the top edge fixed: responses expand downwards, away from the diary.
            panel.localPosition = new Vector3(0, 0.24f - height / 2f, 0.02f);
            panel.localScale = new Vector3(2.6f, height, 1f);
            hintLabel.gameObject.SetActive(showHint);
        }

        public void SetHints(string[] phrases)
        {
            hints = phrases ?? new string[0];
            hintIndex = 0;
            ShowHint();
            Layout();
        }

        public void HideHints()
        {
            hints = new string[0];
            Layout();
        }

        void ShowHint()
        {
            if (hintLabel == null || hints.Length == 0) return;
            hintLabel.text = VRUi.Wrap("Try: “" + hints[hintIndex % hints.Length] + "”", 64, 1);
            nextHintAt = Time.unscaledTime + 5f;
        }

        void Update()
        {
            if (state == AssistantState.Ready && hints.Length > 1 && Time.unscaledTime >= nextHintAt)
            {
                hintIndex++;
                ShowHint();
            }
            if (orb == null) return;
            float pulse = state == AssistantState.Listening || state == AssistantState.Processing
                ? 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            orb.localScale = new Vector3(OrbSize * pulse, OrbSize * pulse, 1f);
        }
    }
}
