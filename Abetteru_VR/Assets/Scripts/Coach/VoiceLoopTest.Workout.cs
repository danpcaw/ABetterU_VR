using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    public partial class VoiceLoopTest
    {
        SquatWorkoutSession workout;
        MeasuredSetWriter workoutWriter;
        string workoutOwner;
        bool pendingSetStart, pendingWorkoutFinish, workoutCalibrated, restAnnounced, applicationPaused;

        public bool WorkoutControlsVisible => !busy && workout != null &&
            (workout.Phase == WorkoutPhase.Rest || workout.Phase == WorkoutPhase.Complete);
        public bool WorkoutInProgress => workout != null && workout.Phase != WorkoutPhase.Complete;

        string WorkoutResult(bool ok, string message, bool openedGym = false) => new JObject
        {
            ["ok"] = ok, ["message"] = message, ["opened_gym"] = openedGym,
            ["sets"] = workout?.PlannedSets ?? 0, ["reps_per_set"] = workout?.TargetReps ?? 0,
            ["rest_seconds"] = workout?.RestSeconds ?? 0, ["completed_reps"] = workout?.TotalReps ?? 0,
            ["saved_sets"] = workout?.SavedSets ?? 0,
        }.ToString(Newtonsoft.Json.Formatting.None);

        string ConfigureSquatWorkout(int sets, int target, int rest)
        {
            if (supabase == null || !supabase.SignedIn || flow == null || flow.Current == AppFlow.Stage.Login)
                return WorkoutResult(false, "Sign in before starting a tracked workout.");
            if (workout != null && (workout.Phase != WorkoutPhase.Complete || workout.UnsavedSets > 0))
                return WorkoutResult(false, "A workout is already active or has unsaved sets. Finish it and retry pending saves first.");
            SquatWorkoutSession next = null;
            try { next = new SquatWorkoutSession(sets, target, rest); }
            catch (ArgumentOutOfRangeException e) { return WorkoutResult(false, e.Message); }
            bool opened = flow.Current != AppFlow.Stage.Gym;
            if (opened && !flow.NavigateTo("virtual_gym")) return WorkoutResult(false, "The gym could not be opened.");
            workout = next;
            workoutOwner = supabase.UserId;
            workoutWriter = new MeasuredSetWriter(workoutOwner, supabase.InsertWorkoutRowOnce);
            workoutCalibrated = false;
            pendingWorkoutFinish = false;
            pendingSetStart = true;
            return WorkoutResult(true, "Workout configured. Stand tall; calibration starts after this reply. No reps logged yet.", opened);
        }

        string ControlSquatWorkout(string action, int seconds, int sets)
        {
            if (workout == null) return WorkoutResult(false, "There is no tracked workout to control.");
            switch (action)
            {
                case "finish":
                    pendingWorkoutFinish = true; pendingSetStart = false;
                    return WorkoutResult(true, "Finish requested. Only actual completed reps will be recorded.");
                case "skip_rest":
                case "start_next":
                    if (!workout.SkipRest()) return WorkoutResult(false, "The workout is not resting between sets.");
                    pendingSetStart = true;
                    return WorkoutResult(true, "Next set requested. Stand tall; it starts after this reply.");
                case "add_rest":
                    if (!workout.AddRest(seconds)) return WorkoutResult(false, "Extra rest is available only between sets, from 1 to 300 seconds.");
                    ShowWorkoutView();
                    return WorkoutResult(true, $"Rest extended; {Mathf.CeilToInt(workout.RestRemaining)} seconds remain.");
                case "change_sets":
                    if (!workout.ChangeSets(sets)) return WorkoutResult(false, "That set count would discard work, exceed 10 sets, or change a completed workout.");
                    if (hud != null) hud.SetWorkoutProgress(workout.NextSet, workout.PlannedSets);
                    ShowWorkoutView();
                    return WorkoutResult(true, "The workout's total set count was updated.");
                default: return WorkoutResult(false, "Unknown workout action.");
            }
        }

        public void RequestFinishWorkout()
        {
            if (!busy && WorkoutInProgress) { pendingWorkoutFinish = true; pendingSetStart = false; }
        }

        void PauseWorkoutTracking()
        {
            if (reps != null && reps.Active) { reps.Active = false; reps.CancelPartialRep(); }
        }

        bool HandleWorkoutUpdate()
        {
            if (applicationPaused) { PauseWorkoutTracking(); return true; }
            if (pendingWorkoutFinish && !busy)
            {
                pendingWorkoutFinish = false; pendingSetStart = false;
                if (inSet) StartCoroutine(EndSet(true));
                else { workout?.Finish(); ShowWorkoutView(); }
                return true;
            }
            if (pendingSetStart && !busy && (tts == null || !tts.IsSpeaking))
            {
                pendingSetStart = false;
                StartCoroutine(BeginSet()); return true;
            }
            if (SetTogglePressed() && !busy && !mic.IsRecording && !flow.PointerOverButton)
            {
                StartCoroutine(inSet ? EndSet() : BeginSet()); return true;
            }
            if (inSet)
            {
                bool tracking = !busy && !mic.IsRecording && (tts == null || !tts.IsSpeaking);
                if (!tracking) PauseWorkoutTracking();
                else reps.Active = true;
                float drop = Mathf.Max(0, reps.CurrentDrop);
                if (tracking && workout != null && reps.RepCount >= workout.TargetReps && !reps.InBottom && drop <= reps.upFraction)
                { StartCoroutine(EndSet()); return true; }
                if (Time.unscaledTime >= nextMeter)
                {
                    nextMeter = Time.unscaledTime + 0.1f;
                    hud?.SetWorkoutProgress(workout.NextSet, workout.PlannedSets);
                    hud?.SetLive(reps.RepCount, workout.TargetReps, drop, reps.downFraction, reps.InBottom, FormCue(drop));
                }
                // Push-to-talk remains available mid-set. Counting pauses through recording and the reply.
                if (!busy && PressedDown() && PauseHandsFree() && mic.StartRecording())
                { PauseWorkoutTracking(); board.Show("Listening… Workout counting paused."); }
                else if (!busy && ReleasedUp() && mic.IsRecording) StartCoroutine(Turn(mic.StopRecording()));
                return true;
            }
            if (workout != null && workout.Phase == WorkoutPhase.Rest && !busy)
            {
                workout.TickRest(Time.unscaledDeltaTime);
                if (Time.unscaledTime >= nextMeter) { nextMeter = Time.unscaledTime + 0.1f; hud?.SetRest(workout); }
                if (workout.RestRemaining <= 0 && !restAnnounced && (tts == null || !tts.IsSpeaking))
                {
                    restAnnounced = true;
                    tts?.Speak("Rest finished. Stand tall and press the left trigger for the next set.", () => { });
                }
            }
            return false;
        }

        IEnumerator BeginSet()
        {
            if (workout == null || workout.Phase == WorkoutPhase.Complete)
            {
                var result = JObject.Parse(ConfigureSquatWorkout(workout?.PlannedSets ?? 1, workout?.TargetReps ?? 10, workout?.RestSeconds ?? 60));
                if ((bool?)result["ok"] != true) { board.Show((string)result["message"]); yield break; }
                pendingSetStart = false;
            }
            if (reps == null || !workout.StartSet()) yield break;
            busy = true;
            flow.SetWorkoutPointer(false);
            if (!workoutCalibrated)
            {
                board.Show("Stand tall, look straight ahead and stay still for calibration.");
                yield return reps.Calibrate(2f);
                workoutCalibrated = true;
            }
            else reps.ResetSet();
            var head = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = head.forward; forward.y = 0;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            if (hud != null) Destroy(hud.gameObject);
            hud = SquatHud.Create(new Vector3(head.position.x, Mathf.Max(head.position.y - 0.15f, 1.2f), head.position.z) + forward * 1.5f,
                Quaternion.LookRotation(forward, Vector3.up));
            hud.SetWorkoutProgress(workout.NextSet, workout.PlannedSets);
            hud.SetLive(0, workout.TargetReps, 0, reps.downFraction, false);
            board.Show($"Set {workout.NextSet} of {workout.PlannedSets} · {workout.TargetReps} reps");
            reps.Active = true; inSet = true; busy = false;
        }

        IEnumerator EndSet(bool finishWorkout = false)
        {
            if (!inSet || workout == null) yield break;
            reps.Active = false; inSet = false; busy = true;
            var result = workout.EndSet(reps.RepCount, reps.SetSeconds, reps.DeepestDrop, reps.FlaggedReps, finishWorkout);
            if (result != null)
            {
                hud?.SetSummary(result.Reps, result.Seconds / result.Reps, result.Deepest, result.Flagged, "Saving this set...", null);
                board.Show($"Set {result.Index}: {result.Reps} reps. Saving…");
                yield return SaveMeasuredSet(result);
                workout.FinishSaving();
            }
            restAnnounced = false; busy = false;
            ShowWorkoutView();
            if (workout.Phase == WorkoutPhase.Complete && workout.TotalReps > 0 && hud != null)
                StartCoroutine(MakeSessionCard(hud, workout.TotalReps));
            string announcement = workout.Phase == WorkoutPhase.Complete
                ? $"Workout finished. {workout.TotalReps} reps across {workout.Results.Count} sets."
                : $"Set {workout.Results.Count} complete. Rest for {workout.RestSeconds} seconds.";
            if (workout.UnsavedSets > 0) announcement += " Some sets still need saving. Use Retry saves.";
            tts?.Speak(announcement, () => { });
        }

        IEnumerator SaveMeasuredSet(SquatWorkoutSession.SetResult set)
        {
            if (supabase == null || supabase.UserId != workoutOwner || !supabase.SignedIn) yield break;
            if (set.DatabaseSetNumber == 0)
            {
                string raw = null, error = null;
                yield return supabase.Get($"strength_sets?select=set_number&user_id=eq.{workoutOwner}&date=eq.{set.Date}&exercise_name=eq.Bodyweight%20Squat&order=set_number.desc&limit=1",
                    r => raw = r, e => error = e);
                if (error != null) { Debug.LogWarning("[Workout] Could not prepare set number."); yield break; }
                JArray rows = null;
                try { rows = JArray.Parse(raw); } catch (Exception) { }
                if (rows == null) yield break;
                set.DatabaseSetNumber = rows.Count == 0 ? 1 : ((int?)rows[0]["set_number"] ?? 0) + 1;
            }
            string failure = null;
            yield return workoutWriter.Save(workout, set, e => failure = e);
            if (set.Saved) OnSaved?.Invoke($"Set {set.Index}: {set.Reps} squat reps saved");
            else Debug.LogWarning("[Workout] Set save pending: " + failure);
        }

        IEnumerator RetryWorkoutSaves()
        {
            if (busy || workout == null) yield break;
            busy = true; flow.SetWorkoutPointer(false);
            foreach (var set in workout.Results) if (!set.Saved) yield return SaveMeasuredSet(set);
            busy = false; ShowWorkoutView();
        }

        void ShowWorkoutView()
        {
            if (workout == null || hud == null) return;
            bool complete = workout.Phase == WorkoutPhase.Complete;
            if (!complete && workout.Phase != WorkoutPhase.Rest) return;
            if (complete) hud.SetWorkoutSummary(workout); else hud.SetRest(workout);
            hud.SetWorkoutControls(
                () =>
                {
                    Debug.Log($"[Workout] button next/new: busy={busy}, complete={complete}");
                    if (busy) return;
                    if (complete)
                    {
                        var configured = JObject.Parse(ConfigureSquatWorkout(workout.PlannedSets, workout.TargetReps, workout.RestSeconds));
                        if ((bool?)configured["ok"] != true) board.Show((string)configured["message"]);
                    }
                    else { workout.SkipRest(); pendingSetStart = true; }
                },
                () => { Debug.Log($"[Workout] button +30: busy={busy}"); if (!busy) { workout.AddRest(30); hud.SetRest(workout); } },
                () => { Debug.Log($"[Workout] button finish: busy={busy}, inProgress={WorkoutInProgress}"); RequestFinishWorkout(); },
                () => { Debug.Log($"[Workout] button retry: busy={busy}"); if (!busy) StartCoroutine(RetryWorkoutSaves()); },
                () => { Debug.Log($"[Workout] button hub: busy={busy}"); if (!busy) flow.LeaveGym(); }, complete, workout.UnsavedSets > 0);
            flow.SetWorkoutPointer(!busy);
            board.Show(complete ? $"Workout finished: {(workout.Results.Count == 0 ? "0" : workout.RepBreakdown)} reps. {workout.SavedSets} sets saved."
                : $"Rest · Next is set {workout.NextSet} of {workout.PlannedSets}. Left trigger when ready.");
        }
    }
}
