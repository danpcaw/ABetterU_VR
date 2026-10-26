using UnityEngine;

namespace ABetterU.Coach
{
    /// Create via Assets > Create > ABetterU > Model API Secrets, save as
    /// Assets/Resources/ModelApiSecrets.asset (git-ignored, never commit).
    /// NOTE: anything in Resources ships inside the APK, so the key is extractable.
    /// Fine for a hackathon demo build; never distribute that APK publicly.
    [CreateAssetMenu(menuName = "ABetterU/Model API Secrets", fileName = "ModelApiSecrets")]
    public class ModelApiConfig : ScriptableObject
    {
        // Verified Oct 26: POST /v1/responses, Bearer auth, OpenAI Responses-style body.
        public string endpoint = "https://api.meta.ai/v1/responses";
        public string model = "muse-spark-1.3";
        public string apiKey = "";

        // Verified Oct 26: multipart POST, JSON "request" part + WAV "audio" part (16 kHz mono 16-bit).
        public string transcribeEndpoint = "https://api.meta.ai/v1/asr/transcribe";
        public string transcribeModel = "muse-voice-transcribe-1.0";

        // Reasoning tokens count against max_output_tokens. At "low" the model burned all 400 tokens
        // thinking and returned no reply (status: incomplete). "minimal" with a generous cap is
        // reliable and fastest (~1.6-2.7 s). "none" is not supported on muse-spark-1.3.
        // (Fields renamed so older saved assets pick up these defaults.)
        public string replyEffort = "minimal";
        public int replyMaxTokens = 800;

        [TextArea(3, 8)]
        public string systemPrompt =
            "You are a concise, encouraging strength coach speaking aloud to someone mid-workout in VR. " +
            "Reply in at most two short sentences. Comment on pace, form cues and rep count. " +
            "Never use markdown, lists or emoji; your words are spoken.";

        public static ModelApiConfig Load() => Resources.Load<ModelApiConfig>("ModelApiSecrets");
    }
}
