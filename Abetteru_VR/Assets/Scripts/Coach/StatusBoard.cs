using UnityEngine;

namespace ABetterU.Coach
{
    /// Head-locked debug text so you can read results inside the headset.
    /// Uses Unity's legacy TextMesh and built-in font, so no TextMeshPro import is needed.
    public class StatusBoard : MonoBehaviour
    {
        TextMesh text;
        string lastMessage = "";
        float displayWidth, displayHeight;
        const float BaseCharacterSize = 0.012f;

        public static StatusBoard Create(Transform parent)
        {
            var go = new GameObject("StatusBoard");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -0.1f, 1.5f);
            return go.AddComponent<StatusBoard>();
        }

        void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text = gameObject.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 64;
            text.characterSize = BaseCharacterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        /// Hides or shows the board's text (hidden during the login and hub screens).
        public void SetVisible(bool visible)
        {
            var r = GetComponent<MeshRenderer>();
            if (r != null) r.enabled = visible;
        }

        /// Detaches the board from the head and fixes it in the world (e.g. on the gym's wall screen).
        /// TextMesh text reads correctly when viewed along +Z, which is how the user faces the screen.
        public void PinToWall(Vector3 position, float scale)
        {
            transform.SetParent(null, false);
            transform.SetPositionAndRotation(position, Quaternion.identity);
            transform.localScale = Vector3.one * scale;
        }

        public void SetDisplayBounds(float width, float height)
        {
            displayWidth = width;
            displayHeight = height;
            RenderMessage();
        }

        public void Show(string message)
        {
            lastMessage = message ?? "";
            RenderMessage();
            Debug.Log("[Board] " + lastMessage.Replace("\n", " | "));
        }

        void RenderMessage()
        {
            if (text == null) return;
            text.characterSize = BaseCharacterSize;
            if (displayWidth <= 0 || displayHeight <= 0) { text.text = lastMessage; return; }

            // Keep full diagnostics in the log; the wall display is a concise spoken-response companion.
            string display = lastMessage;
            int timings = display.IndexOf("\n\nASR ", System.StringComparison.Ordinal);
            if (timings >= 0) display = display.Substring(0, timings);
            int reply = display.LastIndexOf("Atlas: ", System.StringComparison.Ordinal);
            if (reply >= 0) display = display.Substring(reply + 7);
            if (display.StartsWith("Listening...", System.StringComparison.Ordinal)) display = "Listening…";
            text.text = VRUi.Wrap(display, 68, 3);
            var bounds = GetComponent<MeshRenderer>().localBounds.size;
            float factor = Mathf.Min(1f, Mathf.Min(displayWidth / Mathf.Max(bounds.x, 0.001f), displayHeight / Mathf.Max(bounds.y, 0.001f)));
            text.characterSize *= factor;
        }
    }
}
