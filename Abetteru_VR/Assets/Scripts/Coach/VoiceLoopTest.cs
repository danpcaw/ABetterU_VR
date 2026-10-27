using System.Collections;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Hold the RIGHT trigger (or A) to talk, release to send:
    /// mic -> Muse Voice Transcribe -> Muse Spark -> text on a head-locked board.
    /// Add to any GameObject in the scene (e.g. an empty "VoiceLoop"). It adds the MicRecorder itself.
    public class VoiceLoopTest : MonoBehaviour
    {
        MicRecorder mic;
        WitSpeaker tts;
        MetaTranscribeClient asr;
        ModelApiClient spark;
        SupabaseClient supabase;
        CoachAgent agent;
        StatusBoard board;
        bool busy;

        IEnumerator Start()
        {
            gameObject.AddComponent<GymRoom>();   // the virtual gym, built from primitives at startup
            board = StatusBoard.Create(Camera.main != null ? Camera.main.transform : transform);

            var config = ModelApiConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                board.Show("No API key.\nCreate Assets/Resources/\nModelApiSecrets and paste key.");
                enabled = false;
                yield break;
            }

            asr = new MetaTranscribeClient(config);
            spark = new ModelApiClient(config);
            mic = gameObject.AddComponent<MicRecorder>();

            tts = gameObject.AddComponent<WitSpeaker>();
            tts.Init(config);

            board.Show("Allow the microphone\nwhen asked...");
            yield return mic.EnsurePermission();

            string ttsNote = tts.Ready ? "Voice ready (Wit.ai)." : "No voice: paste Wit token\nin ModelApiSecrets.";
            Debug.Log("[VoiceLoop] TTS: " + ttsNote);

            // Judge Instant Login: sign in to A Better U (RLS scopes everything to this one user).
            supabase = new SupabaseClient(config);
            string loginNote = "Not signed in (Supabase unset).";
            if (supabase.Configured)
            {
                board.Show("Signing in to\nA Better U...");
                string loginError = null;
                yield return supabase.SignInJudge(e => loginError = e);
                if (loginError != null) Debug.LogError("[Supabase] " + loginError);
                loginNote = loginError == null ? $"Signed in as {supabase.DisplayName ?? "user"}." : "Sign-in failed (see logcat).";
            }
            Debug.Log("[Supabase] " + loginNote);
            agent = new CoachAgent(config, supabase);

            // Squat counter: the headset's height is the only body signal the Quest gives us.
            reps = gameObject.AddComponent<RepCounter>();
            reps.head = Camera.main != null ? Camera.main.transform : transform;

            board.Show($"Hold RIGHT TRIGGER to talk.\nPress LEFT TRIGGER\nto start a squat set.\n\n{ttsNote}\n{loginNote}");
        }

        RepCounter reps;
        bool inSet;
        int lastShownReps = -1;

        void Update()
        {
            if (mic == null) return;

            // X on the left controller starts a squat set, and ends it.
            if (SetTogglePressed() && !busy)
            {
                StartCoroutine(inSet ? EndSet() : BeginSet());
                return;
            }
            if (inSet)
            {
                if (Time.unscaledTime >= nextMeter)
                {
                    nextMeter = Time.unscaledTime + 0.1f;
                    lastShownReps = reps.RepCount;
                    // Depth meter: the bar fills toward the "down" line (| marks it); % is how far below standing you are.
                    float pct = Mathf.Max(0f, reps.CurrentDrop);
                    int bars = Mathf.Clamp(Mathf.RoundToInt(pct / reps.downFraction * 10f), 0, 14);
                    board.Show($"SQUAT SET\n\nReps: {lastShownReps}\n[{new string('#', Mathf.Min(bars, 10))}{new string('.', Mathf.Max(0, 10 - bars))}]{(bars >= 10 ? " DEEP" : "")}\ndepth {pct * 100f:0}% (need {reps.downFraction * 100f:0}%)\n\nPress LEFT TRIGGER\nwhen done.");
                }
                return;
            }
            if (busy) return;

            if (PressedDown()) { if (mic.StartRecording()) board.Show("Listening..."); }
            else if (ReleasedUp() && mic.IsRecording) StartCoroutine(Turn(mic.StopRecording()));
            else if (mic.IsRecording && Time.unscaledTime >= nextMeter)
            {
                nextMeter = Time.unscaledTime + 0.15f;
                float level = mic.CurrentLevel();
                int bars = Mathf.Clamp(Mathf.RoundToInt(level * 40f), 0, 20);   // 0.5 peak = full bar
                board.Show("Listening...\n" + new string('|', bars) + new string('.', 20 - bars) + $"\nlevel {level:0.000}");
            }
        }

        float nextMeter;

        // The left trigger starts/ends a set. Left X/Y are kept as a fallback, but on our Quest build they never arrive
        // (verified Oct 27), so the on-screen text only mentions the trigger. Each press is logged for diagnosis.
        bool SetTogglePressed()
        {
            string pressed = null;
            if (OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch)) pressed = "left X";
            else if (OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch)) pressed = "left Y";
            else if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)) pressed = "left trigger";
            if (pressed == null) return false;
            Debug.Log($"[Input] {pressed} pressed (busy={busy}, inSet={inSet})");
            return true;
        }

        IEnumerator BeginSet()
        {
            busy = true;
            board.Show("Stand tall, look\nstraight ahead and\nstay still...");
            yield return reps.Calibrate(2f);
            reps.Active = true;
            inSet = true;
            busy = false;
            lastShownReps = -1;   // forces the first "Reps: 0" draw
        }

        IEnumerator EndSet()
        {
            reps.Active = false;
            inSet = false;
            busy = true;
            int n = reps.RepCount;
            if (n == 0) { board.Show("No reps counted.\nPress LEFT TRIGGER to try again."); busy = false; yield break; }

            // The rep count comes from the headset, not from the user or the model.
            board.Show($"Set done: {n} reps.\nLogging...");
            string turn = $"Context: the headset just counted a finished set: {n} bodyweight squat reps (exercise name: Bodyweight Squat). " +
                          "Log it with log_set and reply briefly with what was recorded.";
            string reply = null, error = null;
            yield return agent.Run(turn, p => board.Show($"Set done: {n} reps.\n{Wrap(p)}"), r => reply = r, e => error = e);

            if (reply == null) { board.Show("Coach failed:\n" + Wrap(Short(error ?? "no reply", 160))); busy = false; yield break; }
            board.Show($"Set done: {n} reps.\n\nCoach: {Wrap(reply)}");

            bool spoken = false;
            tts.Speak(reply, () => spoken = true);
            while (!spoken) yield return null;
            busy = false;
        }

        static bool PressedDown() =>
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);

        static bool ReleasedUp() =>
            OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
            OVRInput.GetUp(OVRInput.Button.One, OVRInput.Controller.RTouch);

        IEnumerator Turn(byte[] wav)
        {
            if (wav == null) { board.Show("Too short, try again.\nHold trigger while talking."); yield break; }

            busy = true;
            float t0 = Time.realtimeSinceStartup;
            board.Show("Transcribing...");

            // Debug aid: keep the last recording so it can be pulled with adb and replayed with curl.
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.persistentDataPath, "last.wav"), wav); }
            catch (System.Exception ex) { Debug.LogWarning("[VoiceLoop] Could not save last.wav: " + ex.Message); }

            string heard = null, error = null;
            yield return asr.Transcribe(wav, t => heard = t, e => error = e);
            float tAsr = Time.realtimeSinceStartup - t0;

            if (error != null)
            {
                Debug.LogError("[VoiceLoop] FULL ERROR: " + error);
                board.Show("Transcribe failed:\n" + Wrap(Short(error, 160)));
                busy = false;
                yield break;
            }
            if (string.IsNullOrWhiteSpace(heard)) { board.Show("Didn't catch that.\nTry again."); busy = false; yield break; }

            board.Show($"You: {Wrap(heard)}\n\nThinking...");

            string reply = null;
            float t1 = Time.realtimeSinceStartup;
            string userTurn = $"Context: no set in progress, no rep data.\nThe user said: \"{heard}\"";
            if (supabase != null && supabase.SignedIn)
                yield return agent.Run(userTurn, p => board.Show($"You: {Wrap(heard)}\n\n{Wrap(p)}"), r => reply = r, e => error = e);
            else
                yield return spark.Ask(userTurn, r => reply = r, e => error = e);
            float tSpark = Time.realtimeSinceStartup - t1;

            if (reply == null) { board.Show("Coach failed:\n" + Wrap(Short(error, 160))); busy = false; yield break; }

            board.Show($"You: {Wrap(heard)}\n\nCoach: {Wrap(reply)}\n\nASR {tAsr:0.0}s  Spark {tSpark:0.0}s");

            bool spoken = false;
            tts.Speak(reply, () => spoken = true);
            while (!spoken) yield return null;
            busy = false;
        }

        static string Short(string s, int max = 120) => s.Length > max ? s.Substring(0, max) + "..." : s;

        // Wraps long lines at ~32 chars so the TextMesh stays readable.
        static string Wrap(string s, int width = 32)
        {
            var words = s.Split(' ');
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var w in words)
            {
                if (line + w.Length > width) { sb.Append('\n'); line = 0; }
                sb.Append(w).Append(' ');
                line += w.Length + 1;
            }
            return sb.ToString().TrimEnd();
        }
    }
}
