using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// One day of recorded exercise for the signed-in user, read from the real tables: strength_sets (what the web Fitness page lists,
    /// and what the gym writes) and exercise_logs (other activities and durations). The same movement in both tables is merged, so a
    /// squat set is shown once, never twice. Views only read this.
    public class ExerciseData
    {
        public struct Item { public string Name, Detail, Tag; public int Sets, Reps; public double Minutes; }

        public readonly List<Item> Items = new List<Item>();
        public bool Loading, Loaded, HadError;
        public int TotalSets, TotalReps;
        public double TotalMinutes;

        sealed class Group
        {
            public string Name;
            public readonly List<int> SetReps = new List<int>();
            public double MaxKg, Minutes;
            public int LoggedSets, LoggedReps;
            public string LoggedDetail;
            public bool Vr;
        }

        public IEnumerator Load(SupabaseClient db, DateTime day)
        {
            Loading = true; Loaded = false; HadError = false;
            Items.Clear();
            TotalSets = TotalReps = 0; TotalMinutes = 0;
            if (db == null || !db.SignedIn) { HadError = true; Loading = false; Loaded = true; yield break; }

            string date = day.ToString("yyyy-MM-dd"), uid = db.UserId;
            JArray strength = null, logs = null;
            yield return Fetch(db, $"strength_sets?select=exercise_name,set_number,reps,weight_kg&user_id=eq.{uid}&date=eq.{date}&order=created_at.asc", r => strength = r);
            yield return Fetch(db, $"exercise_logs?select=exercise_name,sets_completed,reps_completed,duration_min,source&user_id=eq.{uid}&date=eq.{date}&order=created_at.asc", r => logs = r);
            if (strength == null || logs == null) { HadError = true; Loading = false; Loaded = true; yield break; }

            var order = new List<Group>();
            var byName = new Dictionary<string, Group>();
            Group Get(string name)
            {
                string key = (name ?? "Exercise").Trim().ToLowerInvariant();
                if (!byName.TryGetValue(key, out var g)) { g = new Group { Name = string.IsNullOrWhiteSpace(name) ? "Exercise" : name.Trim() }; byName[key] = g; order.Add(g); }
                return g;
            }

            foreach (var s in strength)
            {
                var g = Get((string)s["exercise_name"]);
                g.SetReps.Add((int)(DashboardData.Num(s["reps"]) ?? 0));
                g.MaxKg = Math.Max(g.MaxKg, DashboardData.Num(s["weight_kg"]) ?? 0);
            }

            // exercise_logs repeats the strength sets (same movement) and adds time. Take only the minutes for those; keep the whole
            // row for activities that have no strength sets (a run, a stretch).
            foreach (var l in logs)
            {
                string name = (string)l["exercise_name"];
                var g = Get(name);
                g.Minutes += DashboardData.Num(l["duration_min"]) ?? 0;
                if ((string)l["source"] == "vr") g.Vr = true;
                if (g.SetReps.Count == 0)
                {
                    int sets = Math.Max(1, (int)(DashboardData.Num(l["sets_completed"]) ?? 1));
                    string repsText = (string)l["reps_completed"] ?? "";
                    int reps = SumNumbers(repsText, out int count);
                    g.LoggedSets += sets;
                    g.LoggedReps += count == 1 ? reps * sets : reps;
                    g.LoggedDetail = string.IsNullOrEmpty(repsText) ? null : (sets > 1 ? $"{sets} x {repsText}" : $"{repsText} reps");
                }
            }

            foreach (var g in order)
            {
                int sets = g.SetReps.Count > 0 ? g.SetReps.Count : g.LoggedSets;
                int reps = 0;
                foreach (var r in g.SetReps) reps += r;
                if (g.SetReps.Count == 0) reps = g.LoggedReps;

                string detail;
                if (g.SetReps.Count > 0)
                {
                    detail = g.SetReps.Count <= 6 ? string.Join(" + ", g.SetReps) + " reps" : $"{sets} sets, {reps} reps";
                    if (g.MaxKg > 0) detail += $" at {g.MaxKg:0.#} kg";
                }
                else detail = g.LoggedDetail ?? (sets > 0 ? $"{sets} set{(sets == 1 ? "" : "s")}" : "Recorded");

                Items.Add(new Item { Name = g.Name, Detail = detail, Tag = g.Vr ? "Tracked in VR" : null, Sets = sets, Reps = reps, Minutes = g.Minutes });
                TotalSets += sets; TotalReps += reps; TotalMinutes += g.Minutes;
            }
            Loading = false; Loaded = true;
        }

        static int SumNumbers(string text, out int count)
        {
            int sum = 0; count = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\d+"))
            { sum += int.Parse(m.Value); count++; }
            return sum;
        }

        static IEnumerator Fetch(SupabaseClient db, string path, Action<JArray> ok)
        {
            string raw = null, err = null;
            yield return db.Get(path, r => raw = r, e => err = e);
            if (err != null) { Debug.LogWarning($"[Exercise] {path.Split('?')[0]} failed: {err}"); ok(null); yield break; }
            JArray parsed = null;
            try { parsed = JArray.Parse(raw); } catch (Exception) { }
            ok(parsed);
        }
    }
}
