using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// The coach's voice loop. Hold the RIGHT trigger (or A) to talk, release to send:
    /// mic -> Muse Voice Transcribe -> Muse Spark agent (tools on the real A Better U account) -> Wit.ai voice.
    /// It works on the dashboard screens and in the gym. In the gym the LEFT trigger also starts and ends a squat set.
    /// Add to any GameObject in the scene (the "VoiceLoop" object). It adds the other components it needs.
    public class VoiceLoopTest : MonoBehaviour
    {
        MicRecorder mic;
        WitSpeaker tts;
        MetaTranscribeClient asr;
        ModelApiClient spark;
        SupabaseClient supabase;
        CoachAgent agent;
        StatusBoard board;
        AppFlow flow;
        RepCounter reps;

        // Spoken the moment the words are recognised, so the user hears a response well before the real reply is ready.
        static readonly string[] Acks = { "Okay.", "Sure.", "One moment." };
        int ackIndex;

        // Spoken while a slow tool runs. True statements about what is happening, played once per turn from cached clips.
        static readonly Dictionary<string, string> Narrations = new Dictionary<string, string>
        {
            { "search_foods", "Searching the food database. It's a big one, so bear with me." },
            { "build_workout", "Building your workout." },
        };
        bool narrated;

        // Hands-free: say the wake name ("Hey Atlas") and then the request. The trigger still works as a fallback.
        public static string WakeName = "Atlas";
        public static string ReadyHeadline => $"Say \"Hey {WakeName}\"";
        public const string ReadyDetail = "or hold the right trigger to speak";
        const float FollowUpSeconds = 8f;                // after a reply, the next phrase needs no wake name
        HandsFreeListener hf;
        bool handsFree = true;
        float listenAfter, followUntil;
        Regex wakeRegex;
        const string WakeYes = "Yes?";

        static string[] PrefetchList()
        {
            var all = new List<string>(Acks);
            all.AddRange(Narrations.Values);
            all.Add(WakeYes);
            return all.ToArray();
        }

        void Narrate(string progress)
        {
            if (narrated || tts == null) return;
            int close = progress.IndexOf(']');
            if (!progress.StartsWith("[") || close < 2) return;
            if (Narrations.TryGetValue(progress.Substring(1, close - 1), out var phrase) && tts.PlayCached(phrase))
            {
                narrated = true;
                Debug.Log("[Timing] narration played");
            }
        }
        const int TargetReps = 10;
        SquatHud hud;
        MuseImageClient museImage;
        bool busy, inSet;
        int lastShownReps = -1;
        float nextMeter;

        /// The dashboard's assistant bar subscribes to this. (state, headline, detail)
        public Action<AssistantState, string, string> OnAssistant;
        /// Raised when the agent offers a short list of foods to tap (null clears it). The dashboard draws the picker.
        public Action<List<CoachAgent.Choice>> OnChoices;
        /// Raised with a short confirmation after the backend confirmed a write (food, injury note, set).
        public Action<string> OnSaved;

        string lastSaved;                 // confirmation text for the turn in progress
        readonly List<string> savedNames = new List<string>();   // short names of everything the backend confirmed this turn
        float readyAt;                    // when to return the assistant to "Ready" after Done or Error

        IEnumerator Start()
        {
            board = StatusBoard.Create(Camera.main != null ? Camera.main.transform : transform);

            var config = ModelApiConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                board.Show("No API key.\nCreate Assets/Resources/\nModelApiSecrets and paste key.");
                enabled = false;
                yield break;
            }

            museImage = new MuseImageClient(config);
            WakeName = string.IsNullOrEmpty(config.wakeName) ? "Atlas" : config.wakeName;
            // Optional "hey", then the name. "atlas" is also accepted as a few common mishearings.
            wakeRegex = new Regex(@"^\W*(?:(?:hey|hi|hello|okay|ok)\W+)?(?:" + Regex.Escape(WakeName.ToLowerInvariant()) + @"|atlus|at las)\b[\s,.!?:;-]*(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            asr = new MetaTranscribeClient(config);
            spark = new ModelApiClient(config);
            mic = gameObject.AddComponent<MicRecorder>();
            hf = gameObject.AddComponent<HandsFreeListener>();
            hf.UtteranceReady += wav => { if (!busy) StartCoroutine(HandsFreeTurn(wav)); };

            tts = gameObject.AddComponent<WitSpeaker>();
            tts.Init(config);
            StartCoroutine(tts.Prefetch(PrefetchList()));

            board.Show("Allow the microphone\nwhen asked...");
            yield return mic.EnsurePermission();
            Debug.Log("[VoiceLoop] TTS: " + (tts.Ready ? "Voice ready (Wit.ai)." : "No Wit token"));

            // The signed-in session survives "Back to hub" scene reloads; the agent works for whichever user signs in.
            supabase = AppFlow.Session ?? (AppFlow.Session = new SupabaseClient(config));
            agent = new CoachAgent(config, supabase);
            agent.ToolSucceeded += OnToolSucceeded;
            agent.ChoicesOffered += list => OnChoices?.Invoke(list);

            flow = gameObject.AddComponent<AppFlow>();
            flow.Bind(this);
            flow.Init(supabase, board);
        }

        /// Called by AppFlow when the user enters the gym: build the room and the squat counter.
        public void EnterGym()
        {
            gameObject.AddComponent<GymRoom>();   // the virtual gym, built from primitives

            // Squat counter: the headset's height is the only body signal the Quest gives us.
            reps = gameObject.AddComponent<RepCounter>();
            reps.head = Camera.main != null ? Camera.main.transform : transform;

            board.Show($"Hold RIGHT TRIGGER to talk.\nPress LEFT TRIGGER\nto start a squat set.\nLeft MENU button: menu.\n\nSigned in as {supabase.DisplayName ?? "user"}.");
        }

        void Update()
        {
            if (mic == null || flow == null) return;
            HandsFreeTick();
            var stage = flow.Current;
            if (stage == AppFlow.Stage.Login) return;           // nothing to talk to until someone is signed in
            if (flow.MenuOpen) return;                          // the trigger is clicking menu buttons, not recording
            bool inGym = stage == AppFlow.Stage.Gym;

            if (inGym)
            {
                // The left trigger starts a squat set, and ends it.
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
                        // Depth meter: the bar fills toward the "down" line; % is how far below standing you are.
                        float pct = Mathf.Max(0f, reps.CurrentDrop);
                        if (hud != null) hud.SetLive(reps.RepCount, TargetReps, pct, reps.downFraction, reps.InBottom, FormCue(pct));
                    }
                    return;
                }
            }
            if (busy) return;

            // After a confirmation or an error, the assistant goes back to "Ready" by itself.
            if (readyAt > 0f && Time.unscaledTime >= readyAt && !mic.IsRecording)
            {
                readyAt = 0f;
                Report(AssistantState.Ready, ReadyHeadline, ReadyDetail);
            }

            if (PressedDown())
            {
                // On the dashboard a trigger pull while the laser is on a button is a click, never a recording.
                if (!inGym && flow.PointerOverButton) { /* the pointer handles the click */ }
                else if (PauseHandsFree() && mic.StartRecording())
                {
                    readyAt = 0f;
                    board.Show("Listening...");
                    Report(AssistantState.Listening, "Listening...", "Release the trigger to send");
                }
            }
            else if (ReleasedUp() && mic.IsRecording) StartCoroutine(Turn(mic.StopRecording()));
            else if (mic.IsRecording && Time.unscaledTime >= nextMeter)
            {
                nextMeter = Time.unscaledTime + 0.15f;
                float level = mic.CurrentLevel();
                int bars = Mathf.Clamp(Mathf.RoundToInt(level * 40f), 0, 20);   // 0.5 peak = full bar
                board.Show("Listening...\n" + new string('|', bars) + new string('.', 20 - bars) + $"\nlevel {level:0.000}");
            }
        }

        void Report(AssistantState state, string headline, string detail) => OnAssistant?.Invoke(state, headline, detail);

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

        /// The user tapped a food in the picker: log it directly, then confirm only what the backend confirmed.
        public void ChooseFood(string foodId) => StartCoroutine(ChooseRoutine(foodId));

        IEnumerator ChooseRoutine(string foodId)
        {
            while (busy) yield return null;                            // let a reply that is still being spoken finish first
            busy = true;
            lastSaved = null; savedNames.Clear();
            Report(AssistantState.Processing, "Saving...", "");
            string result = null;
            yield return agent.LogChosen(foodId, r => result = r);
            if (savedNames.Count > 0)
            {
                Report(AssistantState.Done, "Saved", lastSaved);
                readyAt = Time.unscaledTime + 8f;
            }
            else
            {
                Debug.LogWarning("[VoiceLoop] picker log failed: " + result);
                Report(AssistantState.Error, "Couldn't save that", "Please try again.");
                readyAt = Time.unscaledTime + 7f;
            }
            busy = false;
        }

        // ── Hands-free ──────────────────────────────────────────────────────────────────────────────────

        // Listens only when nothing else needs the microphone or the speaker: not while thinking or speaking (so it cannot hear
        // itself), not mid-set, not in the gym menu, not on the login screen, and not while the trigger is recording.
        void HandsFreeTick()
        {
            if (hf == null) return;
            bool allowed = handsFree && !busy && !inSet && !mic.IsRecording && !flow.MenuOpen
                           && flow.Current != AppFlow.Stage.Login
                           && Time.unscaledTime >= listenAfter && (tts == null || !tts.IsSpeaking);
            if (allowed && !hf.Listening) hf.Resume();
            else if (!allowed && hf.Listening) hf.Pause();
        }

        bool PauseHandsFree() { hf?.Pause(); return true; }

        // True if the phrase starts with the wake name; rest is what followed it.
        bool TryStripWake(string heard, out string rest)
        {
            rest = null;
            var m = wakeRegex.Match(heard ?? "");
            if (!m.Success) return false;
            rest = m.Groups[1].Value.Trim();
            return true;
        }

        IEnumerator HandsFreeTurn(byte[] wav)
        {
            busy = true;     // stops listening while we work; nothing is shown until we know the phrase was meant for us
            float t0 = Time.realtimeSinceStartup;
            string heard = null, error = null;
            yield return asr.Transcribe(wav, t => heard = t, e => error = e);
            float tAsr = Time.realtimeSinceStartup - t0;

            if (error != null) { Debug.LogWarning("[HandsFree] transcribe failed: " + error); busy = false; yield break; }
            if (string.IsNullOrWhiteSpace(heard)) { busy = false; yield break; }

            bool followUp = Time.unscaledTime < followUntil;
            bool woke = TryStripWake(heard, out string command);
            if (!woke && followUp) command = heard;
            Debug.Log($"[HandsFree] heard \"{heard}\" -> {(woke ? "wake name" : followUp ? "follow-up" : "ignored (no wake name)")}, transcribe {tAsr:0.0}s");

            if (command == null) { busy = false; yield break; }       // not meant for us: dropped, nothing kept

            Report(AssistantState.Listening, "Heard you", "");        // also clears the judge's "Try saying" strip

            if (string.IsNullOrWhiteSpace(command))
            {
                // Only the name was said: answer, then wait for the request itself.
                tts?.PlayCached(WakeYes);
                followUntil = Time.unscaledTime + FollowUpSeconds;
                listenAfter = Time.unscaledTime + 1.2f;                // let "Yes?" finish
                Report(AssistantState.Listening, "I'm listening", "Go ahead");
                readyAt = followUntil;
                busy = false;
                yield break;
            }

            yield return HeardTurn(command, t0, tAsr);
        }

        // A form cue while squatting, from head position only: looking down is checked first, then leaning forward.
        string FormCue(float drop) => reps.BadForm ? "Keep straight and crouch down straight" : null;

        // A shareable session card from Muse Image. It never blocks saving the set: if it fails the card is simply not shown.
        IEnumerator MakeSessionCard(SquatHud forHud, int n)
        {
            forHud.ShowCardPending();
            const string prompt = "Achievement card artwork for a bodyweight squat workout. Dark navy background with teal accents, " +
                                  "a stylised athlete silhouette mid squat in the upper two thirds, flat vector style, the athlete exactly centred horizontally, uncluttered. " +
                                  "Leave the lower third empty. Absolutely no text, no letters and no numbers anywhere in the image.";
            Texture2D art = null; string err = null;
            yield return museImage.Generate(prompt, 768, t => art = t, e => err = e);
            if (forHud == null) { if (art != null) Destroy(art); yield break; }    // a new set started meanwhile
            if (art == null) { Debug.LogWarning("[MuseImage] card failed: " + err); forHud.HideCard(); yield break; }
            forHud.ShowCard(art, n, n == 1 ? "SQUAT" : "SQUATS");
        }

        IEnumerator BeginSet()
        {
            busy = true;
            board.Show("Stand tall, look\nstraight ahead and\nstay still...");
            yield return reps.Calibrate(2f);

            // Fix the HUD in the room, 1.5 m ahead of where the user stands, just below standing eye height.
            var head = Camera.main != null ? Camera.main.transform : transform;
            Vector3 fwd = head.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;
            if (hud != null) Destroy(hud.gameObject);
            hud = SquatHud.Create(new Vector3(head.position.x, Mathf.Max(head.position.y - 0.15f, 1.2f), head.position.z) + fwd * 1.5f,
                                  Quaternion.LookRotation(fwd, Vector3.up));
            hud.SetLive(0, TargetReps, 0f, reps.downFraction, false);
            board.Show("SQUAT SET");

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
            float perRep = n > 0 ? reps.SetSeconds / n : 0f, deepest = reps.DeepestDrop;
            int flagged = reps.FlaggedReps;
            if (n == 0)
            {
                if (hud != null) hud.SetSummary(0, 0f, deepest, 0, "No reps counted. Squat past the line to count.", false);
                board.Show("No reps counted.");
                busy = false; yield break;
            }

            // The rep count comes from the headset, not from the user or the model.
            if (hud != null) hud.SetSummary(n, perRep, deepest, flagged, "Saving to your activity...", null);
            if (hud != null) StartCoroutine(MakeSessionCard(hud, n));
            board.Show($"Set done: {n} reps.\nLogging...");
            lastSaved = null; savedNames.Clear();
            string turn = $"Context: the headset just counted a finished set: {n} bodyweight squat reps (exercise name: Bodyweight Squat). " +
                          "Log it with log_set and reply briefly with what was recorded.";
            string reply = null, error = null;
            yield return agent.Run(turn, p => board.Show($"Set done: {n} reps.\n{Wrap(p)}"), r => reply = r, e => error = e);

            // "Saved" is shown only if the backend confirmed log_set, never because the reply says so.
            bool saved = savedNames.Count > 0;
            if (hud != null) hud.SetSummary(n, perRep, deepest, flagged, saved ? "Saved to your activity" : "Not saved. Please try again.", saved);

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
            if (wav == null)
            {
                board.Show("Too short, try again.\nHold trigger while talking.");
                Report(AssistantState.Ready, "Ask A Better U", "That was too short. Hold the trigger while you talk.");
                yield break;
            }

            busy = true;
            float t0 = Time.realtimeSinceStartup;
            board.Show("Transcribing...");
            Report(AssistantState.Processing, "Transcribing...", "");

            // Debug aid: keep the last recording so it can be pulled with adb and replayed with curl.
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.persistentDataPath, "last.wav"), wav); }
            catch (Exception ex) { Debug.LogWarning("[VoiceLoop] Could not save last.wav: " + ex.Message); }

            string heard = null, error = null;
            yield return asr.Transcribe(wav, t => heard = t, e => error = e);
            float tAsr = Time.realtimeSinceStartup - t0;

            if (error != null)
            {
                Debug.LogError("[VoiceLoop] FULL ERROR: " + error);
                board.Show("Transcribe failed:\n" + Wrap(Short(error, 160)));
                Report(AssistantState.Error, "I couldn't hear that", "Hold right trigger and try again.");
                readyAt = Time.unscaledTime + 7f;
                busy = false;
                yield break;
            }
            if (string.IsNullOrWhiteSpace(heard))
            {
                board.Show("Didn't catch that.\nTry again.");
                Report(AssistantState.Error, "I didn't catch that", "Hold right trigger and try again.");
                readyAt = Time.unscaledTime + 6f;
                busy = false;
                yield break;
            }

            yield return HeardTurn(heard, t0, tAsr);
        }

        // Everything after the words are known: acknowledge, run the agent, speak, confirm. Shared by push-to-talk and the wake name.
        IEnumerator HeardTurn(string heard, float t0, float tAsr)
        {
            busy = true;
            string error = null;
            lastSaved = null;
            savedNames.Clear();
            narrated = false;
            OnChoices?.Invoke(null);                                  // a new request replaces any picker still on screen
            agent.CanShowChoices = flow != null && flow.Current != AppFlow.Stage.Gym;

            if (tts != null && tts.PlayCached(Acks[ackIndex++ % Acks.Length])) Debug.Log("[Timing] acknowledgement played");
            board.Show($"You: {Wrap(heard)}\n\nThinking...");
            Report(AssistantState.Processing, $"You said: \"{heard}\"", "Working on it...");

            string reply = null;
            float t1 = Time.realtimeSinceStartup;
            string userTurn = $"Context: no set in progress, no rep data.\nThe user said: \"{heard}\"";
            if (supabase != null && supabase.SignedIn)
                yield return agent.Run(userTurn, p =>
                {
                    Narrate(p);
                    board.Show($"You: {Wrap(heard)}\n\n{Wrap(p)}");
                    Report(AssistantState.Processing, $"You said: \"{heard}\"", Friendly(p));
                }, r => reply = r, e => error = e);
            else
                yield return spark.Ask(userTurn, r => reply = r, e => error = e);
            float tSpark = Time.realtimeSinceStartup - t1;

            if (reply == null)
            {
                board.Show("Coach failed:\n" + Wrap(Short(error ?? "no reply", 160)));
                Debug.LogError("[VoiceLoop] Coach error: " + error);
                Report(AssistantState.Error, "Something went wrong", "Hold right trigger to try again.");
                readyAt = Time.unscaledTime + 7f;
                busy = false;
                yield break;
            }

            // Honesty check: the model sometimes says "logged" without ever calling the tool (seen Oct 28: three searches, no
            // writes, "all logged"). If the reply claims a save but the backend confirmed nothing, replace it.
            bool falseClaim = savedNames.Count == 0 && ClaimsSave(reply);
            if (falseClaim)
            {
                Debug.LogWarning("[VoiceLoop] Reply claimed a save but no write succeeded. Replacing it. Original: " + reply);
                reply = "I wasn't able to save that, so nothing has been logged. Could you try again?";
            }

            board.Show($"You: {Wrap(heard)}\n\nCoach: {Wrap(reply)}\n\nASR {tAsr:0.0}s  Spark {tSpark:0.0}s");
            Report(falseClaim ? AssistantState.Error : AssistantState.Speaking, falseClaim ? "Nothing was saved" : "A Better U", reply);

            bool spoken = false;
            tts.Speak(reply, () => spoken = true);
            while (!spoken) yield return null;
            Debug.Log($"[Timing] turn: asr {tAsr:0.00}s, agent {tSpark:0.00}s, speech started {tts.FirstAudioDelay:0.00}s after reply, speech to reply-heard total {Time.realtimeSinceStartup - t0:0.00}s");

            // "Saved" appears only if the backend confirmed a write during this turn, and lists exactly what was written.
            if (savedNames.Count > 0)
            {
                Report(AssistantState.Done, "Saved", savedNames.Count == 1 ? lastSaved : $"{savedNames.Count} saved: {string.Join(", ", savedNames)}");
                readyAt = Time.unscaledTime + 8f;
            }
            else if (falseClaim)
            {
                Report(AssistantState.Error, "Nothing was saved", "Hold right trigger and try again.");
                readyAt = Time.unscaledTime + 8f;
            }
            else Report(AssistantState.Ready, ReadyHeadline, ReadyDetail);
            listenAfter = Time.unscaledTime + 0.7f;                  // do not hear our own voice fading out
            followUntil = Time.unscaledTime + FollowUpSeconds;       // a natural reply needs no wake name
            busy = false;
        }

        // Turns the agent's "[tool] {args}" progress text into something a person would want to read.
        static string Friendly(string progress)
        {
            int close = progress.IndexOf(']');
            string tool = progress.StartsWith("[") && close > 1 ? progress.Substring(1, close - 1) : "";
            switch (tool)
            {
                case "search_foods": return "Searching your food database...";
                case "log_food": return "Saving your meal...";
                case "find_my_meals": return "Looking at your saved meals...";
                case "log_meal": return "Saving your meal...";
                case "report_injury": return "Saving your note...";
                case "build_workout": return "Building your workout...";
                case "log_set": return "Saving your set...";
                default: return "Working on it...";
            }
        }

        void OnToolSucceeded(string tool, JObject result)
        {
            string text = null, shortName = null;
            switch (tool)
            {
                case "log_food":
                case "log_meal":
                    shortName = (string)result["logged_name"];
                    text = $"{shortName} logged to {(string)result["meal"]}";
                    break;
                case "report_injury":
                {
                    string side = (string)result["side"];
                    string area = ((string)result["body_area"] ?? "area").Replace('_', ' ');
                    shortName = (side != null ? side + " " : "") + area;
                    text = ((bool?)result["already_recorded"] == true ? "Already noted: " : "Noted: ") + shortName;
                    break;
                }
                case "log_set":
                    shortName = $"{(int)result["reps"]} reps {(string)result["logged_exercise"]}";
                    text = $"{(int)result["reps"]} reps of {(string)result["logged_exercise"]} saved to your activity";
                    break;
            }
            if (text == null) return;
            lastSaved = text;
            savedNames.Add(shortName);
            OnSaved?.Invoke(text);
        }

        // True if the reply says something was logged or saved, and is not a refusal or a "could not" message.
        static bool ClaimsSave(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return false;
            bool claims = Regex.IsMatch(reply, @"\b(logged|saved|recorded|added)\b", RegexOptions.IgnoreCase);
            bool denies = Regex.IsMatch(reply, @"\b(not|nothing|couldn'?t|could not|can'?t|cannot|unable|wasn'?t|didn'?t|failed)\b|n't", RegexOptions.IgnoreCase);
            return claims && !denies;
        }

        static string Short(string s, int max = 120) => s.Length > max ? s.Substring(0, max) + "..." : s;

        // Wraps long lines at ~32 chars so the TextMesh stays readable.
        static string Wrap(string s, int width = 32) => VRUi.Wrap(s, width);
    }
}
