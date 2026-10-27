using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Muse Spark as a tool-calling agent on the user's real A Better U account.
    /// One turn = model call -> (function_call -> run tool -> send result back)* -> spoken reply.
    /// Tools: search_foods (read) and log_food (write). The coach may only say "logged" after log_food returns ok.
    public class CoachAgent
    {
        const int MaxSteps = 5;

        readonly ModelApiConfig config;
        readonly SupabaseClient supabase;

        // food_id -> grams per serving, remembered from search results so log_food can also write quantity_g
        // (the web app computes calories from grams; a servings-only row shows no calories until edited).
        readonly Dictionary<string, double> servingGrams = new Dictionary<string, double>();

        // Every food the search has returned, so log_food can only log something the database actually showed us.
        readonly Dictionary<string, JObject> foodsSeen = new Dictionary<string, JObject>();

        public CoachAgent(ModelApiConfig config, SupabaseClient supabase)
        {
            this.config = config;
            this.supabase = supabase;
        }

        static readonly JArray Tools = JArray.Parse(@"[
          {""type"":""function"",""name"":""search_foods"",
           ""description"":""Search the A Better U food database. Returns up to 5 candidates (id, name, brand, serving, kcal). Call this before log_food."",
           ""parameters"":{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Plain food name, e.g. scrambled eggs""}},""required"":[""query""]}},
          {""type"":""function"",""name"":""log_food"",
           ""description"":""Log one food to the user's food diary. food_id must come from search_foods."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""food_id"":{""type"":""string""},
             ""meal"":{""type"":""string"",""enum"":[""breakfast"",""lunch"",""dinner"",""snack""]},
             ""servings"":{""type"":""number"",""description"":""Number of servings, default 1""}},
            ""required"":[""food_id"",""meal""]}},
          {""type"":""function"",""name"":""report_injury"",
           ""description"":""Save an injury or sore area the user says they have. Record only what they say; never diagnose."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""body_area"":{""type"":""string"",""enum"":[""neck"",""shoulder"",""upper_back"",""lower_back"",""chest"",""elbow"",""wrist_hand"",""hip"",""glute"",""hamstring"",""quad"",""knee"",""calf_shin"",""ankle_foot"",""other""]},
             ""side"":{""type"":""string"",""enum"":[""left"",""right"",""both""]},
             ""note"":{""type"":""string"",""description"":""The user's own words, under 200 characters""}},
            ""required"":[""body_area""]}},
          {""type"":""function"",""name"":""build_workout"",
           ""description"":""Fetch exercise candidates from the A Better U library, already filtered to avoid the user's active injuries. Then choose 3 and give sets and reps."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""focus"":{""type"":""string"",""enum"":[""legs"",""core"",""upper"",""full""],""description"":""Default legs""}}}}
        ]");

        const string AgentRules =
            " You can act on the user's A Better U account with tools. To log food: call search_foods, pick the closest " +
            "match, then call log_food. Choose the result whose name contains the most of the words the user said, " +
            "branded or not (the user's speech may drop a brand word). Never pick an item whose name lacks a key word the user said; " +
            "if no result clearly matches, say so and ask. Do not search again once a result already matches. " +
            "After logging, say the exact logged_name, its brand and kcal_per_serving aloud. " +
            "Only say something is logged after log_food returns ok:true. The food search matches the food's own name and handles brand words itself, " +
            "so if it returns nothing, try once more with fewer, simpler words before telling the user it was not found. " +
            "Use the earlier conversation to understand short replies like yes or no. Do not start a workout unless the user asked for one. " +
            "Never invent calories or numbers; use only what tools return. " +
            "If the user says something hurts or is sore, call report_injury first (record their words, never diagnose), " +
            "then if they want a workout call build_workout with focus legs unless they ask for upper body, core or full body, " +
            "and pick exactly 3 exercises from its candidates, giving sets and reps. For legs, prefer gentle glute, hip and bodyweight squat options. " +
            "Say the exercise names aloud in one short sentence, and add that you are not a medical professional only if they ask about treatment.";

        /// Runs one user turn. onProgress gets short status text (tool names) for the board.
        public IEnumerator Run(string userText, Action<string> onProgress, Action<string> onReply, Action<string> onError)
        {
            // Earlier turns (text only) come first so "yes please" has something to refer to.
            var input = new JArray();
            foreach (var h in history) input.Add(h);
            var userMsg = new JObject
            {
                ["role"] = "user",
                ["content"] = new JArray { new JObject { ["type"] = "input_text", ["text"] = userText } },
            };
            input.Add(userMsg);

            var now = DateTime.Now;
            string instructions = config.coachPrompt + AgentRules +
                $" The user's name is {supabase.DisplayName ?? "unknown"}. Today is {now:yyyy-MM-dd}, local time {now:HH:mm}.";

            for (int step = 0; step < MaxSteps; step++)
            {
                var body = new JObject
                {
                    ["model"] = config.model,
                    ["instructions"] = instructions,
                    ["input"] = input,
                    ["tools"] = Tools,
                    ["reasoning"] = new JObject { ["effort"] = config.replyEffort },
                    ["max_output_tokens"] = config.replyMaxTokens,
                };

                string raw = null, err = null;
                yield return Post(body.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
                if (err != null) { onError(err); yield break; }

                JArray output = JObject.Parse(raw)["output"] as JArray;
                if (output == null) { onError("No output in model response."); yield break; }

                string text = null;
                bool calledTool = false;
                foreach (var item in output)
                {
                    string type = (string)item["type"];
                    if (type == "message")
                    {
                        var t = (string)item["content"]?[0]?["text"];
                        if (!string.IsNullOrWhiteSpace(t)) text = t.Trim();
                    }
                    else if (type == "function_call")
                    {
                        calledTool = true;
                        string callId = (string)item["call_id"], name = (string)item["name"], args = (string)item["arguments"];
                        onProgress?.Invoke($"[{name}] {args}");
                        input.Add(new JObject { ["type"] = "function_call", ["call_id"] = callId, ["name"] = name, ["arguments"] = args });

                        string result = null;
                        yield return RunTool(name, args, r => result = r);
                        Debug.Log($"[Agent] {name}({args}) -> {result}");
                        input.Add(new JObject { ["type"] = "function_call_output", ["call_id"] = callId, ["output"] = result });
                    }
                }

                if (!calledTool)
                {
                    if (string.IsNullOrEmpty(text)) { onError("Model returned no reply (see log)."); yield break; }
                    Remember(userMsg, text);
                    onReply(text);
                    yield break;
                }
            }
            onError("Agent used too many steps.");
        }

        // Keeps the last few user/assistant text turns. Tool calls are not kept: the reply already says what happened.
        readonly List<JObject> history = new List<JObject>();
        const int MaxHistoryMessages = 8;

        void Remember(JObject userMsg, string reply)
        {
            history.Add(userMsg);
            history.Add(new JObject
            {
                ["role"] = "assistant",
                ["content"] = new JArray { new JObject { ["type"] = "output_text", ["text"] = reply } },
            });
            while (history.Count > MaxHistoryMessages) history.RemoveAt(0);
        }

        IEnumerator RunTool(string name, string argsJson, Action<string> done)
        {
            JObject args = null;
            try { args = JObject.Parse(argsJson); }
            catch (Exception) { /* reported below; C# forbids yield inside catch */ }
            if (args == null) { done(Fail("arguments were not valid JSON")); yield break; }

            switch (name)
            {
                case "search_foods": yield return SearchFoods((string)args["query"], done); break;
                case "log_food": yield return LogFood(args, done); break;
                case "report_injury": yield return ReportInjury(args, done); break;
                case "build_workout": yield return BuildWorkout(args, done); break;
                default: done(Fail("unknown tool " + name)); break;
            }
        }

        IEnumerator SearchFoods(string query, Action<string> done)
        {
            if (string.IsNullOrWhiteSpace(query)) { done(Fail("query is required")); yield break; }

            // search_foods matches the food NAME only, so a spoken brand ("KFC") or a synonym ("barbecue" for BBQ)
            // returns nothing. Normalise, then retry with up to two leading words dropped ("kfc bbq mini wrap" -> "mini wrap").
            var words = NormaliseQuery(query);
            string raw = "[]", err = null, usedQuery = query;
            for (int skip = 0; skip < words.Count && skip <= 2; skip++)
            {
                usedQuery = string.Join(" ", words.GetRange(skip, words.Count - skip));
                var body = new JObject { ["q"] = usedQuery, ["lim"] = 5, ["uid"] = supabase.UserId }.ToString(Newtonsoft.Json.Formatting.None);
                yield return supabase.Rpc("search_foods", body, r => raw = r, e => err = e);
                if (err != null) { done(Fail(err)); yield break; }
                if (JArray.Parse(raw).Count > 0) break;
            }

            var rows = new JArray();
            foreach (var f in JArray.Parse(raw))
            {
                if (f["serving_size_g"]?.Type != JTokenType.Null && f["serving_size_g"] != null)
                    servingGrams[(string)f["id"]] = (double)f["serving_size_g"];
                var summary = new JObject
                {
                    ["food_id"] = f["id"],
                    ["name"] = f["name"],
                    ["brand"] = f["brand"],
                    ["serving"] = f["serving_name"],
                    ["serving_g"] = f["serving_size_g"],
                    ["kcal_per_serving"] = f["energy_kcal_serving"],
                };
                foodsSeen[(string)f["id"]] = summary;
                rows.Add(summary);
            }
            done(new JObject { ["ok"] = true, ["searched_as"] = usedQuery, ["results"] = rows }.ToString(Newtonsoft.Json.Formatting.None));
        }

        static List<string> NormaliseQuery(string query)
        {
            var cleaned = new StringBuilder();
            foreach (char c in query.ToLowerInvariant()) cleaned.Append(char.IsLetterOrDigit(c) ? c : ' ');
            var words = new List<string>();
            foreach (var w in cleaned.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                words.Add(w == "barbecue" || w == "barbeque" ? "bbq" : w);
            return words;
        }

        IEnumerator LogFood(JObject args, Action<string> done)
        {
            string foodId = (string)args["food_id"];
            if (string.IsNullOrEmpty(foodId)) { done(Fail("food_id is required; call search_foods first")); yield break; }

            if (!foodsSeen.TryGetValue(foodId, out JObject chosen))
            {
                done(Fail("food_id was not returned by search_foods; call search_foods and use an id from its results"));
                yield break;
            }

            string meal = (string)args["meal"];
            if (meal != "breakfast" && meal != "lunch" && meal != "dinner" && meal != "snack")
                meal = DefaultMealSlot(DateTime.Now);

            double servings = args["servings"] != null ? (double)args["servings"] : 1.0;
            if (servings <= 0 || servings > 20) servings = 1.0;

            var row = new JObject
            {
                ["user_id"] = supabase.UserId,
                ["log_date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["meal_slot"] = meal,
                ["item_type"] = "food",
                ["food_id"] = foodId,
                ["servings"] = servings,
                ["position"] = 0,
                ["source"] = "ai_assistant",
            };

            // Match what the web app itself writes: grams. With no serving weight on the food (e.g. KFC BBQ Mini Wrap),
            // the web app stores 100 g per unit, so do the same and the diary shows calories without an Edit + Save.
            double gramsPerUnit = servingGrams.TryGetValue(foodId, out double g) ? g : 100.0;
            row["quantity_g"] = Math.Round(gramsPerUnit * servings, 1);

            // Order within the meal: after whatever is already there.
            string raw = null, err = null, sameMeal = null;
            yield return supabase.Get($"food_log_entries?select=id&log_date=eq.{row["log_date"]}&meal_slot=eq.{meal}", r => sameMeal = r, e => err = e);
            if (err == null && sameMeal != null) row["position"] = JArray.Parse(sameMeal).Count;
            err = null;

            yield return supabase.Insert("food_log_entries", row.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            // Echo exactly what was stored so the coach can say it aloud and a wrong match is audible.
            done(new JObject
            {
                ["ok"] = true,
                ["logged_name"] = chosen["name"],
                ["logged_brand"] = chosen["brand"],
                ["kcal_per_serving"] = chosen["kcal_per_serving"],
                ["meal"] = meal,
                ["servings"] = servings,
            }.ToString(Newtonsoft.Json.Formatting.None));
        }

        IEnumerator ReportInjury(JObject args, Action<string> done)
        {
            string area = (string)args["body_area"];
            string side = (string)args["side"];
            string note = (string)args["note"];
            if (string.IsNullOrEmpty(area)) { done(Fail("body_area is required")); yield break; }
            if (note != null && note.Length > 280) note = note.Substring(0, 280);

            // Don't stack duplicates: one active row per area+side is enough.
            string existing = null, err = null;
            string sideFilter = side != null ? $"&side=eq.{side}" : "&side=is.null";
            yield return supabase.Get($"user_injuries?select=id&status=eq.active&body_area=eq.{area}{sideFilter}", r => existing = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }
            if (JArray.Parse(existing).Count > 0)
            {
                done(new JObject { ["ok"] = true, ["already_recorded"] = true, ["body_area"] = area }.ToString(Newtonsoft.Json.Formatting.None));
                yield break;
            }

            var row = new JObject { ["user_id"] = supabase.UserId, ["body_area"] = area, ["source"] = "ai_assistant" };
            if (side != null) row["side"] = side;
            if (!string.IsNullOrWhiteSpace(note)) row["note"] = note;

            string raw = null;
            yield return supabase.Insert("user_injuries", row.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }
            done(new JObject { ["ok"] = true, ["recorded"] = true, ["body_area"] = area, ["side"] = side }.ToString(Newtonsoft.Json.Formatting.None));
        }

        // Exercise-name fragments to hide per injured area. Deterministic on purpose: the safety filter
        // is code, not model judgement. It only FILTERS the existing library; it never advises treatment.
        static readonly Dictionary<string, string[]> AvoidWords = new Dictionary<string, string[]>
        {
            ["knee"] = new[] { "jump", "lunge", "plyo", "hop", "burpee", "pistol", "sissy", "extension", "leg press", "hack", "split squat", "step", "skater", "full squat", "deep",
                                    "bound", "skip", "mountain", "swing", "sprint", "drill", "acceleration", "butt kick", "claw" },
            ["quad"] = new[] { "squat", "lunge", "extension", "leg press", "step", "jump" },
            ["hamstring"] = new[] { "deadlift", "curl", "good morning", "lunge", "jump" },
            ["glute"] = new[] { "bridge", "thrust", "lunge", "squat", "jump" },
            ["hip"] = new[] { "lunge", "split", "sumo", "abduct", "adduct", "step", "jump" },
            ["calf_shin"] = new[] { "jump", "run", "calf", "hop", "skip", "burpee" },
            ["ankle_foot"] = new[] { "jump", "hop", "plyo", "run", "skip", "burpee", "calf", "bound", "mountain", "sprint", "drill" },
            ["lower_back"] = new[] { "deadlift", "good morning", "bent over", "row", "twist", "sit-up", "situp", "hyperextension", "clean", "snatch" },
            ["upper_back"] = new[] { "row", "pull", "shrug", "clean", "snatch" },
            ["shoulder"] = new[] { "overhead", "shoulder press", "military", "snatch", "upright", "dip", "push press", "behind", "arnold" },
            ["elbow"] = new[] { "curl", "extension", "skull", "dip", "pull-up", "chin" },
            ["wrist_hand"] = new[] { "push-up", "pushup", "plank", "press", "curl", "handstand" },
            ["chest"] = new[] { "bench", "press", "fly", "push-up", "pushup", "dip" },
            ["neck"] = new[] { "shrug", "neck", "overhead" },
        };

        IEnumerator BuildWorkout(JObject args, Action<string> done)
        {
            string focus = ((string)args["focus"] ?? "legs").ToLowerInvariant();
            string category = focus switch
            {
                "core" => "category=eq.Core",
                "upper" => "category=in.(Chest,Back,Shoulders,Arms)",
                "full" => "category=neq.Stretching",
                _ => "category=eq.Legs",
            };

            // 1) What does the user say hurts right now? (RLS: only their own rows.)
            string injuriesRaw = null, err = null;
            yield return supabase.Get("user_injuries?select=body_area,side&status=eq.active", r => injuriesRaw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }
            var areas = new List<string>();
            foreach (var i in JArray.Parse(injuriesRaw)) areas.Add((string)i["body_area"]);

            // 2) Pull only the columns we need, bodyweight/dumbbell, beginner first when anything hurts.
            string difficulty = areas.Count > 0 ? "difficulty=eq.beginner" : "difficulty=in.(beginner,intermediate)";
            string path = "get_exercise_library?select=id,name,category,equipment,difficulty,primary_muscles,default_sets,default_reps" +
                          $"&{category}&equipment=in.(bodyweight,dumbbell)&{difficulty}&limit=150";
            string libRaw = null;
            yield return supabase.Rpc(path, "{}", r => libRaw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            // 3) Drop anything whose name matches an avoid-word for an injured area, shuffle, keep 20.
            var kept = new List<JToken>();
            foreach (var ex in JArray.Parse(libRaw))
            {
                string name = ((string)ex["name"] ?? "").ToLowerInvariant();
                bool blocked = false;
                foreach (var area in areas)
                    if (AvoidWords.TryGetValue(area, out var words))
                        foreach (var w in words) if (name.Contains(w)) { blocked = true; break; }
                if (!blocked) kept.Add(ex);
            }
            for (int i = kept.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (kept[i], kept[j]) = (kept[j], kept[i]);
            }
            var candidates = new JArray();
            foreach (var ex in kept.GetRange(0, Math.Min(20, kept.Count)))
                candidates.Add(new JObject
                {
                    ["id"] = ex["id"], ["name"] = ex["name"], ["equipment"] = ex["equipment"],
                    ["muscles"] = ex["primary_muscles"], ["sets"] = ex["default_sets"], ["reps"] = ex["default_reps"],
                });

            done(new JObject
            {
                ["ok"] = true,
                ["avoiding_areas"] = new JArray(areas),
                ["note"] = "Candidates are pre-filtered from the A Better U exercise library. Choose 3. Prefer low-load, controlled options.",
                ["candidates"] = candidates,
            }.ToString(Newtonsoft.Json.Formatting.None));
        }

        static string Fail(string why) => new JObject { ["ok"] = false, ["error"] = why }.ToString(Newtonsoft.Json.Formatting.None);

        /// Used when the model gives no valid meal. Local clock decides.
        static string DefaultMealSlot(DateTime localNow)
        {
            int minutes = localNow.Hour * 60 + localNow.Minute;

if (minutes >= 6 * 60 && minutes <= 10 * 60 + 30) return "breakfast";
if (minutes >= 12 * 60 && minutes <= 14 * 60) return "lunch";
if (minutes >= 18 * 60 && minutes <= 20 * 60) return "dinner";
return "snack";
        }

        IEnumerator Post(string json, Action<string> ok, Action<string> fail)
        {
            using var req = new UnityWebRequest(config.endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) fail($"{req.responseCode} {req.error}: {req.downloadHandler.text}");
            else ok(req.downloadHandler.text);
        }
    }
}
