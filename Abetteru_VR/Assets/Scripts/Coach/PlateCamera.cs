using System.Collections;
using System.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// The headset's passthrough camera, read through Unity's WebCamTexture (Meta's Passthrough Camera API). Asks for the
    /// HEADSET_CAMERA permission, starts the first camera and can freeze one frame as a normal texture. In the Unity editor it
    /// uses the computer's webcam, so the screen can be tried without a headset.
    public class PlateCamera : MonoBehaviour
    {
        public const string Permission = "horizonos.permission.HEADSET_CAMERA";

        public WebCamTexture Live { get; private set; }
        public string Status { get; private set; } = "";
        public bool Failed { get; private set; }
        public bool Streaming => Live != null && Live.isPlaying && Live.width > 16 && !Failed;
        Coroutine routine;

        bool waitingForPermission, enabledPassthrough;

        public void Begin()
        {
            Stop();
            Failed = false;
            // The headset only delivers camera frames while passthrough is on in the app (it stays invisible: no layer is drawn).
            if (OVRManager.instance != null && !OVRManager.instance.isInsightPassthroughEnabled)
            { OVRManager.instance.isInsightPassthroughEnabled = true; enabledPassthrough = true; }
            routine = StartCoroutine(Run());
        }

        // Back from the system permission dialog while still waiting: start over, so nothing is left stuck on "waiting".
        void OnApplicationFocus(bool focused) { if (focused && waitingForPermission) Begin(); }

        static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission);
#else
            return true;
#endif
        }

        IEnumerator Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!HasPermission())
            {
                Status = "Waiting for camera permission...";
                waitingForPermission = true;
                UnityEngine.Android.Permission.RequestUserPermission(Permission);
                for (float t = 0f; t < 60f && !HasPermission(); t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
                waitingForPermission = false;
                if (!HasPermission())
                { Status = "Camera permission was not granted."; Failed = true; Debug.LogWarning("[Camera] Permission not granted."); yield break; }
            }
#endif
            yield return new WaitForSecondsRealtime(0.4f);        // let passthrough come up before asking for frames
            var devices = WebCamTexture.devices;
            Debug.Log($"[Camera] {devices.Length} camera(s): {string.Join(", ", devices.Select(d => d.name))}");
            if (devices.Length == 0) { Status = "No camera was found."; Failed = true; yield break; }

            // The headset lists several cameras and not all of them are the colour passthrough cameras; a wrong one streams black.
            // Try each in turn and keep the first that shows a real picture.
            Status = "Starting the camera...";
            WebCamTexture firstStreaming = null;
            foreach (var device in devices)
            {
                var tex = new WebCamTexture(device.name, 1280, 960, 30);
                tex.Play();
                for (float t = 0f; t < 4f && (tex.width <= 16 || !tex.didUpdateThisFrame); t += Time.unscaledDeltaTime) yield return null;
                if (tex.width <= 16) { Debug.Log($"[Camera] {device.name}: did not start"); tex.Stop(); Destroy(tex); continue; }
                yield return new WaitForSecondsRealtime(0.5f);
                float mean = Brightness(tex);
                Debug.Log($"[Camera] {device.name}: {tex.width}x{tex.height}, brightness {mean:0.0} of 255");
                if (mean > 6f) { Live = tex; Status = "Camera ready"; Debug.Log($"[Camera] using {device.name}"); yield break; }
                if (firstStreaming == null) firstStreaming = tex; else { tex.Stop(); Destroy(tex); }
            }

            if (firstStreaming != null) Live = firstStreaming;
            Status = "The camera only returned black frames.";
            Failed = true;
            Debug.LogWarning("[Camera] Every camera returned black frames.");
        }

        // Average brightness of a sparse grid of the frame (0 to 255). A black frame is about 0.
        static float Brightness(WebCamTexture tex)
        {
            var pixels = tex.GetPixels32();
            if (pixels.Length == 0) return 0f;
            int step = Mathf.Max(1, pixels.Length / 4000);
            float sum = 0f; int n = 0;
            for (int i = 0; i < pixels.Length; i += step) { var c = pixels[i]; sum += Mathf.Max(c.r, Mathf.Max(c.g, c.b)); n++; }
            return sum / Mathf.Max(1, n);
        }

        /// Freezes the current frame as an ordinary texture (the caller owns it and must Destroy it).
        public Texture2D Capture()
        {
            if (!Streaming) return null;
            var tex = new Texture2D(Live.width, Live.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(Live.GetPixels32());
            tex.Apply();
            Debug.Log($"[Camera] captured {tex.width}x{tex.height}");
            return tex;
        }

        public void Stop()
        {
            if (routine != null) { StopCoroutine(routine); routine = null; }
            if (Live != null) { Live.Stop(); Destroy(Live); Live = null; }
            waitingForPermission = false;
            if (enabledPassthrough && OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = false;
            enabledPassthrough = false;
        }

        void OnDisable() => Stop();
    }
}
