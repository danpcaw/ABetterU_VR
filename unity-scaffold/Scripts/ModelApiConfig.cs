using UnityEngine;

namespace ABetterU.Coach
{
    /// Create via Assets > Create > ABetterU > Model API Secrets, save as
    /// Assets/Resources/ModelApiSecrets.asset (git-ignored, never commit).
    [CreateAssetMenu(menuName = "ABetterU/Model API Secrets", fileName = "ModelApiSecrets")]
    public class ModelApiConfig : ScriptableObject
    {
        // TODO(verify): confirm the real endpoint, auth header and model id from Meta's Model API docs.
        public string endpoint = "https://CHANGE-ME/v1/chat/completions";
        public string model = "muse-spark";
        public string apiKey = "";

        [TextArea(3, 8)]
        public string systemPrompt =
            "You are a concise, encouraging strength coach speaking aloud to someone mid-workout in VR. " +
            "Reply in at most two short sentences. Comment on pace, form cues and rep count. " +
            "Never use markdown, lists or emoji; your words are spoken.";

        public static ModelApiConfig Load() => Resources.Load<ModelApiConfig>("ModelApiSecrets");
    }
}
