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
        AndroidTts tts;
        MetaTranscribeClient asr;
        ModelApiClient spark;
        StatusBoard board;
        bool busy;

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

            asr = new MetaTranscribeClient(config);
            spark = new ModelApiClient(config);
            mic = gameObject.AddComponent<MicRecorder>();

            tts = gameObject.AddComponent<AndroidTts>();

            board.Show("Allow the microphone\nwhen asked...");
            yield return mic.EnsurePermission();

            yield return tts.Initialise();
            string ttsNote = tts.Ready ? "Voice ready." : "No voice: " + tts.InitError;
            Debug.Log("[VoiceLoop] TTS: " + ttsNote);
            board.Show($"Hold RIGHT TRIGGER to talk.\nRelease to send.\n\n{ttsNote}");
        }

        void Update()
        {
            if (mic == null || busy) return;

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
            yield return spark.Ask($"Context: no set in progress, no rep data.\nThe user said: \"{heard}\"", r => reply = r, e => error = e);
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
