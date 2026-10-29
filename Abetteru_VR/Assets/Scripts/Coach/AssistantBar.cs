using UnityEngine;

namespace ABetterU.Coach
{
    public enum AssistantState { Ready, Listening, Processing, Speaking, Done, Error }

    /// Presentation only. The voice loop supplies state and confirmed outcomes.
    public class AssistantBar : MonoBehaviour
    {
        const float OrbSize = 0.30f;
        Transform root, orb;
        Line headline, detail, hintLabel;
        Renderer orbRenderer;
        AssistantState state = AssistantState.Ready;
        string[] hints = new string[0];
        int hintIndex;
        float nextHintAt;

        /// Floating text with a soft dark shadow behind it, so it stays readable over anything without a panel.
        sealed class Line
        {
            readonly TextMesh main;
            readonly TextMesh[] shadows;
            static readonly Vector3[] Offsets = { new Vector3(0f, -0.006f, 0.004f), new Vector3(0.005f, -0.004f, 0.004f), new Vector3(-0.005f, -0.004f, 0.004f) };

            public Line(Transform root, Vector3 pos, float size, Color color, TextAnchor anchor)
            {
                shadows = new TextMesh[Offsets.Length];
                for (int i = 0; i < shadows.Length; i++)
                    shadows[i] = VRGlass.Label(root, "", pos + Offsets[i], size, new Color(0f, 0f, 0f, 0.55f), anchor, TextAlignment.Center, 5);
                main = VRGlass.Label(root, "", pos, size, color, anchor, TextAlignment.Center, 6);
            }

            public string text
            {
                set { main.text = value; foreach (var s in shadows) s.text = value; }
            }

            public void SetActive(bool on)
            {
                main.gameObject.SetActive(on);
                foreach (var s in shadows) s.gameObject.SetActive(on);
            }
        }

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
            orb = VRGlass.Image(root, "Orb", VRArt.Orb(Color.cyan, Color.cyan), new Vector3(0f, 0.09f, -0.01f), new Vector2(OrbSize, OrbSize), 2);
            orbRenderer = orb.GetComponent<Renderer>();
            headline = new Line(root, new Vector3(0f, -0.09f, -0.02f), 0.011f, VRUi.Ink, TextAnchor.MiddleCenter);
            detail = new Line(root, new Vector3(0f, -0.17f, -0.02f), 0.0082f, VRUi.Ink * 0.92f, TextAnchor.UpperCenter);
            hintLabel = new Line(root, new Vector3(0f, -0.30f, -0.02f), 0.0072f, VRUi.Muted, TextAnchor.MiddleCenter);
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
            if (hintLabel == null) return;
            hintLabel.SetActive(state == AssistantState.Ready && hints.Length > 0);
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
