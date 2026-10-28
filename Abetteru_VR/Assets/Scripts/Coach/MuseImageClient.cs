using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Muse Image on the Meta Model API (POST /v1/images/generations, $0.01 per image, about 8 s per call).
    /// Verified Oct 28: model "muse-image-1.0", Bearer auth, response data[0].b64_json. The API defaults to WebP, which
    /// Unity cannot decode, so we ask for PNG.
    public class MuseImageClient
    {
        const string Endpoint = "https://api.meta.ai/v1/images/generations";
        readonly ModelApiConfig config;

        public MuseImageClient(ModelApiConfig config) => this.config = config;

        public IEnumerator Generate(string prompt, int size, Action<Texture2D> onImage, Action<string> onError)
        {
            var body = new JObject
            {
                ["model"] = "muse-image-1.0",
                ["prompt"] = prompt,
                ["size"] = $"{size}x{size}",
                ["n"] = 1,
                ["output_format"] = "png",
            }.ToString(Newtonsoft.Json.Formatting.None);

            using var req = new UnityWebRequest(Endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 60,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{req.responseCode} {req.error}");
                yield break;
            }

            Texture2D tex = null;
            try
            {
                string b64 = (string)JObject.Parse(req.downloadHandler.text)["data"]?[0]?["b64_json"];
                if (!string.IsNullOrEmpty(b64))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(Convert.FromBase64String(b64))) { UnityEngine.Object.Destroy(tex); tex = null; }
                }
            }
            catch (Exception e) { onError?.Invoke("bad image response: " + e.Message); yield break; }

            if (tex == null) { onError?.Invoke("no image in response"); yield break; }
            onImage?.Invoke(tex);
        }
    }
}
