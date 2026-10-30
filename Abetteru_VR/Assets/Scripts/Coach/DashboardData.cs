using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Everything the Home and My World screens show, read from the signed-in user's real A Better U records.
    /// "Not loaded / missing" is kept distinct from zero: nullable values stay null until data actually exists.
    /// Views only read this; all queries and calorie arithmetic live here (mirrors the web app's getFoodPortion).
    public class DashboardData
    {
        public struct InjuryNote { public string Area, Side, Note, Reported; }

        public bool Loading, Loaded;
        public bool HadError;

        public string Name, AvatarUrl;
        public double? CalorieGoal;

        public int MealsToday;
        public double Kcal, Protein, Carbs, Fat;
        public double BurntKcal;                  // estimated calories burnt by today's exercise (VR or logged by hand)
        public string LatestMeal;                 // "Lunch: Big Mac"

        public int WorkoutsThisWeek;
        public string LatestWorkout;              // "Bodyweight Squat, 10 reps"
        public string LatestWorkoutWhen;          // "Today", "Yesterday" or a date

        public double? WeightKg;
        public readonly List<float> WeightHistory = new List<float>();   // oldest to newest, for the sparkline
        public double? WeightDelta;                                      // newest minus oldest in WeightHistory
        public double? SleepMinutes;                                     // last recorded night (sleep_logs)
        public int? SleepScore;                                          // 0 to 100, duration vs 8 h (mirrors the web app)
        public double? MoodRating;                                       // latest mood_logs rating, 1 to 5
        public string MoodEmotion;
        public readonly float[] MoodWeek = new float[7];                 // daily average rating, oldest to today, 0 = none
        public int StreakDays;                                           // consecutive days with a food or workout log
        public readonly List<InjuryNote> Injuries = new List<InjuryNote>();

        static readonly Dictionary<string, string> AreaNames = new Dictionary<string, string>
        {
            { "neck", "neck" }, { "shoulder", "shoulder" }, { "upper_back", "upper back" }, { "lower_back", "lower back" }, { "chest", "chest" },
            { "elbow", "elbow" }, { "wrist_hand", "wrist or hand" }, { "hip", "hip" }, { "glute", "glute" }, { "hamstring", "hamstring" },
            { "quad", "quad" }, { "knee", "knee" }, { "calf_shin", "calf or shin" }, { "ankle_foot", "ankle or foot" }, { "other", "area" },
        };

        public IEnumerator Refresh(SupabaseClient db, Action done)
        {
            if (db == null || !db.SignedIn) { done?.Invoke(); yield break; }
            Loading = true;
            HadError = false;
            string uid = db.UserId;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string weekAgo = DateTime.Now.AddDays(-6).ToString("yyyy-MM-dd");

            // Profile: name and the daily calorie goal.
            JArray rows = null;
            yield return Fetch(db, $"profiles?select=name,avatar,avatar_url,daily_calorie_goal&user_id=eq.{uid}", r => rows = r);
            if (rows != null && rows.Count > 0)
            {
                Name = (string)rows[0]["name"];
                AvatarUrl = (string)rows[0]["avatar"] ?? (string)rows[0]["avatar_url"];
                CalorieGoal = Num(rows[0]["daily_calorie_goal"]);
            }

            // Today's food, joined to the foods table so we can total calories and macros.
            const string foodCols = "name,serving_size_g,energy_kcal_100g,energy_kcal_serving,protein_g_100g,protein_g_serving," +
                                    "carbohydrate_g_100g,carbohydrate_g_serving,fat_g_100g,fat_g_serving";
            rows = null;
            yield return Fetch(db, $"food_log_entries?select=meal_slot,servings,quantity_g,created_at,food:food_id({foodCols}),{MealEmbed}&log_date=eq.{today}&order=created_at.desc", r => rows = r);
            if (rows != null)
            {
                Kcal = Protein = Carbs = Fat = 0;
                MealsToday = 0;
                LatestMeal = null;
                foreach (var entry in rows)
                {
                    if (!EntryMacros(entry, out string itemName, out double k, out double p, out double c, out double f)) continue;
                    MealsToday++;
                    Kcal += k; Protein += p; Carbs += c; Fat += f;

                    if (LatestMeal == null)
                        LatestMeal = $"{Capitalise((string)entry["meal_slot"])}: {itemName}";
                }
            }

            // Today's exercise, so the nutrition card can show calories eaten less calories burnt.
            var todays = new ExerciseData();
            yield return todays.Load(db, DateTime.Now);
            BurntKcal = todays.Loaded && !todays.HadError ? todays.TotalKcal : 0;

            // Weight: latest value plus recent history for the sparkline and change since the oldest shown.
            rows = null;
            yield return Fetch(db, "weight_logs?select=weight_kg,date&order=date.desc&limit=12", r => rows = r);
            WeightHistory.Clear();
            WeightDelta = null;
            if (rows != null && rows.Count > 0)
            {
                WeightKg = Num(rows[0]["weight_kg"]);
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    var w = Num(rows[i]["weight_kg"]);
                    if (w.HasValue) WeightHistory.Add((float)w.Value);
                }
                if (WeightHistory.Count >= 2)
                    WeightDelta = WeightHistory[WeightHistory.Count - 1] - WeightHistory[0];
            }
            else WeightKg = null;

            // Last recorded night's sleep. Score mirrors the web app: duration against an 8-hour goal.
            rows = null;
            yield return Fetch(db, "sleep_logs?select=log_date,duration_min&order=log_date.desc&limit=1", r => rows = r);
            SleepMinutes = rows != null && rows.Count > 0 ? Num(rows[0]["duration_min"]) : null;
            SleepScore = SleepMinutes.HasValue ? (int?)Math.Min(100, (int)Math.Round(SleepMinutes.Value / 480.0 * 100.0)) : null;

            // Mood: latest entry plus daily averages for the last seven days (mood_logs rates 1 to 5).
            rows = null;
            yield return Fetch(db, "mood_logs?select=logged_at,mood_rating,emotion&order=logged_at.desc&limit=60", r => rows = r);
            MoodRating = null; MoodEmotion = null;
            for (int i = 0; i < MoodWeek.Length; i++) MoodWeek[i] = 0f;
            if (rows != null && rows.Count > 0)
            {
                MoodRating = Num(rows[0]["mood_rating"]);
                MoodEmotion = (string)rows[0]["emotion"];
                var sums = new double[7]; var counts = new int[7];
                foreach (var m in rows)
                {
                    string at = (string)m["logged_at"];
                    var rating = Num(m["mood_rating"]);
                    if (string.IsNullOrEmpty(at) || !rating.HasValue || !DateTime.TryParse(at, out var when)) continue;
                    int ago = (DateTime.Now.Date - when.Date).Days;
                    if (ago < 0 || ago > 6) continue;
                    sums[6 - ago] += rating.Value; counts[6 - ago]++;
                }
                for (int i = 0; i < 7; i++) if (counts[i] > 0) MoodWeek[i] = (float)(sums[i] / counts[i]);
            }

            // Workouts in the last seven days, and the newest one.
            rows = null;
            yield return Fetch(db, $"exercise_logs?select=date,exercise_name,sets_completed,reps_completed&date=gte.{weekAgo}&order=created_at.desc", r => rows = r);
            if (rows != null)
            {
                WorkoutsThisWeek = rows.Count;
                if (rows.Count > 0)
                {
                    var w = rows[0];
                    int sets = (int)(Num(w["sets_completed"]) ?? 1);
                    string reps = (string)w["reps_completed"];
                    LatestWorkout = (string)w["exercise_name"] + (string.IsNullOrEmpty(reps) ? "" : sets > 1 ? $", {sets} x {reps}" : $", {reps} reps");
                    LatestWorkoutWhen = DayLabel((string)w["date"]);
                }
                else { LatestWorkout = null; LatestWorkoutWhen = null; }
            }

            // Active injuries or sore areas the user has told us about.
            rows = null;
            yield return Fetch(db, "user_injuries?select=body_area,side,note,reported_at&status=eq.active&order=reported_at.desc", r => rows = r);
            if (rows != null)
            {
                Injuries.Clear();
                foreach (var i in rows)
                {
                    string area = (string)i["body_area"];
                    Injuries.Add(new InjuryNote
                    {
                        Area = AreaNames.TryGetValue(area ?? "", out var nice) ? nice : (area ?? "area"),
                        Side = (string)i["side"],
                        Note = (string)i["note"],
                        Reported = DayLabel(((string)i["reported_at"] ?? "").Length >= 10 ? ((string)i["reported_at"]).Substring(0, 10) : null),
                    });
                }
            }

            // Streak: consecutive days (ending today, or yesterday if today has nothing yet) with any log.
            {
                string since90 = DateTime.Now.AddDays(-90).ToString("yyyy-MM-dd");
                var days = new HashSet<string>();
                rows = null;
                yield return Fetch(db, $"food_log_entries?select=log_date&log_date=gte.{since90}", r => rows = r);
                if (rows != null) foreach (var e in rows) { string d = (string)e["log_date"]; if (!string.IsNullOrEmpty(d) && d.Length >= 10) days.Add(d.Substring(0, 10)); }
                rows = null;
                yield return Fetch(db, $"exercise_logs?select=date&date=gte.{since90}", r => rows = r);
                if (rows != null) foreach (var e in rows) { string d = (string)e["date"]; if (!string.IsNullOrEmpty(d) && d.Length >= 10) days.Add(d.Substring(0, 10)); }

                StreakDays = 0;
                var cursor = DateTime.Now.Date;
                if (!days.Contains(cursor.ToString("yyyy-MM-dd"))) cursor = cursor.AddDays(-1);
                while (days.Contains(cursor.ToString("yyyy-MM-dd"))) { StreakDays++; cursor = cursor.AddDays(-1); }
            }

            Loading = false;
            Loaded = true;
            done?.Invoke();
        }

        IEnumerator Fetch(SupabaseClient db, string path, Action<JArray> ok)
        {
            string raw = null, err = null;
            yield return db.Get(path, r => raw = r, e => err = e);
            if (err != null)
            {
                Debug.LogWarning($"[Dashboard] {path.Split('?')[0]} failed: {err}");
                HadError = true;
                ok(null);
                yield break;
            }
            JArray parsed = null;
            try { parsed = JArray.Parse(raw); } catch (Exception) { /* leave null; treated as an error below */ }
            if (parsed == null) HadError = true;
            ok(parsed);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

        // A diary row is either a single food (grams) or one of the user's saved meals (servings of the recipe).
        // Both screens read rows selected with this embed and total them with EntryMacros, so they always agree.
        const string MealNutrientCols = "serving_size_g,energy_kcal_100g,energy_kcal_serving,protein_g_100g,protein_g_serving," +
                                        "carbohydrate_g_100g,carbohydrate_g_serving,fat_g_100g,fat_g_serving";
        public const string MealEmbed = "item_type,meal:meal_id(name,servings,meal_ingredients(quantity_g,food:food_id(" + MealNutrientCols + ")))";

        public static bool EntryMacros(JToken entry, out string name, out double kcal, out double protein, out double carbs, out double fat)
        {
            name = null; kcal = protein = carbs = fat = 0;

            var food = entry["food"];
            if (food != null && food.Type != JTokenType.Null)
            {
                double servingG = Num(food["serving_size_g"]) ?? 0;
                double servings = Num(entry["servings"]) ?? 0;
                double qty = Num(entry["quantity_g"]) ?? 0;
                double grams = qty > 0 ? qty : (servings > 0 ? servings : 1) * (servingG > 0 ? servingG : 100);
                kcal = Portion(food, "energy_kcal", grams);
                protein = Portion(food, "protein_g", grams);
                carbs = Portion(food, "carbohydrate_g", grams);
                fat = Portion(food, "fat_g", grams);
                name = (string)food["name"];
                return true;
            }

            // Saved meal: add up its ingredients, then scale by servings eaten / servings the recipe makes
            // (the web app's computeEntryNutrition does the same).
            var meal = entry["meal"];
            if (meal != null && meal.Type != JTokenType.Null && meal["meal_ingredients"] is JArray ingredients)
            {
                double eaten = Num(entry["servings"]) ?? 1;
                double recipe = Math.Max(1, Num(meal["servings"]) ?? 1);
                double scale = eaten / recipe;
                foreach (var ing in ingredients)
                {
                    var f = ing["food"];
                    if (f == null || f.Type == JTokenType.Null) continue;
                    double grams = Num(ing["quantity_g"]) ?? 0;
                    kcal += Portion(f, "energy_kcal", grams) * scale;
                    protein += Portion(f, "protein_g", grams) * scale;
                    carbs += Portion(f, "carbohydrate_g", grams) * scale;
                    fat += Portion(f, "fat_g", grams) * scale;
                }
                name = (string)meal["name"];
                return true;
            }
            return false;
        }

        // Same rule as the web app's getFoodPortion: use per-100 g values, or derive them from per-serving values
        // using the serving weight (100 g when the food has none).
        public static double Portion(JToken food, string nutrient, double grams)
        {
            double per100 = Num(food[nutrient + "_100g"]) ?? 0;
            double perServing = Num(food[nutrient + "_serving"]) ?? -1;
            double servingG = Num(food["serving_size_g"]) ?? 0;
            double basis = servingG > 0 ? servingG : (Num(food["energy_kcal_serving"]) != null ? 100 : 0);

            double effective100 = per100 > 0 ? per100 : (perServing >= 0 && basis > 0 ? perServing / basis * 100 : 0);
            return effective100 * grams / 100.0;
        }

        public static double? Num(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (double)t;
            return double.TryParse((string)t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpper(s[0]) + s.Substring(1);

        static string DayLabel(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate) || !DateTime.TryParse(isoDate, out var d)) return null;
            int days = (DateTime.Now.Date - d.Date).Days;
            return days <= 0 ? "Today" : days == 1 ? "Yesterday" : d.ToString("d MMM");
        }
    }
}
