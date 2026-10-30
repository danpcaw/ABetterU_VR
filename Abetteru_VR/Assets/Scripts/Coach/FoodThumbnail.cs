using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// A small public picture in a rounded square (a food, or a post). The picture is cropped to fill the square and its corners are
    /// rounded to match the frame, so wide "letterbox" pictures sit inside it instead of overlapping the edges. Failed or absent
    /// pictures keep the neutral icon. Finished pictures are cached, because the lists redraw often.
    public class FoodThumbnail : MonoBehaviour
    {
        const int Pixels = 192;                          // the picture is stored at this size
        const float CornerFraction = 0.18f;              // corner radius as a fraction of the side, close to the frame's own
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        GameObject fallback;
        float box = 0.19f;

        public static void Create(Transform parent, Vector3 position, string url, float size = 0.20f, VRArt.IconKind icon = VRArt.IconKind.Apple)
        {
            var go = new GameObject("Food thumbnail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var loader = go.AddComponent<FoodThumbnail>();
            VRGlass.Panel(go.transform, "Image frame", Vector3.zero, new Vector2(size, size), 0.035f, GlassStyle.Secondary, 3);
            loader.fallback = VRGlass.Icon(go.transform, icon, VRUi.Muted, new Vector3(0, 0, -0.006f), size * 0.6f, 4).gameObject;
            loader.box = size * 0.94f;                   // just inside the frame's border
            if (string.IsNullOrEmpty(url)) return;
            if (cache.TryGetValue(url, out var cached) && cached != null) { loader.Show(cached); return; }
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                loader.StartCoroutine(loader.Load(url));
        }

        IEnumerator Load(string url)
        {
            using var request = UnityWebRequestTexture.GetTexture(url, false);      // readable: the crop reads its pixels
            request.timeout = 10;
            request.redirectLimit = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var raw = DownloadHandlerTexture.GetContent(request);
            if (raw == null || !raw.isReadable) yield break;
            var rounded = RoundedSquare(raw);
            Destroy(raw);
            cache[url] = rounded;
            if (this != null) Show(rounded);
        }

        void Show(Texture2D tex)
        {
            VRGlass.Image(transform, "Picture", tex, new Vector3(0, 0, -0.008f), new Vector2(box, box), 5);
            if (fallback != null) fallback.SetActive(false);
        }

        /// The centre square of the picture (cover fit), scaled down, with the four corners made transparent and softly rounded.
        static Texture2D RoundedSquare(Texture2D src)
        {
            int side = Mathf.Min(src.width, src.height);
            var pixels = src.GetPixels((src.width - side) / 2, (src.height - side) / 2, side, side);
            var outPixels = new Color[Pixels * Pixels];
            float radius = Pixels * CornerFraction, half = Pixels / 2f;
            for (int y = 0; y < Pixels; y++)
                for (int x = 0; x < Pixels; x++)
                {
                    // Distance outside the rounded square (zero or less means inside).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius), qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) - radius;
                    var c = pixels[(y * side / Pixels) * side + (x * side / Pixels)];
                    c.a *= Mathf.Clamp01(0.5f - outside);        // about a one-pixel soft edge
                    outPixels[y * Pixels + x] = c;
                }
            var tex = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
            tex.SetPixels(outPixels);
            tex.Apply();
            return tex;
        }
    }
}
