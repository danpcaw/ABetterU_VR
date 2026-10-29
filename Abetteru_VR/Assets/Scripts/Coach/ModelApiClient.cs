using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Thin wrapper over the Meta Model API Responses endpoint (POST /v1/responses).
    /// Non-streaming for now; streaming is the latency upgrade for evenings 3-4.
    public class ModelApiClient
    {
        readonly ModelApiConfig config;

        public ModelApiClient(ModelApiConfig config) => this.config = config;

        [Serializable] class InPart { public string type = "input_text"; public string text; }
        [Serializable] class InMsg { public string role = "user"; public InPart[] content; }
        [Serializable] class Reasoning { public string effort; }
        [Serializable] class Req
        {
            public string model;
            public string instructions;
            public InMsg[] input;
            public Reasoning reasoning;
            public int max_output_tokens;
            public bool stream = false;
        }

        // Response: output[] holds a "reasoning" item then a "message" item with content[].text.
        [Serializable] class OutPart { public string type; public string text; }
        [Serializable] class OutItem { public string type; public OutPart[] content; }
        [Serializable] class Resp { public OutItem[] output; public string status; }

        /// Sends one coaching turn. Calls onReply with the spoken text, or onError with a message.
        public IEnumerator Ask(string userContext, Action<string> onReply, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new Req
            {
                model = config.model,
                instructions = config.assistantPrompt,
                input = new[] { new InMsg { content = new[] { new InPart { text = userContext } } } },
                reasoning = new Reasoning { effort = config.replyEffortV2 },
                max_output_tokens = config.replyMaxTokens,
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

            var text = ExtractMessageText(req.downloadHandler.text);
            if (string.IsNullOrWhiteSpace(text))
            {
                // Usually means max_output_tokens was all spent on reasoning (status: incomplete).
                Debug.LogError("[ModelApi] No message text. Full response: " + req.downloadHandler.text);
                onError?.Invoke("Model ran out of tokens before replying (see log).");
                yield break;
            }
            onReply?.Invoke(text.Trim());
        }

        static string ExtractMessageText(string json)
        {
            var parsed = JsonUtility.FromJson<Resp>(json);
            if (parsed?.output == null) return null;
            foreach (var item in parsed.output)
                if (item.type == "message" && item.content != null && item.content.Length > 0)
                    return item.content[0].text;
            return null;
        }
    }
}
