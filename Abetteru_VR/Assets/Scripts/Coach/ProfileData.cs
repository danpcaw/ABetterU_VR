using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// The signed-in user's profile page, read from their real profile row, their connections and their own recent feed posts.
    /// Nullable or empty means "not set", never zero. Views only read this.
    public class ProfileData
    {
        public struct PostLine { public string Body, When, Visibility, ImageUrl; }

        public bool Loading, Loaded, HadError;
        public string Name, Username, Bio, GoalType, Experience;
        public DateTime? MemberSince;
        public double? WeightKg, GoalWeightKg, HeightCm;
        public int? CalorieGoal, DaysPerWeek;
        public readonly List<string> Goals = new List<string>();
        public int Followers, Following, PostCount;
        public readonly List<PostLine> Posts = new List<PostLine>();

        public IEnumerator Load(SupabaseClient db)
        {
            Loading = true; Loaded = false; HadError = false;
            Posts.Clear(); Goals.Clear();
            if (db == null || !db.SignedIn) { HadError = true; Loading = false; Loaded = true; yield break; }
            string uid = db.UserId;

            JArray rows = null;
            yield return Fetch(db, $"profiles?select=name,username,bio,goals,weight_kg,height_cm,goal_weight_kg,goal_type,daily_calorie_goal,workout_days_per_week,fitness_experience,created_at&user_id=eq.{uid}", r => rows = r);
            if (rows == null || rows.Count == 0) { HadError = true; Loading = false; Loaded = true; yield break; }
            var p = rows[0];
            Name = (string)p["name"]; Username = (string)p["username"]; Bio = (string)p["bio"];
            GoalType = (string)p["goal_type"]; Experience = (string)p["fitness_experience"];
            WeightKg = DashboardData.Num(p["weight_kg"]); GoalWeightKg = DashboardData.Num(p["goal_weight_kg"]); HeightCm = DashboardData.Num(p["height_cm"]);
            var cal = DashboardData.Num(p["daily_calorie_goal"]); CalorieGoal = cal.HasValue ? (int?)Math.Round(cal.Value) : null;
            var days = DashboardData.Num(p["workout_days_per_week"]); DaysPerWeek = days.HasValue ? (int?)Math.Round(days.Value) : null;
            if (DateTime.TryParse((string)p["created_at"], out var made)) MemberSince = made;
            if (p["goals"] is JArray goals) foreach (var g in goals) { string s = (string)g; if (!string.IsNullOrWhiteSpace(s)) Goals.Add(s.Replace('_', ' ')); }

            // Connections and posts are extras: if they fail the profile still shows, with zeros left out of the picture.
            rows = null;
            yield return Fetch(db, $"follow?select=id&followee_id=eq.{uid}", r => rows = r);
            Followers = rows?.Count ?? 0;
            rows = null;
            yield return Fetch(db, $"follow?select=id&follower_id=eq.{uid}", r => rows = r);
            Following = rows?.Count ?? 0;

            rows = null;
            yield return Fetch(db, $"post?select=id,body,visibility,created_at&author_id=eq.{uid}&deleted_at=is.null&order=created_at.desc&limit=50", r => rows = r);
            if (rows != null)
            {
                PostCount = rows.Count;
                int shown = Math.Min(3, rows.Count);
                var ids = new List<string>();
                for (int i = 0; i < shown; i++) ids.Add((string)rows[i]["id"]);

                // The first picture of each shown post, if it has one.
                var firstImage = new Dictionary<string, string>();
                JArray media = null;
                if (ids.Count > 0)
                    yield return Fetch(db, $"post_media?select=post_id,media_url,sort_order&post_id=in.({string.Join(",", ids)})&order=sort_order.asc", r => media = r);
                if (media != null)
                    foreach (var m in media) { string pid = (string)m["post_id"]; if (pid != null && !firstImage.ContainsKey(pid)) firstImage[pid] = (string)m["media_url"]; }

                for (int i = 0; i < shown; i++)
                {
                    string when = DateTime.TryParse((string)rows[i]["created_at"], out var d) ? d.ToLocalTime().ToString("d MMM") : "";
                    firstImage.TryGetValue((string)rows[i]["id"], out var image);
                    Posts.Add(new PostLine { Body = (string)rows[i]["body"] ?? "(picture)", When = when, Visibility = (string)rows[i]["visibility"], ImageUrl = image });
                }
            }
            Loading = false; Loaded = true;
        }

        static IEnumerator Fetch(SupabaseClient db, string path, Action<JArray> ok)
        {
            string raw = null, err = null;
            yield return db.Get(path, r => raw = r, e => err = e);
            if (err != null) { Debug.LogWarning($"[Profile] {path.Split('?')[0]} failed: {err}"); ok(null); yield break; }
            JArray parsed = null;
            try { parsed = JArray.Parse(raw); } catch (Exception) { }
            ok(parsed);
        }
    }
}
