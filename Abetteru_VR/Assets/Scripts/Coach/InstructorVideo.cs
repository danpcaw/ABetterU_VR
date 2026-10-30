using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace ABetterU.Coach
{
    /// A how-to video of an exercise, played from a web address. Two looks:
    ///  - Panel: the video in a framed glass panel in front of you, with a Close button.
    ///  - In the room: a life-size figure standing in front of you, with the green screen made see-through by the ABetterU/ChromaKey
    ///    shader (like the instructor in a studio fitness app). It turns to face you and has no buttons: it closes when a set starts or
    ///    you ask Atlas to close it.
    /// The sound is always off, so it never talks over Atlas.
    public class InstructorVideo : MonoBehaviour
    {
        const string KeyShader = "ABetterU/ChromaKey";
        public static bool KeyShaderAvailable => Shader.Find(KeyShader) != null;

        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public bool InWorld { get; private set; }
        public Action Closed;

        VideoPlayer player;
        RenderTexture rt;
        Material keyMaterial;
        Transform head, figure;
        bool closing;

        public IEnumerator Open(string url, string title, bool inWorld, Transform headTransform)
        {
            head = headTransform;
            InWorld = inWorld && KeyShaderAvailable;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = url;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.errorReceived += (p, message) => Error = message;
            player.Prepare();
            for (float t = 0f; !player.isPrepared && Error == null && t < 25f; t += Time.unscaledDeltaTime) yield return null;
            if (!player.isPrepared)
            {
                if (Error == null) Error = "The video took too long to load.";
                yield break;
            }

            int w = (int)player.width, h = (int)player.height;
            if (w <= 0 || h <= 0) { w = 720; h = 1280; }
            float scale = Mathf.Min(1f, 1024f / Mathf.Max(w, h));                      // keep the picture within 1024 pixels
            rt = new RenderTexture(Mathf.Max(16, (int)(w * scale)), Mathf.Max(16, (int)(h * scale)), 0, RenderTextureFormat.ARGB32);
            rt.Create();
            player.targetTexture = rt;
            player.Play();

            float aspect = w / (float)h;
            if (InWorld) BuildInRoom(aspect); else BuildPanel(title, aspect);
            Ready = true;
        }

        Vector3 FlatForward()
        {
            Vector3 f = head != null ? head.forward : Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
        }

        void BuildPanel(string title, float aspect)
        {
            Vector3 forward = FlatForward();
            Vector3 pos = new Vector3(head.position.x, Mathf.Max(head.position.y - 0.05f, 1.2f), head.position.z) + forward * 1.3f;
            var root = new GameObject("How-to video").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));

            float vh = 1.0f, vw = vh * aspect;
            if (vw > 1.8f) { vw = 1.8f; vh = vw / aspect; }                                 // a wide video keeps within the panel
            var size = new Vector2(Mathf.Max(1.4f, vw + 0.30f), vh + 0.55f);
            VRGlass.Panel(root, "Panel", new Vector3(0, 0, 0.03f), size, 0.06f, GlassStyle.Panel, 30);
            VRGlass.Label(root, title, new Vector3(-size.x / 2f + 0.14f, size.y / 2f - 0.13f, -0.02f), 0.0125f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left, 36);
            if (KeyShaderAvailable)
            {
                // The green screen is removed here too, so he stands on the glass instead of inside a green rectangle.
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Video";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root, false);
                quad.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                quad.transform.localScale = new Vector3(vw, vh, 1f);
                keyMaterial = new Material(Shader.Find(KeyShader));
                keyMaterial.mainTexture = rt;
                var renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = keyMaterial;
                renderer.sortingOrder = 34;
            }
            else VRGlass.Image(root, "Video", rt, new Vector3(0f, 0f, -0.01f), new Vector2(vw, vh), 34);
            VRGlass.Label(root, "From the A Better U exercise library", new Vector3(0f, -size.y / 2f + 0.10f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleCenter, TextAlignment.Center, 36);
            VRGlass.Button(root, "Close", new Vector3(size.x / 2f - 0.22f, size.y / 2f - 0.13f, -0.02f), new Vector2(0.32f, 0.12f), GlassStyle.Secondary, 0.009f, Close, 0.035f, 34);
        }

        void BuildInRoom(float aspect)
        {
            const float height = 1.75f;                                                    // about a person's height
            Vector3 forward = FlatForward();
            Vector3 pos = new Vector3(head.position.x, 0f, head.position.z) + forward * 2.0f;
            pos.y = height / 2f;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Instructor";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            figure = quad.transform;
            figure.position = pos;
            figure.localScale = new Vector3(height * aspect, height, 1f);

            keyMaterial = new Material(Shader.Find(KeyShader));
            keyMaterial.mainTexture = rt;
            quad.GetComponent<Renderer>().sharedMaterial = keyMaterial;
            FaceUser();
        }

        // The quad shows its picture to anything on its -Z side, so point +Z away from the user.
        void FaceUser()
        {
            if (figure == null || head == null) return;
            Vector3 away = figure.position - head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) figure.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        void LateUpdate() { if (InWorld) FaceUser(); }

        public void Close()
        {
            if (closing) return;
            closing = true;
            Closed?.Invoke();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (player != null) player.Stop();
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (keyMaterial != null) Destroy(keyMaterial);
        }
    }
}
