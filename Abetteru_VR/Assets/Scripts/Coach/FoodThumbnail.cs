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

        public static void Create(Transform parent, Vector3 position, string url)
        {
            var go = new GameObject("Food thumbnail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var loader = go.AddComponent<FoodThumbnail>();
            VRGlass.Panel(go.transform, "Image frame", Vector3.zero, new Vector2(0.20f, 0.20f), 0.035f, GlassStyle.Secondary, 3);
            loader.fallback = VRGlass.Icon(go.transform, VRArt.IconKind.Apple, VRUi.Muted, new Vector3(0, 0, -0.006f), 0.12f, 4).gameObject;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                loader.StartCoroutine(loader.Load(url));
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
            var size = ratio >= 1f ? new Vector2(0.18f, 0.18f / ratio) : new Vector2(0.18f * ratio, 0.18f);
            VRGlass.Image(transform, "Food image", ownedTexture, new Vector3(0, 0, -0.008f), size, 5);
            fallback.SetActive(false);
        }

        void OnDestroy()
        {
            if (ownedTexture != null) Destroy(ownedTexture);
        }
    }
}
