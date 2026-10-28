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
        const int MaxSteps = 10;          // a sentence with several foods needs a search and a log for each

        readonly ModelApiConfig config;
        readonly SupabaseClient supabase;

        // food_id -> grams per serving, remembered from search results so log_food can also write quantity_g
        // (the web app computes calories from grams; a servings-only row shows no calories until edited).
        readonly Dictionary<string, double> servingGrams = new Dictionary<string, double>();

        // Every food the search has returned, so log_food can only log something the database actually showed us.
        readonly Dictionary<string, JObject> foodsSeen = new Dictionary<string, JObject>();

        /// Raised after a tool call the backend confirmed (ok:true), with the tool's name and its result.
        public event Action<string, JObject> ToolSucceeded;

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
          {""type"":""function"",""name"":""log_set"",
           ""description"":""Record a finished exercise set in the user's workout history. Use only rep counts the user states or the message context provides; never guess."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""exercise_name"":{""type"":""string"",""description"":""e.g. Bodyweight Squat""},
             ""reps"":{""type"":""integer""},
             ""sets"":{""type"":""integer"",""description"":""Default 1""},
             ""weight_kg"":{""type"":""number"",""description"":""Omit for bodyweight""}},
            ""required"":[""exercise_name"",""reps""]}},
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
            "If the user lists several foods, deal with every item: search for each, then call log_food once per item with the right number of servings " +
            "(for example 3 rations or 2 slices means servings 3 or 2). Never say an item is logged until log_food returned ok:true for that item, " +
            "and clearly name any item you could not find or log. " +
            "Only say something is logged after log_food returns ok:true. The food search matches the food's own name and handles brand words itself, " +
            "so if it returns nothing, try once more with fewer, simpler words before telling the user it was not found. " +
            "Use the earlier conversation to understand short replies like yes or no. Do not start a workout unless the user asked for one. " +
            "Never invent calories or numbers; use only what tools return. " +
            "If asked what you can do, say in one or two short sentences that you can log food from the A Better U food database, " +
            "save an injury or sore area, build a workout that works around it, and record finished sets. " +
            "If the user says something hurts or is sore, call report_injury first (record their words, never diagnose), " +
            "then if they want a workout call build_workout with focus legs unless they ask for upper body, core or full body, " +
            "and pick exactly 3 exercises from its candidates, giving sets and reps. For legs, prefer gentle glute, hip and bodyweight squat options. " +
            "When the user says they finished a set, call log_set with the exercise and the reps they stated, then say what was recorded. " +
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

                        // Tell the UI about every tool that really succeeded, so "Saved" is only ever shown after the backend said ok.
                        JObject parsedResult = null;
                        try { parsedResult = JObject.Parse(result); } catch (Exception) { /* not JSON: nothing to announce */ }
                        if (parsedResult != null && (bool?)parsedResult["ok"] == true) ToolSucceeded?.Invoke(name, parsedResult);
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
                case "log_set": yield return LogSet(args, done); break;
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
                double? grams = EstimateServingGrams(f);
                if (grams.HasValue) servingGrams[(string)f["id"]] = grams.Value;
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

        /// Mirrors estimateServingGrams in the web app (src/lib/foods.ts): the serving weight if the food has one,
        /// else grams derived from kcal per serving vs per 100 g, else null (the web app then logs 100 g).
        static double? EstimateServingGrams(JToken food)
        {
            double servingG = food["serving_size_g"]?.Type == JTokenType.Null ? 0 : (double?)food["serving_size_g"] ?? 0;
            if (servingG > 0) return servingG;

            double kcalServing = food["energy_kcal_serving"]?.Type == JTokenType.Null ? 0 : (double?)food["energy_kcal_serving"] ?? 0;
            double kcal100 = food["energy_kcal_100g"]?.Type == JTokenType.Null ? 0 : (double?)food["energy_kcal_100g"] ?? 0;
            if (kcalServing > 0 && kcal100 > 0) return Math.Round(kcalServing / kcal100 * 100);
            return null;
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

            // The web diary computes calories only from quantity_g (computeEntryNutrition), so always write grams,
            // exactly as LogFoodSheet does: estimated serving weight, else 100 g per unit.
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

        IEnumerator LogSet(JObject args, Action<string> done)
        {
            string exercise = ((string)args["exercise_name"] ?? "").Trim();
            int reps = args["reps"] != null ? (int)args["reps"] : 0;
            int sets = args["sets"] != null ? (int)args["sets"] : 1;
            if (exercise.Length == 0) { done(Fail("exercise_name is required")); yield break; }
            if (reps < 1 || reps > 200) { done(Fail("reps must be between 1 and 200")); yield break; }
            if (sets < 1 || sets > 20) sets = 1;

            // source='vr' marks these rows as created by the VR app (exercise_logs has the column; strength_sets does not).
            var row = new JObject
            {
                ["user_id"] = supabase.UserId,
                ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["exercise_name"] = exercise,
                ["sets_completed"] = sets,
                ["reps_completed"] = reps.ToString(),
                ["notes"] = "Logged from A Better U VR",
                ["source"] = "vr",
            };
            if (args["weight_kg"] != null && (double)args["weight_kg"] > 0) row["weight_kg"] = (double)args["weight_kg"];

            string today = row["date"].ToString();
            string raw = null, err = null;

            // 1) strength_sets is where the web Fitness page lists history (exercise_logs is collapsed "older history").
            //    Link the set to the library exercise when the name matches, and number sets after any already logged today.
            string libRaw = null;
            yield return supabase.Rpc($"get_exercise_library?select=id,name&name=ilike.{UnityWebRequest.EscapeURL(exercise)}&limit=1", "{}", r => libRaw = r, e => err = e);
            string libraryId = null;
            if (err == null && libRaw != null) { var libRows = JArray.Parse(libRaw); if (libRows.Count > 0) libraryId = (string)libRows[0]["id"]; }
            err = null;

            string existingRaw = null;
            yield return supabase.Get($"strength_sets?select=id&date=eq.{today}&exercise_name=eq.{UnityWebRequest.EscapeURL(exercise)}", r => existingRaw = r, e => err = e);
            int alreadyLogged = (err == null && existingRaw != null) ? JArray.Parse(existingRaw).Count : 0;
            err = null;

            double weight = row["weight_kg"] != null ? (double)row["weight_kg"] : 0.0;   // NOT NULL in strength_sets; 0 = bodyweight
            var setRows = new JArray();
            for (int n = 1; n <= sets; n++)
                setRows.Add(new JObject
                {
                    ["user_id"] = supabase.UserId, ["exercise_name"] = exercise, ["date"] = today,
                    ["set_number"] = alreadyLogged + n, ["reps"] = reps, ["weight_kg"] = weight, ["is_warmup"] = false,
                    ["exercise_library_id"] = libraryId,
                });
            yield return supabase.Insert("strength_sets", setRows.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            // 2) exercise_logs row keeps the source='vr' marker (strength_sets has no source column) and feeds the dashboard count.
            yield return supabase.Insert("exercise_logs", row.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            done(new JObject { ["ok"] = true, ["logged_exercise"] = exercise, ["sets"] = sets, ["reps"] = reps }.ToString(Newtonsoft.Json.Formatting.None));
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
