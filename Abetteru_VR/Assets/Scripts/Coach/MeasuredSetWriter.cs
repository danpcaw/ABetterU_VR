using System;
using System.Collections;
using Newtonsoft.Json.Linq;

namespace ABetterU.Coach
{
    /// Writes measured sets directly. Stable primary keys and per-table acknowledgements make retries safe.
    public sealed class MeasuredSetWriter
    {
        readonly string owner;
        readonly Func<string, JObject, Action<string>, IEnumerator> insert;
        public MeasuredSetWriter(string owner, Func<string, JObject, Action<string>, IEnumerator> insert)
        { this.owner = owner; this.insert = insert; }

        public IEnumerator Save(SquatWorkoutSession session, SquatWorkoutSession.SetResult set, Action<string> done)
        {
            if (set.Reps <= 0) { done("No reps to save."); yield break; }
            string error = null;
            if (!set.StrengthSaved)
            {
                var strength = new JObject
                {
                    ["id"] = set.StrengthId, ["user_id"] = owner, ["date"] = set.Date,
                    ["exercise_name"] = "Bodyweight Squat", ["set_number"] = set.DatabaseSetNumber > 0 ? set.DatabaseSetNumber : set.Index,
                    ["reps"] = set.Reps, ["weight_kg"] = 0, ["is_warmup"] = false,
                };
                yield return insert("strength_sets", strength, e => error = e);
                if (error != null) { done(error); yield break; }
                set.StrengthSaved = true;
            }
            if (!set.ActivitySaved)
            {
                var activity = new JObject
                {
                    ["id"] = set.ActivityId, ["user_id"] = owner, ["date"] = set.Date,
                    ["exercise_name"] = "Bodyweight Squat", ["sets_completed"] = 1, ["reps_completed"] = set.Reps.ToString(),
                    ["duration_min"] = (int)Math.Round(set.Seconds / 60f), ["source"] = "vr",
                    ["notes"] = $"VR session {session.Id}; set {set.Index}/{session.PlannedSets}; target {session.TargetReps} reps",
                };
                yield return insert("exercise_logs", activity, e => error = e);
                if (error != null) { done(error); yield break; }
                set.ActivitySaved = true;
            }
            done(null);
        }
    }
}
