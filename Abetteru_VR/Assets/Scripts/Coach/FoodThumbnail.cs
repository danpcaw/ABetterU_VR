using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// A small public food image. Failed or absent images retain the neutral food icon.
    public class FoodThumbnail : MonoBehaviour
    {
        Texture2D ownedTexture;
        GameObject fallback;
        float box = 0.18f;

        public static void Create(Transform parent, Vector3 position, string url, float size = 0.20f, VRArt.IconKind icon = VRArt.IconKind.Apple)
        {
            var go = new GameObject("Food thumbnail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var loader = go.AddComponent<FoodThumbnail>();
            VRGlass.Panel(go.transform, "Image frame", Vector3.zero, new Vector2(size, size), 0.035f, GlassStyle.Secondary, 3);
            loader.fallback = VRGlass.Icon(go.transform, icon, VRUi.Muted, new Vector3(0, 0, -0.006f), size * 0.6f, 4).gameObject;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                { loader.box = size * 0.9f; loader.StartCoroutine(loader.Load(url)); }
        }

        IEnumerator Load(string url)
        {
            using var request = UnityWebRequestTexture.GetTexture(url, true);
            request.timeout = 10;
            request.redirectLimit = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            ownedTexture = DownloadHandlerTexture.GetContent(request);
            if (ownedTexture == null) yield break;
            float ratio = ownedTexture.width / (float)Mathf.Max(1, ownedTexture.height);
            var size = ratio >= 1f ? new Vector2(box, box / ratio) : new Vector2(box * ratio, box);
            VRGlass.Image(transform, "Food image", ownedTexture, new Vector3(0, 0, -0.008f), size, 5);
            fallback.SetActive(false);
        }

        void OnDestroy()
        {
            if (ownedTexture != null) Destroy(ownedTexture);
        }
    }
}
