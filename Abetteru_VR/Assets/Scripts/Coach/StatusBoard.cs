using UnityEngine;

namespace ABetterU.Coach
{
    /// Head-locked debug text so you can read results inside the headset.
    /// Uses Unity's legacy TextMesh and built-in font, so no TextMeshPro import is needed.
    public class StatusBoard : MonoBehaviour
    {
        TextMesh text;

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
            text.characterSize = 0.012f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        public void Show(string message)
        {
            if (text != null) text.text = message;
            Debug.Log("[Board] " + message.Replace("\n", " | "));
        }
    }
}
