using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// A round profile picture. Draws nothing until the image has loaded, so whatever sits behind it (the initial on a glowing orb)
    /// shows meanwhile and stays if there is no picture or it fails. Pictures are cached, because the dashboards redraw often.
    public class AvatarImage : MonoBehaviour
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static void Create(Transform parent, Vector3 position, string url, float size)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
            var go = new GameObject("Avatar picture");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var avatar = go.AddComponent<AvatarImage>();
            if (cache.TryGetValue(url, out var tex) && tex != null) avatar.Show(tex, size);
            else avatar.StartCoroutine(avatar.Load(url, size));
        }

        IEnumerator Load(string url, float size)
        {
            using var request = UnityWebRequestTexture.GetTexture(url, false);
            request.timeout = 10;
            request.redirectLimit = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var raw = DownloadHandlerTexture.GetContent(request);
            if (raw == null || !raw.isReadable) yield break;
            var round = Circle(raw);
            Destroy(raw);
            cache[url] = round;
            Show(round, size);
        }

        void Show(Texture2D tex, float size) => VRGlass.Image(transform, "Picture", tex, new Vector3(0f, 0f, -0.012f), new Vector2(size, size), 5);

        /// Centre square of the picture, scaled to 256 px, with everything outside a circle made transparent (soft 2 px edge).
        static Texture2D Circle(Texture2D src)
        {
            const int n = 256;
            int side = Mathf.Min(src.width, src.height);
            var pixels = src.GetPixels((src.width - side) / 2, (src.height - side) / 2, side, side);
            var outPixels = new Color[n * n];
            float r = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float edge = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));          // 1 inside, fading to 0 over the last pixel or two
                    var c = pixels[(y * side / n) * side + (x * side / n)];
                    c.a *= Mathf.Clamp01(edge * 0.5f);
                    outPixels[y * n + x] = c;
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels(outPixels);
            tex.Apply();
            return tex;
        }
    }
}
