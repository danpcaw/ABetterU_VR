using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        public Func<bool> HasTrackedSquatWorkout;
        public Func<int, int, int, string> StartSquatSession;
        public Func<string, int, int, string> ControlSquatSession;
        public Func<JObject, Action<string>, IEnumerator> DraftPost;
        public Func<string, Action<string>, IEnumerator> PublishPost;
        public Func<string, Action<string>, IEnumerator> ShowExerciseVideo;
        public Func<string> CloseExerciseVideo;

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
             ""servings"":{""type"":""number"",""description"":""Number of servings, default 1""},
             ""grams"":{""type"":""number"",""description"":""Weight in grams when the user gave a weight (for example 100 grams of chips). Pass this instead of servings.""}},
            ""required"":[""food_id"",""meal""]}},
          {""type"":""function"",""name"":""find_my_meals"",
           ""description"":""List the user's own saved meals and combos (for example their usual breakfast), with kcal per serving. Call this when they refer to a meal they saved or 'my usual'. Returns meal_id values for log_meal."",
           ""parameters"":{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Optional words from the meal name; omit to list all""}}}},
          {""type"":""function"",""name"":""log_meal"",
           ""description"":""Log one of the user's saved meals to their food diary. meal_id must come from find_my_meals."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""meal_id"":{""type"":""string""},
             ""meal"":{""type"":""string"",""enum"":[""breakfast"",""lunch"",""dinner"",""snack""]},
             ""servings"":{""type"":""number"",""description"":""Servings of the saved meal, default 1""}},
            ""required"":[""meal_id"",""meal""]}},
          {""type"":""function"",""name"":""offer_choices"",
           ""description"":""Show the user a short list of foods on screen to tap, when search_foods returned several different foods and they did not name a brand, or the best match is unclear. food_ids must come from search_foods (2 to 10). Nothing is logged until the user taps one."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""food_ids"":{""type"":""array"",""items"":{""type"":""string""},""description"":""2 to 10 ids from search_foods. Omit when show_all is true.""},
             ""show_all"":{""type"":""boolean"",""description"":""True to show everything the last search found, in pages of 10 (use when the user wants to browse a restaurant or brand).""},
             ""meal"":{""type"":""string"",""enum"":[""breakfast"",""lunch"",""dinner"",""snack""]},
             ""servings"":{""type"":""number"",""description"":""Servings the user asked for, default 1. The user can also change the amount on screen.""},
             ""grams"":{""type"":""number"",""description"":""Weight in grams if the user gave one (for example 100). Use this instead of servings.""}},
            ""required"":[""meal""]}},
          {""type"":""function"",""name"":""go_to"",
           ""description"":""Open a screen in the app. Destinations: home; my_world (the profile, nutrition, today's activity and wellbeing notes cards, so 'wellness card' means my_world); food_diary; exercise_log (every exercise recorded for the day); my_profile (their profile page); my_experiences; virtual_gym. Only say you opened something after this returns ok."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""destination"":{""type"":""string"",""enum"":[""home"",""my_world"",""food_diary"",""exercise_log"",""my_profile"",""my_experiences"",""virtual_gym""]}},
            ""required"":[""destination""]}},
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
          {""type"":""function"",""name"":""start_squat_session"",
           ""description"":""Configure a headset-tracked squat workout when the user asks to DO squats, such as 3 sets of 10 reps. Opens the gym, then calibrates and starts after your reply. Does not log any reps."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""sets"":{""type"":""integer"",""minimum"":1,""maximum"":10},
             ""reps"":{""type"":""integer"",""minimum"":1,""maximum"":50},
             ""rest_seconds"":{""type"":""integer"",""minimum"":0,""maximum"":300,""description"":""Default 60 seconds between sets""}},""required"":[""sets"",""reps""]}},
          {""type"":""function"",""name"":""control_squat_session"",
           ""description"":""Control the current headset workout. finish stops and retains actual completed reps; start_next or skip_rest starts the next set after the reply; add_rest extends a rest; change_sets changes the total number of sets. Never use log_set for headset-tracked reps because they are saved automatically."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""action"":{""type"":""string"",""enum"":[""finish"",""start_next"",""skip_rest"",""add_rest"",""change_sets""]},
             ""seconds"":{""type"":""integer"",""minimum"":1,""maximum"":300,""description"":""Extra rest, default 30""},
             ""sets"":{""type"":""integer"",""minimum"":1,""maximum"":10}},""required"":[""action""]}},
          {""type"":""function"",""name"":""draft_post"",
           ""description"":""Prepare a post for the user's A Better U feed and show a preview on screen. Use when the user asks to share or post their session or something they did. Write a short, upbeat, first-person caption under 280 characters using only facts from the message context or tool results, never invented numbers. Optionally give image_prompt: a short description of a scene for a photorealistic 3D render, with no text in it; leave it out to reuse the session card made after a set. This does NOT post anything: the user must tap Post or ask you to post it."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""caption"":{""type"":""string""},
             ""hashtags"":{""type"":""array"",""items"":{""type"":""string""},""description"":""Up to 3, letters and numbers only""},
             ""image_prompt"":{""type"":""string""}},""required"":[""caption""]}},
          {""type"":""function"",""name"":""show_exercise_video"",
           ""description"":""Show the how-to video for an exercise from the A Better U exercise library when the user asks to see how to do an exercise, a demo or a video. Use the exercise name they say, for example Bodyweight Squat. Say only that the video is from the library; never explain technique yourself."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""exercise_name"":{""type"":""string""}},""required"":[""exercise_name""]}},
          {""type"":""function"",""name"":""close_exercise_video"",
           ""description"":""Close the how-to video that is showing, when the user asks to close, hide or stop it."",
           ""parameters"":{""type"":""object"",""properties"":{}}},
          {""type"":""function"",""name"":""publish_post"",
           ""description"":""Publish the draft post the user has just approved, for example when they say post it. Only call after a draft_post and the user's go-ahead. Posts are friends-only unless the user says public."",
           ""parameters"":{""type"":""object"",""properties"":{
             ""visibility"":{""type"":""string"",""enum"":[""friends"",""public""]}}}},
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
            "After logging, confirm naturally in your own words, but always include the food's name, brand if it has one, and its calories, woven into the sentence rather than read out as fields. " +
            "If the user lists several foods, deal with every item: search for each, then call log_food once per item with the right number of servings " +
            "(for example 3 rations or 2 slices means servings 3 or 2). Never say an item is logged until log_food returned ok:true for that item, " +
            "and clearly name any item you could not find or log. " +
            "For any food request, call find_my_meals AND search_foods together in the same step, never one after the other (the user does not have to say 'meal' or 'combo'). " +
            "If one of their saved meals matches, even loosely, such as 'toast and banana' matching a saved 'Toast and banana', call log_meal " +
            "(not log_food) and say its logged_name and kcal. Otherwise log the foods. " +
            "You can open screens with go_to: home, my_world (profile, nutrition, today's activity and wellbeing cards, so 'wellness card' means my_world), food_diary, exercise_log (the exercises recorded for a day), my_profile (their profile page), my_experiences and virtual_gym. " +
            "Only say you opened something after go_to returned ok:true. My Community and Discover are coming soon: say so, do not pretend to open them. " +
            "If the user gives a weight in grams, pass grams to log_food, not servings. " +
            "If search_foods returns exact_match_food_id, the user named exactly that food: log it straight away with log_food and do not offer choices. " +
            "Otherwise, whenever search_foods returns two or more results that could be what the user meant (different products or variants, such as McCain Naked Oven Chips and McCain Straight Cut Chips), " +
            "ALWAYS call offer_choices with the best food_ids (up to 10) instead of choosing for them, even if they named a brand, then say one short sentence asking them to pick one on screen and do not log anything. " +
            "Pass servings or grams (if they gave a weight) to offer_choices so the amount is already set. " +
            "Only log straight away when there is exactly one result, or exactly one result's name is what they asked for. " +
            "If the user asks to see, list or browse a restaurant's or brand's foods, or asks for options or alternatives, search with just the restaurant or brand name (for example McDonald's, not a dish), then call offer_choices with show_all true so they can page through everything. " +
            "If there is no exact match but similar foods exist (for example no plain Cheeseburger but a Double Cheeseburger and a Hamburger), say there is no exact match and offer those. " +
            "When a logged food shows grams (foods listed per 100 g are logged as 100 g per serving), say the grams so the user can correct them. " +
            "Speed matters: make every independent call in a single step. For several foods, search all of them in one step, then log all of them in one step. " +
            "Never split a request into separate foods when a saved meal covers it. " +
            "Only say something is logged after log_food or log_meal returns ok:true. The food search matches the food's own name and handles brand words itself, " +
            "so if it returns nothing, try once more with fewer, simpler words before telling the user it was not found. " +
            "Use the earlier conversation to understand short replies like yes or no. Do not start a workout unless the user asked for one. " +
            "Never invent calories or numbers; use only what tools return. " +
            "If asked what you can do, say in one or two short sentences that you can log food from the A Better U food database, " +
            "set up and track squat workouts, show your exercise log and profile, scan a plate with the headset camera, and share a post to your A Better U feed. " +
            "If the user says something hurts or is sore, call report_injury first (record their words, never diagnose), " +
            "then if they want a workout call build_workout with focus legs unless they ask for upper body, core or full body, " +
            "and pick exactly 3 exercises from its candidates, giving sets and reps. For legs, prefer gentle glute, hip and bodyweight squat options. " +
            "When the user asks to do or start squats, call start_squat_session with their requested sets and reps, default one set of ten and 60 seconds rest. " +
            "For an ongoing headset workout use control_squat_session to stop, skip rest, extend rest or change the number of sets. " +
            "When the user asks to share or post their session or something they did, call draft_post, then tell them the preview is on screen and they can tap Post or say post it. " +
            "When the user asks to see how to do an exercise, or asks for a demo or video, call show_exercise_video, and say the video is from the A Better U library; do not explain technique or give form advice. Close it with close_exercise_video when asked. " +
            "Never say anything is posted until publish_post returns ok. Posts go to the user's A Better U social feed (not to Meta, Facebook or Instagram), and are friends-only unless the user chooses public. Say \"your A Better U feed\" when you mention it. " +
            "Headset-measured sets are saved automatically; never log_set those reps again. Do not claim a session was started or changed without a successful tool result. " +
            "When the user says they finished an exercise outside the tracked headset workout, call log_set with the exercise and the reps they stated, then say what was recorded. " +
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
            string instructions = config.assistantPrompt + AgentRules +
                $" The user's name is {supabase.DisplayName ?? "unknown"}. Today is {now:yyyy-MM-dd}, local time {now:HH:mm}.";

            for (int step = 0; step < MaxSteps; step++)
            {
                var body = new JObject
                {
                    ["model"] = config.model,
                    ["instructions"] = instructions,
                    ["input"] = input,
                    ["tools"] = Tools,
                    ["reasoning"] = new JObject { ["effort"] = config.replyEffortV2 },
                    ["max_output_tokens"] = config.replyMaxTokens,
                };

                string raw = null, err = null;
                float tStep = Time.realtimeSinceStartup;
                yield return Post(body.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
                Debug.Log($"[Timing] spark call {step + 1}: {Time.realtimeSinceStartup - tStep:0.00}s");
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
                        float tTool = Time.realtimeSinceStartup;
                        yield return RunTool(name, args, r => result = r);
                        Debug.Log($"[Timing] tool {name}: {Time.realtimeSinceStartup - tTool:0.00}s");
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
                case "find_my_meals": yield return FindMyMeals((string)args["query"], done); break;
                case "log_meal": yield return LogMeal(args, done); break;
                case "offer_choices": done(OfferChoices(args)); break;
                case "go_to": done(GoTo(args)); break;
                case "report_injury": yield return ReportInjury(args, done); break;
                case "build_workout": yield return BuildWorkout(args, done); break;
                case "draft_post":
                    if (DraftPost != null) yield return DraftPost(args, done); else done(Fail("posting is unavailable"));
                    break;
                case "show_exercise_video":
                    if (ShowExerciseVideo != null) yield return ShowExerciseVideo((string)args["exercise_name"], done); else done(Fail("videos are unavailable"));
                    break;
                case "close_exercise_video":
                    done(CloseExerciseVideo != null ? CloseExerciseVideo() : Fail("videos are unavailable"));
                    break;
                case "publish_post":
                    if (PublishPost != null) yield return PublishPost((string)args["visibility"], done); else done(Fail("posting is unavailable"));
                    break;
                case "log_set":
                    if (HasTrackedSquatWorkout?.Invoke() == true && ((string)args["exercise_name"] ?? "").IndexOf("squat", StringComparison.OrdinalIgnoreCase) >= 0)
                        done(Fail("The headset owns this workout's measured reps and saves each set automatically. Use control_squat_session instead."));
                    else yield return LogSet(args, done);
                    break;
                case "start_squat_session":
                    done(StartSquatSession != null ? StartSquatSession((int?)args["sets"] ?? 1, (int?)args["reps"] ?? 10,
                        (int?)args["rest_seconds"] ?? 60) : Fail("tracked workouts are unavailable")); break;
                case "control_squat_session":
                    done(ControlSquatSession != null ? ControlSquatSession((string)args["action"], (int?)args["seconds"] ?? 30,
                        (int?)args["sets"] ?? 0) : Fail("tracked workouts are unavailable")); break;
                default: done(Fail("unknown tool " + name)); break;
            }
        }

        // Only the columns we use: the full row is about 2.4 KB, so a long result list was slow to download on the headset.
        const string SearchRpc = "search_foods?select=id,name,brand,restaurant,source,image_url,serving_size_g,serving_name,energy_kcal_100g,energy_kcal_serving";

        // Everything the last search found (best first), so the picker can show it all without asking the model again.
        readonly List<JObject> lastSearchAll = new List<JObject>();

        IEnumerator SearchFoods(string query, Action<string> done)
        {
            if (string.IsNullOrWhiteSpace(query)) { done(Fail("query is required")); yield break; }

            // search_foods matches the food NAME only, so a spoken brand ("KFC") or a synonym ("barbecue" for BBQ)
            // returns nothing. Normalise, then retry with up to two leading words dropped ("kfc bbq mini wrap" -> "mini wrap").
            var words = NormaliseQuery(query);
            string raw = "[]", err = null, usedQuery = query;
            // Brand and product words live in different columns ("McCain" is the brand, "chips" is in the name), and the database
            // matches a whole phrase inside one column, so "mccain chips" finds nothing. For several words, fetch by one word
            // (longest first) and keep only rows whose name, brand and restaurant together contain every word.
            bool matchedByWords = false;
            if (words.Count >= 2)
            {
                foreach (var anchor in words.Where(w => w.Length >= 3).Distinct().OrderByDescending(w => w.Length).Take(3).ToList())
                {
                    var anchorBody = new JObject { ["q"] = anchor, ["lim"] = 100, ["uid"] = supabase.UserId }.ToString(Newtonsoft.Json.Formatting.None);
                    string anchorRaw = null, anchorErr = null;
                    yield return supabase.Rpc(SearchRpc, anchorBody, r => anchorRaw = r, e => anchorErr = e);
                    if (anchorErr != null) { done(Fail(anchorErr)); yield break; }

                    var matches = new JArray();
                    foreach (var f in JArray.Parse(anchorRaw))
                        if (ContainsAllWords(f, words)) matches.Add(f);
                    if (matches.Count > 0)
                    {
                        raw = matches.ToString(Newtonsoft.Json.Formatting.None);
                        usedQuery = string.Join(" ", words);
                        matchedByWords = true;
                        break;
                    }
                }
            }

            for (int skip = 0; !matchedByWords && skip < words.Count && skip <= 2; skip++)
            {
                usedQuery = string.Join(" ", words.GetRange(skip, words.Count - skip));
                var body = new JObject { ["q"] = usedQuery, ["lim"] = 100, ["uid"] = supabase.UserId }.ToString(Newtonsoft.Json.Formatting.None);
                yield return supabase.Rpc(SearchRpc, body, r => raw = r, e => err = e);
                if (err != null) { done(Fail(err)); yield break; }
                if (JArray.Parse(raw).Count > 0) break;
            }

            // The shared search ranks restaurant rows ahead of generic foods (alphabetical within a tier, then a small limit), so
            // "apple" never reached the UK CoFID whole foods. For a generic query (no brand in it) put whole foods first here, and
            // drop rows with no calorie data at all, because logging those would write 0 kcal.
            var usable = new List<JToken>();
            foreach (var f in JArray.Parse(raw))
                if ((DashboardData.Num(f["energy_kcal_100g"]) ?? 0) > 0 || (DashboardData.Num(f["energy_kcal_serving"]) ?? 0) > 0) usable.Add(f);

            if (!QueryNamesABrand(usable, words))
                usable = usable.OrderBy(f => SourceTier((string)f["source"])).ThenBy(f => IsRawVariant(f) ? 0 : 1).ToList();

            usable = DedupeSameProduct(usable);

            var rows = new JArray();
            lastSearchAll.Clear();
            foreach (var f in usable.Take(60))
            {
                double? grams = EstimateServingGrams(f);
                if (grams.HasValue) servingGrams[(string)f["id"]] = grams.Value;

                // Foods listed only per 100 g (CoFID) are logged as 100 g per serving, so say exactly that.
                double? kcalServing = DashboardData.Num(f["energy_kcal_serving"]);
                double? kcal100 = DashboardData.Num(f["energy_kcal_100g"]);
                bool per100Only = !grams.HasValue && !(kcalServing > 0);
                var summary = new JObject
                {
                    ["food_id"] = f["id"],
                    ["name"] = f["name"],
                    ["brand"] = f["brand"],
                    ["source"] = f["source"],
                    ["image_url"] = f["image_url"],
                    ["serving"] = per100Only ? "100 g" : f["serving_name"],
                    ["serving_g"] = per100Only ? 100 : f["serving_size_g"],
                    ["kcal_per_serving"] = per100Only ? kcal100 : (kcalServing ?? (kcal100.HasValue && grams.HasValue ? Math.Round(kcal100.Value * grams.Value / 100.0) : (double?)null)),
                };
                foodsSeen[(string)f["id"]] = summary;
                lastSearchAll.Add(summary);
                if (rows.Count < 10) rows.Add(summary);
            }
            // If exactly one food is named exactly what the user said (apart from its brand), say so, so the agent logs it without asking.
            string exactId = null; int exactCount = 0;
            foreach (var f in usable.Take(60))
            {
                var nameWords = new HashSet<string>(NormaliseQuery((string)f["name"] ?? ""));
                var brandWords = new HashSet<string>(NormaliseQuery(((string)f["brand"] ?? "") + " " + ((string)f["restaurant"] ?? "")));
                var core = words.Where(w => !brandWords.Contains(w)).ToList();
                if (core.Count > 0 && nameWords.SetEquals(core)) { exactId = (string)f["id"]; exactCount++; }
            }
            var result = new JObject { ["ok"] = true, ["searched_as"] = usedQuery, ["total_found"] = lastSearchAll.Count, ["results"] = rows };
            if (exactCount == 1) result["exact_match_food_id"] = exactId;
            done(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// Mirrors estimateServingGrams in the web app (src/lib/foods.ts): the serving weight if the food has one,
        /// else grams derived from kcal per serving vs per 100 g, else null (the web app then logs 100 g).
        // The same product often exists from several sources (restaurant list, our own entry). Keep one of each, best first.
        static List<JToken> DedupeSameProduct(List<JToken> rows)
        {
            var seen = new HashSet<string>();
            var kept = new List<JToken>();
            foreach (var f in rows)
            {
                double kcal = DashboardData.Num(f["energy_kcal_serving"]) ?? DashboardData.Num(f["energy_kcal_100g"]) ?? 0;
                string key = string.Join(" ", NormaliseQuery((string)f["name"] ?? "")) + "|" + ((string)f["brand"] ?? "").ToLowerInvariant() + "|" + Math.Round(kcal);
                if (seen.Add(key)) kept.Add(f);
            }
            return kept;
        }

        // True if every word appears somewhere in the food's name, brand or restaurant (apostrophes ignored).
        static bool ContainsAllWords(JToken f, List<string> words)
        {
            string text = (((string)f["name"] ?? "") + " " + ((string)f["brand"] ?? "") + " " + ((string)f["restaurant"] ?? ""))
                .ToLowerInvariant().Replace("'", "").Replace("\u2019", "");
            foreach (var w in words) if (!text.Contains(w)) return false;
            return true;
        }

        // Whole foods first for generic queries. Restaurant and meal-kit rows come last.
        static int SourceTier(string source)
        {
            switch (source)
            {
                case "cofid": return 0;
                case "supermarket": case "abetteru": case "manual": return 1;
                case "off": return 2;
                default: return 3;
            }
        }

        static bool IsRawVariant(JToken f) => ((string)f["name"] ?? "").IndexOf(" raw", StringComparison.OrdinalIgnoreCase) >= 0;

        // True if a word the user said is a brand or restaurant in the results ("kfc", "tesco"), so the database's own order stays.
        static bool QueryNamesABrand(List<JToken> rows, List<string> words)
        {
            foreach (var f in rows)
            {
                string brandText = ((string)f["brand"] ?? "") + " " + ((string)f["restaurant"] ?? "");
                var brandWords = NormaliseQuery(brandText);
                foreach (var w in words) if (w.Length >= 3 && brandWords.Contains(w)) return true;
            }
            return false;
        }

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
            // Apostrophes are dropped (not turned into spaces) and a possessive 's goes with them, so "McDonald's" becomes
            // "mcdonald". Longer plurals are trimmed too ("mcdonalds" -> "mcdonald", "apples" -> "apple"): the search is a
            // substring match, so the shorter form finds the stored "McDonald's" as well as the plural.
            var cleaned = new StringBuilder();
            foreach (char c in query.ToLowerInvariant().Replace("\u2019s", "").Replace("'s", ""))
                if (c != '\'' && c != '\u2019' && c != '\u2018') cleaned.Append(char.IsLetterOrDigit(c) ? c : ' ');
            var words = new List<string>();
            foreach (var w0 in cleaned.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = w0 == "barbecue" || w0 == "barbeque" ? "bbq" : w0;
                if (w.Length >= 6 && w.EndsWith("s") && !w.EndsWith("ss")) w = w.Substring(0, w.Length - 1);
                words.Add(w);
            }
            return words;
        }

        // ── Navigation: the app opens the screen; the agent may only say so after this returned ok ──

        public Func<string, bool> Navigate;      // set by the voice loop; true only if the screen really opened

        string GoTo(JObject args)
        {
            string dest = (string)args["destination"];
            if (Navigate == null || string.IsNullOrEmpty(dest)) return Fail("opening screens is not available");
            if (!Navigate(dest)) return Fail("that screen cannot be opened from here");
            return new JObject { ["ok"] = true, ["opened"] = dest }.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ── Food picker: the agent shows options on screen; the user's tap logs one directly (no model round) ──

        public struct Choice { public string FoodId, Name, Brand, Serving, Source, ImageUrl; public double? Kcal; }

        public event Action<List<Choice>, double, double> ChoicesOffered;
        public bool CanShowChoices;                     // false where there is no screen to show them (the gym)
        string pendingMeal = "snack";
        double pendingServings = 1.0, pendingGrams = 0.0;

        string OfferChoices(JObject args)
        {
            if (!CanShowChoices) return Fail("there is no screen for choices here; pick the best match yourself and call log_food");

            var list = new List<Choice>();
            bool showAll = args["show_all"] != null && (bool?)args["show_all"] == true;
            IEnumerable<JObject> source;
            if (showAll)
                source = lastSearchAll;
            else
            {
                var picked = new List<JObject>();
                if (args["food_ids"] is JArray ids)
                    foreach (var idToken in ids)
                        if (idToken != null && foodsSeen.TryGetValue((string)idToken, out JObject f)) picked.Add(f);
                source = picked;
            }

            foreach (var f in source)
            {
                list.Add(new Choice
                {
                    FoodId = (string)f["food_id"],
                    Name = (string)f["name"],
                    Brand = f["brand"] != null && f["brand"].Type != JTokenType.Null ? (string)f["brand"] : null,
                    Serving = f["serving"] != null && f["serving"].Type != JTokenType.Null ? (string)f["serving"] : null,
                    Source = (string)f["source"],
                    ImageUrl = f["image_url"] != null && f["image_url"].Type != JTokenType.Null ? (string)f["image_url"] : null,
                    Kcal = DashboardData.Num(f["kcal_per_serving"]),
                });
                if (list.Count == 60) break;
            }
            if (list.Count < 2) return Fail("need at least 2 foods from search_foods to show");

            string meal = (string)args["meal"];
            pendingMeal = meal == "breakfast" || meal == "lunch" || meal == "dinner" || meal == "snack" ? meal : DefaultMealSlot(DateTime.Now);
            double servings = args["servings"] != null ? (double)args["servings"] : 1.0;
            pendingServings = servings > 0 && servings <= 20 ? servings : 1.0;
            double grams = args["grams"] != null && args["grams"].Type != JTokenType.Null ? (double)args["grams"] : 0;
            pendingGrams = grams > 0 && grams <= 5000 ? grams : 0;

            ChoicesOffered?.Invoke(list, pendingServings, pendingGrams);
            return new JObject { ["ok"] = true, ["shown"] = list.Count, ["note"] = "The options are on screen with an amount stepper and page buttons. Ask the user to pick one; nothing has been logged." }
                .ToString(Newtonsoft.Json.Formatting.None);
        }

        /// The user tapped an option. Logs it exactly as log_food would, and remembers it so the next spoken turn knows.
        public IEnumerator LogChosen(string foodId, double servings, double grams, Action<string> done)
        {
            if (servings > 0 && servings <= 20) pendingServings = servings;
            var args = new JObject { ["food_id"] = foodId, ["meal"] = pendingMeal, ["servings"] = pendingServings };
            if (grams > 0) args["grams"] = grams;                 // the weight the user chose wins; servings is derived from it
            string result = null;
            yield return LogFood(args, r => result = r);
            Debug.Log($"[Agent] picker log_food -> {result}");

            JObject parsed = null;
            try { parsed = JObject.Parse(result); } catch (Exception) { /* not JSON: treated as a failure below */ }
            if (parsed != null && (bool?)parsed["ok"] == true)
            {
                ToolSucceeded?.Invoke("log_food", parsed);
                history.Add(new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JArray { new JObject { ["type"] = "output_text", ["text"] = $"The user chose {(string)parsed["logged_name"]} on screen and it was logged to {pendingMeal}." } },
                });
                while (history.Count > MaxHistoryMessages) history.RemoveAt(0);
            }
            done(result);
        }

        // For the plate scan: the same food search and log the voice agent uses, so a scanned item is matched and stored exactly like a spoken one.
        public IEnumerator SearchForScan(string query, Action<string> done) => SearchFoods(query, done);
        public IEnumerator LogForScan(string foodId, string meal, double grams, Action<string> done) =>
            LogFood(new JObject { ["food_id"] = foodId, ["meal"] = meal, ["grams"] = grams }, done);
        public static string MealNow() => DefaultMealSlot(DateTime.Now);

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

            // A weight the user said ("100 grams of chips") is stored as that weight; servings is derived for display.
            double gramsRequested = args["grams"] != null && args["grams"].Type != JTokenType.Null ? (double)args["grams"] : 0;
            if (gramsRequested > 0 && gramsRequested <= 5000)
            {
                servings = Math.Round(gramsRequested / gramsPerUnit, 2);
                row["servings"] = servings;
                row["quantity_g"] = Math.Round(gramsRequested, 1);
            }

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
                ["grams"] = row["quantity_g"],
            }.ToString(Newtonsoft.Json.Formatting.None));
        }

        // Every saved meal find_my_meals has returned, so log_meal can only log a meal the database actually showed us.
        readonly Dictionary<string, JObject> mealsSeen = new Dictionary<string, JObject>();

        IEnumerator FindMyMeals(string query, Action<string> done)
        {
            const string foodCols = "serving_size_g,energy_kcal_100g,energy_kcal_serving";
            string raw = null, err = null;
            yield return supabase.Get(
                $"meals?select=id,name,is_combo,servings,default_meal_slot,meal_ingredients(quantity_g,food:food_id({foodCols}))" +
                $"&created_by_user_id=eq.{supabase.UserId}&is_active=eq.true&order=created_at.desc&limit=40",
                r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            var wanted = NormaliseQuery(query ?? "");
            var rows = new JArray();
            foreach (var m in JArray.Parse(raw))
            {
                string name = (string)m["name"];
                string lower = name.ToLowerInvariant();
                bool matches = true;
                foreach (var w in wanted) if (w != "my" && w != "usual" && !lower.Contains(w)) { matches = false; break; }
                // "my usual" alone should list everything; any other word must appear in the name.
                if (!matches) continue;

                // Same arithmetic as the dashboards: sum the ingredients, divide by the recipe's serving count.
                double kcal = 0;
                foreach (var ing in (JArray)m["meal_ingredients"])
                {
                    var food = ing["food"];
                    if (food == null || food.Type == JTokenType.Null) continue;
                    kcal += DashboardData.Portion(food, "energy_kcal", DashboardData.Num(ing["quantity_g"]) ?? 0);
                }
                double recipeServings = Math.Max(1, DashboardData.Num(m["servings"]) ?? 1);

                var summary = new JObject
                {
                    ["meal_id"] = m["id"],
                    ["name"] = name,
                    ["is_combo"] = m["is_combo"],
                    ["kcal_per_serving"] = Math.Round(kcal / recipeServings),
                };
                mealsSeen[(string)m["id"]] = summary;
                rows.Add(summary);
                if (rows.Count == 8) break;
            }
            done(new JObject { ["ok"] = true, ["meals"] = rows }.ToString(Newtonsoft.Json.Formatting.None));
        }

        IEnumerator LogMeal(JObject args, Action<string> done)
        {
            string mealId = (string)args["meal_id"];
            if (string.IsNullOrEmpty(mealId) || !mealsSeen.TryGetValue(mealId, out JObject chosen))
            {
                done(Fail("meal_id was not returned by find_my_meals; call find_my_meals and use an id from its results"));
                yield break;
            }

            string slot = (string)args["meal"];
            if (slot != "breakfast" && slot != "lunch" && slot != "dinner" && slot != "snack")
                slot = DefaultMealSlot(DateTime.Now);

            double servings = args["servings"] != null ? (double)args["servings"] : 1.0;
            if (servings <= 0 || servings > 20) servings = 1.0;

            // Same row the web app's logMealToDiary writes: a meal entry carries servings only (no food_id, no quantity_g).
            var row = new JObject
            {
                ["user_id"] = supabase.UserId,
                ["log_date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["meal_slot"] = slot,
                ["item_type"] = "meal",
                ["meal_id"] = mealId,
                ["servings"] = servings,
                ["position"] = 0,
                ["source"] = "ai_assistant",
            };

            string raw = null, err = null, sameMeal = null;
            yield return supabase.Get($"food_log_entries?select=id&log_date=eq.{row["log_date"]}&meal_slot=eq.{slot}", r => sameMeal = r, e => err = e);
            if (err == null && sameMeal != null) row["position"] = JArray.Parse(sameMeal).Count;
            err = null;

            yield return supabase.Insert("food_log_entries", row.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            done(new JObject
            {
                ["ok"] = true,
                ["logged_name"] = chosen["name"],
                ["kcal_per_serving"] = chosen["kcal_per_serving"],
                ["meal"] = slot,
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
