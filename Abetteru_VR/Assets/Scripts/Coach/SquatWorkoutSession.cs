using System;
using System.Collections.Generic;
using System.Linq;

namespace ABetterU.Coach
{
    public enum WorkoutPhase { Ready, Active, Saving, Rest, Complete }

    /// Workout progression only; no tracking, networking, audio or Unity time dependencies.
    public sealed class SquatWorkoutSession
    {
        public sealed class SetResult
        {
            public readonly string StrengthId = Guid.NewGuid().ToString();
            public readonly string ActivityId = Guid.NewGuid().ToString();
            public readonly string Date = DateTime.Now.ToString("yyyy-MM-dd");
            public int Index, Reps, Flagged, DatabaseSetNumber;
            public float Seconds, Deepest;
            public bool StrengthSaved, ActivitySaved;
            public bool Saved => StrengthSaved && ActivitySaved;
        }

        readonly List<SetResult> results = new List<SetResult>();
        public IReadOnlyList<SetResult> Results => results;
        public string Id { get; } = Guid.NewGuid().ToString();
        public int PlannedSets { get; private set; }
        public int TargetReps { get; }
        public int RestSeconds { get; }
        public float RestRemaining { get; private set; }
        public WorkoutPhase Phase { get; private set; } = WorkoutPhase.Ready;
        public int NextSet => results.Count + 1;
        public int TotalReps => results.Sum(s => s.Reps);
        public int SavedSets => results.Count(s => s.Saved);
        public int UnsavedSets => results.Count(s => !s.Saved);
        public string RepBreakdown => string.Join(" + ", results.Select(s => s.Reps));
        bool finishRequested;

        public SquatWorkoutSession(int sets, int reps, int restSeconds)
        {
            if (sets < 1 || sets > 10) throw new ArgumentOutOfRangeException(nameof(sets), "Choose 1 to 10 sets.");
            if (reps < 1 || reps > 50) throw new ArgumentOutOfRangeException(nameof(reps), "Choose 1 to 50 reps.");
            if (restSeconds < 0 || restSeconds > 300) throw new ArgumentOutOfRangeException(nameof(restSeconds), "Rest must be 0 to 300 seconds.");
            PlannedSets = sets; TargetReps = reps; RestSeconds = restSeconds;
        }

        public bool StartSet()
        {
            if (Phase != WorkoutPhase.Ready && Phase != WorkoutPhase.Rest) return false;
            if (finishRequested || results.Count >= PlannedSets) return false;
            Phase = WorkoutPhase.Active;
            RestRemaining = 0;
            return true;
        }

        public SetResult EndSet(int reps, float seconds, float deepest, int flagged, bool finishWorkout)
        {
            if (Phase != WorkoutPhase.Active) return null;
            finishRequested |= finishWorkout;
            if (reps <= 0) { Phase = WorkoutPhase.Complete; return null; }
            var result = new SetResult { Index = results.Count + 1, Reps = reps, Seconds = Math.Max(0, seconds),
                Deepest = Math.Max(0, deepest), Flagged = Math.Max(0, flagged) };
            results.Add(result);
            Phase = WorkoutPhase.Saving;
            return result;
        }

        public void FinishSaving()
        {
            if (Phase != WorkoutPhase.Saving) return;
            if (finishRequested || results.Count >= PlannedSets) Phase = WorkoutPhase.Complete;
            else { Phase = WorkoutPhase.Rest; RestRemaining = RestSeconds; }
        }

        public void TickRest(float seconds)
        {
            if (Phase == WorkoutPhase.Rest) RestRemaining = Math.Max(0, RestRemaining - Math.Max(0, seconds));
        }
        public bool AddRest(int seconds)
        {
            if (Phase != WorkoutPhase.Rest || seconds < 1 || seconds > 300) return false;
            RestRemaining = Math.Min(300, RestRemaining + seconds);
            return true;
        }
        public bool SkipRest()
        {
            if (Phase != WorkoutPhase.Rest) return false;
            RestRemaining = 0; return true;
        }
        public bool ChangeSets(int sets)
        {
            int minimum = results.Count + (Phase == WorkoutPhase.Active ? 1 : 0);
            if (Phase == WorkoutPhase.Saving || Phase == WorkoutPhase.Complete || sets < Math.Max(1, minimum) || sets > 10) return false;
            PlannedSets = sets;
            if (results.Count >= sets && Phase != WorkoutPhase.Active) Phase = WorkoutPhase.Complete;
            return true;
        }
        public void Finish()
        {
            finishRequested = true;
            if (Phase != WorkoutPhase.Active && Phase != WorkoutPhase.Saving) Phase = WorkoutPhase.Complete;
        }
    }
}
