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

        // Reasoning tokens count against max_output_tokens. Too small a cap means the
        // budget is spent thinking and the reply comes back empty.
        public string reasoningEffort = "low";
        public int maxOutputTokens = 400;

        [TextArea(3, 8)]
        public string systemPrompt =
            "You are a concise, encouraging strength coach speaking aloud to someone mid-workout in VR. " +
            "Reply in at most two short sentences. Comment on pace, form cues and rep count. " +
            "Never use markdown, lists or emoji; your words are spoken.";

        public static ModelApiConfig Load() => Resources.Load<ModelApiConfig>("ModelApiSecrets");
    }
}
