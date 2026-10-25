using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Thin wrapper over the Meta Model API. Request/response shape assumes a chat-completions
    /// style API; TODO(verify) against the real docs before relying on it (prep checklist item 1).
    public class ModelApiClient
    {
        readonly ModelApiConfig config;

        public ModelApiClient(ModelApiConfig config) => this.config = config;

        [Serializable] class Msg { public string role; public string content; }
        [Serializable] class Req { public string model; public Msg[] messages; public int max_tokens = 80; }
        [Serializable] class Choice { public Msg message; }
        [Serializable] class Resp { public Choice[] choices; }

        /// Sends one coaching turn. Calls onReply with the text, or onError with a message.
        public IEnumerator Ask(string userContext, Action<string> onReply, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new Req
            {
                model = config.model,
                messages = new[]
                {
                    new Msg { role = "system", content = config.systemPrompt },
                    new Msg { role = "user", content = userContext },
                },
            });

            using var req = new UnityWebRequest(config.endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{req.responseCode} {req.error}: {req.downloadHandler.text}");
                yield break;
            }

            var parsed = JsonUtility.FromJson<Resp>(req.downloadHandler.text);
            if (parsed?.choices == null || parsed.choices.Length == 0)
            {
                onError?.Invoke("Empty response: " + req.downloadHandler.text);
                yield break;
            }
            onReply?.Invoke(parsed.choices[0].message.content);
        }
    }
}
