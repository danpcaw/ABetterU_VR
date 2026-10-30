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
        public struct Item { public string Name, Detail, Tag, ImageUrl; public int Sets, Reps; public double Minutes, Kcal; }

        public readonly List<Item> Items = new List<Item>();
        public bool Loading, Loaded, HadError;
        public int TotalSets, TotalReps;
        public double TotalMinutes, TotalKcal;                 // estimated calories burnt, worked out the way the web Fitness page does

        struct Lib { public double Met, PerRep; public string ThumbMale, ThumbFemale; }
        static Dictionary<string, Lib> library;                // read once per session: the library never changes while the app runs
        const double DefaultMet = 5.0;

        sealed class Group
        {
            public string Name;
            public readonly List<int> SetReps = new List<int>();
            public double MaxKg, Minutes, Kcal;
            public int LoggedSets, LoggedReps;
            public string LoggedDetail;
            public bool Vr, HadLog;
            public double LogKcal;
        }

        public IEnumerator Load(SupabaseClient db, DateTime day)
        {
            Loading = true; Loaded = false; HadError = false;
            Items.Clear();
            TotalSets = TotalReps = 0; TotalMinutes = TotalKcal = 0;
            if (db == null || !db.SignedIn) { HadError = true; Loading = false; Loaded = true; yield break; }

            string date = day.ToString("yyyy-MM-dd"), uid = db.UserId;
            JArray strength = null, logs = null, prof = null;
            yield return LoadLibrary(db);
            yield return Fetch(db, $"profiles?select=sex,weight_kg&user_id=eq.{uid}", r => prof = r);
            string sex = prof != null && prof.Count > 0 ? (string)prof[0]["sex"] : null;
            double? bodyKg = prof != null && prof.Count > 0 ? DashboardData.Num(prof[0]["weight_kg"]) : null;
            yield return Fetch(db, $"strength_sets?select=exercise_name,set_number,reps,weight_kg&user_id=eq.{uid}&date=eq.{date}&order=created_at.asc", r => strength = r);
            yield return Fetch(db, $"exercise_logs?select=exercise_name,sets_completed,reps_completed,duration_min,source,calories_burned_override&user_id=eq.{uid}&date=eq.{date}&order=created_at.asc", r => logs = r);
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
                double mins = DashboardData.Num(l["duration_min"]) ?? 0;
                g.Minutes += mins;
                g.LogKcal += BurnFor(name, mins, DashboardData.Num(l["sets_completed"]) ?? 0, (string)l["reps_completed"], DashboardData.Num(l["calories_burned_override"]), bodyKg);
                g.HadLog = true;
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

                // Calories: from the log rows when there are any; a strength set with no log row falls back to kcal per rep x reps.
                double kcal = g.HadLog ? g.LogKcal : (library != null && library.TryGetValue(g.Name.ToLowerInvariant(), out var lb) ? lb.PerRep * reps : 0);
                string thumb = null;
                if (library != null && library.TryGetValue(g.Name.ToLowerInvariant(), out var lib2))
                    thumb = sex == "female" ? (lib2.ThumbFemale ?? lib2.ThumbMale) : (lib2.ThumbMale ?? lib2.ThumbFemale);

                Items.Add(new Item { Name = g.Name, Detail = detail, Tag = g.Vr ? "Tracked in VR" : null, ImageUrl = thumb, Sets = sets, Reps = reps, Minutes = g.Minutes, Kcal = Math.Round(kcal) });
                TotalSets += sets; TotalReps += reps; TotalMinutes += g.Minutes; TotalKcal += Math.Round(kcal);
            }
            Loading = false; Loaded = true;
        }

        // The web Fitness page's rule: a typed-in figure wins, then duration x MET x bodyweight, then sets x reps x kcal per rep.
        static double BurnFor(string name, double minutes, double sets, string reps, double? overrideKcal, double? bodyKg)
        {
            if (overrideKcal.HasValue) return overrideKcal.Value;
            Lib lib = default; library?.TryGetValue((name ?? "").Trim().ToLowerInvariant(), out lib);
            if (minutes > 0 && bodyKg.HasValue) return Math.Round((lib.Met > 0 ? lib.Met : DefaultMet) * bodyKg.Value * (minutes / 60.0));
            var first = System.Text.RegularExpressions.Regex.Match(reps ?? "", @"\d+");     // the web page reads the first number too
            int repCount = first.Success ? int.Parse(first.Value) : 0;
            return lib.PerRep > 0 && sets > 0 && repCount > 0 ? Math.Round(lib.PerRep * sets * repCount) : 0;
        }

        static IEnumerator LoadLibrary(SupabaseClient db)
        {
            if (library != null) yield break;
            string raw = null, err = null;
            yield return db.Rpc("get_exercise_library?select=name,met_value,kcal_per_rep,thumb_url_male,thumb_url_female,image_url_male,image_url_female", "{}", r => raw = r, e => err = e);
            var map = new Dictionary<string, Lib>();
            if (err == null)
            {
                try
                {
                    foreach (var row in JArray.Parse(raw))
                    {
                        string n = ((string)row["name"] ?? "").Trim().ToLowerInvariant();
                        if (n.Length == 0) continue;
                        string male = (string)row["thumb_url_male"], female = (string)row["thumb_url_female"];
                        if (string.IsNullOrEmpty(male)) male = (string)row["image_url_male"];
                        if (string.IsNullOrEmpty(female)) female = (string)row["image_url_female"];
                        map[n] = new Lib { Met = DashboardData.Num(row["met_value"]) ?? 0, PerRep = DashboardData.Num(row["kcal_per_rep"]) ?? 0,
                                           ThumbMale = string.IsNullOrEmpty(male) ? null : male, ThumbFemale = string.IsNullOrEmpty(female) ? null : female };
                    }
                    library = map;
                }
                catch (Exception) { }
            }
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
