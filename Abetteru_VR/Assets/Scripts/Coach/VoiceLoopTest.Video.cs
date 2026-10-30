using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    public partial class VoiceLoopTest
    {
        InstructorVideo instructor;
        AppFlow.Stage instructorStage;

        static string VideoFail(string why) => new JObject { ["ok"] = false, ["error"] = why }.ToString(Newtonsoft.Json.Formatting.None);

        /// From the "How to" button on My Experiences.
        public void ShowHowTo(string exercise = "Bodyweight Squat") => StartCoroutine(HowToFromButton(exercise));

        IEnumerator HowToFromButton(string exercise)
        {
            string reply = null;
            yield return ShowExerciseVideoRoutine(exercise, r => reply = r);
            try
            {
                var j = JObject.Parse(reply);
                if ((bool?)j["ok"] != true) Report(AssistantState.Error, "No video", (string)j["error"]);
            }
            catch (Exception) { }
        }

        /// The tool: find the exercise's how-to video in the platform's library, matched to the account's sex (no sex set means male),
        /// and show it. Says plainly if there is no video, and never describes how to do the exercise itself.
        IEnumerator ShowExerciseVideoRoutine(string exercise, Action<string> done)
        {
            if (supabase == null || !supabase.SignedIn) { done(VideoFail("Sign in first.")); yield break; }
            if (inSet) { done(VideoFail("A set is in progress. Finish it first.")); yield break; }
            string wanted = string.IsNullOrWhiteSpace(exercise) ? "Bodyweight Squat" : exercise.Trim();
            CloseInstructor();

            // Which version: the profile's sex decides, the same way the web app personalises its demos. Not set means male.
            string sexRaw = null, err = null;
            yield return supabase.Get($"profiles?select=sex&user_id=eq.{supabase.UserId}", r => sexRaw = r, e => err = e);
            bool female = false;
            try { female = string.Equals((string)JArray.Parse(sexRaw)[0]["sex"], "female", StringComparison.OrdinalIgnoreCase); } catch (Exception) { }

            string raw = null; err = null;
            // The library is read through get_exercise_library (the same function the workout builder uses): ordinary accounts are not
            // allowed to read the exercise_library table directly, only that function.
            yield return supabase.Rpc("get_exercise_library?select=name,video_url,video_url_male,video_url_female,animation_url_male,animation_url_female" +
                $"&name=ilike.*{Uri.EscapeDataString(wanted)}*&limit=8", "{}", r => raw = r, e => err = e);
            if (err != null) Debug.LogWarning("[Video] Library lookup failed: " + err);
            JToken best = null;
            try
            {
                foreach (var row in JArray.Parse(raw))
                {
                    string n = (string)row["name"] ?? "";
                    if (best == null || string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase) && !string.Equals((string)best["name"], wanted, StringComparison.OrdinalIgnoreCase)
                        || (!string.Equals((string)best["name"], wanted, StringComparison.OrdinalIgnoreCase) && n.Length < ((string)best["name"]).Length)) best = row;
                }
            }
            catch (Exception) { }
            if (best == null) { done(VideoFail($"I couldn't find an exercise called {wanted} in the library.")); yield break; }

            // The sex-specific upload first, then the general one, then the generated animation.
            string[] order = female
                ? new[] { "video_url_female", "video_url", "animation_url_female" }
                : new[] { "video_url_male", "video_url", "animation_url_male" };
            string url = null;
            foreach (var field in order) { string v = (string)best[field]; if (!string.IsNullOrWhiteSpace(v)) { url = v; break; } }
            if (url == null) { done(VideoFail($"There is no how-to video for {(string)best["name"]} yet.")); yield break; }

            var head = Camera.main != null ? Camera.main.transform : transform;
            bool gym = flow != null && flow.Current == AppFlow.Stage.Gym;
            bool inRoom = gym && InstructorVideo.KeyShaderAvailable;

            var go = new GameObject("Instructor video");
            var video = go.AddComponent<InstructorVideo>();
            instructor = video;
            instructorStage = flow.Current;
            video.Closed = () =>
            {
                if (instructor == video) instructor = null;
                if (flow != null && flow.Current == AppFlow.Stage.Gym) flow.SetWorkoutPointer(WorkoutControlsVisible);
            };
            if (gym) flow.SetWorkoutPointer(true);                         // the Close button needs the pointer in the gym

            yield return video.Open(url, "How to: " + (string)best["name"], inRoom, head);
            if (!video.Ready)
            {
                Debug.LogWarning("[Video] Could not play " + url + " : " + video.Error);
                video.Close();
                done(VideoFail("The video couldn't be loaded."));
                yield break;
            }
            done(new JObject { ["ok"] = true, ["exercise"] = (string)best["name"], ["shown"] = video.InWorld ? "life-size in the room" : "in a panel" }
                .ToString(Newtonsoft.Json.Formatting.None));
        }

        string CloseVideoTool()
        {
            if (instructor == null) return VideoFail("There is no video open.");
            CloseInstructor();
            return new JObject { ["ok"] = true }.ToString(Newtonsoft.Json.Formatting.None);
        }

        void CloseInstructor()
        {
            if (instructor != null) instructor.Close();
            instructor = null;
        }

        // A video belongs to the screen it was opened on; leaving that screen closes it.
        void VideoTick()
        {
            if (instructor != null && flow != null && flow.Current != instructorStage) CloseInstructor();
        }
    }
}
