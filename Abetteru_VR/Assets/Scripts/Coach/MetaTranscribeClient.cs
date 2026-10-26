using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Speech-to-text via Muse Voice Transcribe (HTTP file upload, push-to-talk mode).
    /// The realtime WebSocket (wss://api.meta.ai/v1/asr/realtime, ENDPOINTING mode) is the
    /// lower-latency upgrade if round-trip time needs to come down.
    public class MetaTranscribeClient
    {
        readonly ModelApiConfig config;

        public MetaTranscribeClient(ModelApiConfig config) => this.config = config;

        [Serializable] class AsrRequest { public string mode = "PUSH_TO_TALK"; public string model; public string audioEncoding = "WAV"; }
        [Serializable] class AsrResponse { public string transcript; public int audioDurationMs; }

        public IEnumerator Transcribe(byte[] wav, Action<string> onText, Action<string> onError)
        {
            var requestJson = JsonUtility.ToJson(new AsrRequest { model = config.transcribeModel });
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("request", requestJson, Encoding.UTF8, "application/json"),
                new MultipartFormFileSection("audio", wav, "speech.wav", "audio/wav"),
            };

            using var req = UnityWebRequest.Post(config.transcribeEndpoint, form);
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Transcribe {req.responseCode} {req.error}: {req.downloadHandler.text}");
                yield break;
            }

            var parsed = JsonUtility.FromJson<AsrResponse>(req.downloadHandler.text);
            onText?.Invoke(parsed?.transcript?.Trim() ?? "");
        }
    }
}
