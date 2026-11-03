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
        const string AndroidCamera = "android.permission.CAMERA";

        public WebCamTexture Live { get; private set; }
        public string Status { get; private set; } = "";
        public bool Failed { get; private set; }
        public bool Streaming => Live != null && Live.isPlaying && Live.width > 16 && !Failed;
        Coroutine routine;

        bool enabledPassthrough;
        WebCamTexture trying;                  // the camera being tested right now; released by Stop() if the test is cut short (calibrate, back, leaving the screen)
        static string lastGoodCamera;          // the camera that gave a picture last time: tried first, because some of the headset's cameras stream black

        public void Begin()
        {
            Stop();
            Failed = false;
            // The headset only delivers camera frames while passthrough is on in the app (it stays invisible: no layer is drawn).
            if (OVRManager.instance != null && !OVRManager.instance.isInsightPassthroughEnabled)
            { OVRManager.instance.isInsightPassthroughEnabled = true; enabledPassthrough = true; }
            routine = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // The camera list stays empty until both of these are granted, and every look at that list while Android's CAMERA is missing
            // queues another system dialog. So ask first and do not touch the cameras until they are answered. CAMERA goes first: its
            // dialog offers the headset cameras too, so one dialog normally covers both and the second request is skipped.
            foreach (var perm in new[] { AndroidCamera, Permission })
            {
                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm)) continue;
                Status = "Waiting for camera permission...";
                bool denied = false;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionDenied += _ => denied = true;
                UnityEngine.Android.Permission.RequestUserPermission(perm, callbacks);
                for (float t = 0f; t < 120f && !denied && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm); t += 0.25f)
                    yield return new WaitForSecondsRealtime(0.25f);
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
                {
                    Failed = true;
                    Status = "Camera permission was not granted.";
                    Debug.LogWarning($"[Camera] {perm} not granted.");
                    yield break;
                }
                Debug.Log($"[Camera] {perm} granted");
            }
#endif
            // Right after a grant the camera list can still be empty, so give it a few tries.
            Status = "Starting the camera...";
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                yield return StartCameras();
                if (Live != null) yield break;
                yield return new WaitForSecondsRealtime(1.5f);
            }
            Failed = true;
            Status = "The camera did not start. Tap Try again.";
            Debug.LogWarning("[Camera] Gave up: " + Status);
        }

        // One pass over the headset's cameras. Sets Live to the first that shows a real picture; black or dead ones are released at once.
        IEnumerator StartCameras()
        {
            yield return new WaitForSecondsRealtime(0.4f);        // let passthrough come up before asking for frames
            var devices = WebCamTexture.devices.OrderBy(d => d.name == lastGoodCamera ? 0 : 1).ToArray();
            Debug.Log($"[Camera] {devices.Length} camera(s): {string.Join(", ", devices.Select(d => d.name))}");
            if (devices.Length == 0) yield break;

            foreach (var device in devices)
            {
                var tex = new WebCamTexture(device.name, 1280, 960, 30);
                trying = tex;
                tex.Play();
                for (float t = 0f; t < 4f && (tex.width <= 16 || !tex.didUpdateThisFrame); t += Time.unscaledDeltaTime) yield return null;
                if (tex.width <= 16) { Debug.Log($"[Camera] {device.name}: did not start"); tex.Stop(); Destroy(tex); trying = null; continue; }
                yield return new WaitForSecondsRealtime(0.5f);
                float mean = Brightness(tex);
                Debug.Log($"[Camera] {device.name}: {tex.width}x{tex.height}, brightness {mean:0.0} of 255");
                if (mean > 6f) { trying = null; Live = tex; lastGoodCamera = device.name; Status = "Camera ready"; Debug.Log($"[Camera] using {device.name}"); yield break; }
                tex.Stop(); Destroy(tex); trying = null;
            }
            Debug.LogWarning("[Camera] No camera gave a picture on this pass.");
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
            if (trying != null) { trying.Stop(); Destroy(trying); trying = null; }
            if (enabledPassthrough && OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = false;
            enabledPassthrough = false;
        }

        void OnDisable() => Stop();
    }
}
