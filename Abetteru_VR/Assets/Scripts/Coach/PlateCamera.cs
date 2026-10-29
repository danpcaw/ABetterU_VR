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
        public bool Streaming => Live != null && Live.isPlaying && Live.width > 16;
        Coroutine routine;

        public void Begin()
        {
            Stop();
            Failed = false;
            routine = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission))
            {
                Status = "Waiting for camera permission...";
                UnityEngine.Android.Permission.RequestUserPermission(Permission);
                for (float t = 0f; t < 40f && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission); t += Time.unscaledDeltaTime)
                    yield return null;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission))
                { Status = "Camera permission was not granted."; Failed = true; Debug.LogWarning("[Camera] Permission not granted."); yield break; }
            }
#endif
            var devices = WebCamTexture.devices;
            Debug.Log($"[Camera] {devices.Length} camera(s): {string.Join(", ", devices.Select(d => d.name))}");
            if (devices.Length == 0) { Status = "No camera was found."; Failed = true; yield break; }

            Status = "Starting the camera...";
            Live = new WebCamTexture(devices[0].name, 1280, 960, 30);
            Live.Play();
            for (float t = 0f; t < 10f && Live.width <= 16; t += Time.unscaledDeltaTime) yield return null;
            if (Live.width <= 16) { Status = "The camera did not start."; Failed = true; Debug.LogWarning("[Camera] Did not start."); yield break; }

            Status = "Camera ready";
            Debug.Log($"[Camera] streaming {Live.width}x{Live.height} from {devices[0].name}");
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
        }

        void OnDisable() => Stop();
    }
}
