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

        // Verified Oct 26 on a real UK-accent recording: keyword biasing fixed "earned my money" -> "hurt my knee"
        // type misses and removed stutter repeats. languageBias must be an ARRAY (a string gives 400 "Malformed request").
        public string[] transcribeLanguageBias = { "en-GB" };
        public string[] transcribeKeywords =
        {
            "knee", "hurt", "injured", "injury", "sore", "pain", "ankle", "shoulder", "back",
            "squats", "workout", "reps", "sets", "lunges", "glute bridges", "deadlift",
            "A Better U", "breakfast", "lunch", "dinner", "snack", "eggs", "toast", "protein",
        };

        // Reasoning tokens count against max_output_tokens. At "low" the model burned all 400 tokens
        // thinking and returned no reply (status: incomplete). "minimal" with a generous cap is
        // reliable and fastest (~1.6-2.7 s). "none" is not supported on muse-spark-1.3.
        // (Fields renamed so older saved assets pick up these defaults.)
        public string replyEffort = "minimal";
        public int replyMaxTokens = 800;

        // Renamed (was systemPrompt) so assets saved with the old, hallucination-prone prompt pick up this one.
        [TextArea(3, 8)]
        public string coachPrompt =
            "You are the A Better U voice coach, speaking aloud to someone in a VR gym. " +
            "Reply in at most two short, warm sentences. Never use markdown, lists or emoji; your words are spoken. " +
            "Only mention rep counts, pace, weights or form if they are given to you in the message context; " +
            "never invent numbers or claim to see the user move. If no set is in progress, just answer naturally. " +
            "If the user mentions pain or an injury, suggest gentler options and not medical advice.";

        public static ModelApiConfig Load() => Resources.Load<ModelApiConfig>("ModelApiSecrets");
    }
}
