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

        // Hands-free uses the realtime WebSocket (about 0.1 to 0.3 s after you stop, against 2 to 2.5 s for an upload). Tick off to force
        // the upload path. Even when on, a phrase falls back to the upload whenever the realtime session is not connected yet.
        public bool realtimeAsr = true;

        // How loud Atlas speaks (0 to 1). The Wit style is already "soft", the gentlest it has, so a softer feel comes from volume.
        [Range(0.2f, 1f)] public float voiceVolume = 0.75f;
        public string realtimeEndpoint = "wss://api.meta.ai/v1/asr/realtime";

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
        // Renamed from replyEffort (Oct 28) so a saved ModelApiSecrets asset picks up this default instead of its old value.
        // Measured Oct 28 with the Atlas prompt: "minimal" averages 1.5 s per model call, "low" 2.4 s (about +0.9 s on every call,
        // and a food log makes three), and "low" used up to 722 of the 800 output tokens. "minimal" already phrases well. Try "low" only by hand.
        public string replyEffortV2 = "minimal";
        public int replyMaxTokens = 800;

        // Spoken replies: Meta's Model API has no TTS and the Quest's own engine is disabled by Meta, so we use
        // Wit.ai (Meta-owned). Paste the Wit SERVER access token here (this asset is git-ignored).
        // Verified Oct 26: POST /synthesize, Accept audio/raw = 24 kHz 16-bit mono PCM, ~0.7 s to first byte.
        public string witToken = "";
        public string witVoice = "wit$Rebecca";  // US voices: Cooper, Charlie, Cody (male); Rebecca, Remi (female)
        public string wakeName = "Atlas";        // the name the hands-free assistant answers to ("Hey Atlas"); chosen Oct 28 after a transcription test
        // Renamed from witStyle (Oct 28) for the same reason. All five styles work for Rebecca and Remi. "soft" is a little slower and longer
        // than "default" (6.4 s against 5.8 s of audio for the same sentence); "fast" is shortest (5.3 s).
        public string witStyleV2 = "soft";       // default | soft | formal | fast | projected
        public string witVersion = "20240304";

        // A Better U backend. The anon key is public by design (RLS limits it); the judge password is not,
        // so it lives only in this git-ignored asset.
        public string supabaseUrl = "";
        public string supabaseAnonKey = "";
        public string judgeEmail = "judgeuser@abetteru.co.uk";
        public string judgePassword = "";

        // Renamed again (was coachPrompt) so assets saved with the older prompt pick up Atlas.
        [TextArea(3, 12)]
        public string atlasPrompt =
            "You are Atlas, the A Better U voice coach, talking aloud with someone in their VR gym. " +
            "You are warm, upbeat and easy-going, like a friendly trainer beside them, not a call centre. " +
            "Speak naturally: use contractions, vary how you start sentences, and throw in the occasional 'nice one' or 'right then' where it fits, but never force it. " +
            "Keep most replies to one or two short sentences. " +
            "Use the person's name now and then if you know it, not in every reply. " +
            "Ask a natural follow-up question only when the conversation invites one, not after every turn. " +
            "Write exactly as you would speak: commas where you would pause, no markdown, no lists, no emoji, because your words are read aloud. " +
            "When you confirm something, say it the way a person would, with the real facts woven in, for example 'Done, eggs on toast logged for breakfast, about 180 calories' rather than reading out field names. " +
            "Only mention rep counts, pace, weights or form if they are given to you in the message context; never invent numbers or claim to see the user move. " +
            "If someone mentions pain or injury, suggest gentler options; you are not a medical professional.";

        public static ModelApiConfig Load() => Resources.Load<ModelApiConfig>("ModelApiSecrets");
    }
}
