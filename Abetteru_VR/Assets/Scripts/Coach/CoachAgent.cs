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
            ""required"":[""food_id"",""meal""]}}
        ]");

        const string AgentRules =
            " You can act on the user's A Better U account with tools. To log food: call search_foods, pick the closest " +
            "plain or generic match (avoid restaurant-branded items unless the user named one), then call log_food. " +
            "Only say something is logged after log_food returns ok:true. If nothing matches, say so and ask. " +
            "Never invent calories or numbers; use only what tools return.";

        /// Runs one user turn. onProgress gets short status text (tool names) for the board.
        public IEnumerator Run(string userText, Action<string> onProgress, Action<string> onReply, Action<string> onError)
        {
            var input = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray { new JObject { ["type"] = "input_text", ["text"] = userText } },
                },
            };

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
                    onReply(text);
                    yield break;
                }
            }
            onError("Agent used too many steps.");
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
                default: done(Fail("unknown tool " + name)); break;
            }
        }

        IEnumerator SearchFoods(string query, Action<string> done)
        {
            if (string.IsNullOrWhiteSpace(query)) { done(Fail("query is required")); yield break; }

            var body = new JObject { ["q"] = query, ["lim"] = 5, ["uid"] = supabase.UserId }.ToString(Newtonsoft.Json.Formatting.None);
            string raw = null, err = null;
            yield return supabase.Rpc("search_foods", body, r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            var rows = new JArray();
            foreach (var f in JArray.Parse(raw))
            {
                if (f["serving_size_g"]?.Type != JTokenType.Null && f["serving_size_g"] != null)
                    servingGrams[(string)f["id"]] = (double)f["serving_size_g"];
                rows.Add(new JObject
                {
                    ["food_id"] = f["id"],
                    ["name"] = f["name"],
                    ["brand"] = f["brand"],
                    ["serving"] = f["serving_name"],
                    ["serving_g"] = f["serving_size_g"],
                    ["kcal_per_serving"] = f["energy_kcal_serving"],
                });
            }
            done(new JObject { ["ok"] = true, ["results"] = rows }.ToString(Newtonsoft.Json.Formatting.None));
        }

        IEnumerator LogFood(JObject args, Action<string> done)
        {
            string foodId = (string)args["food_id"];
            if (string.IsNullOrEmpty(foodId)) { done(Fail("food_id is required; call search_foods first")); yield break; }

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

            if (servingGrams.TryGetValue(foodId, out double gramsPerServing))
                row["quantity_g"] = Math.Round(gramsPerServing * servings, 1);

            string raw = null, err = null;
            yield return supabase.Insert("food_log_entries", row.ToString(Newtonsoft.Json.Formatting.None), r => raw = r, e => err = e);
            if (err != null) { done(Fail(err)); yield break; }

            done(new JObject { ["ok"] = true, ["meal"] = meal, ["servings"] = servings }.ToString(Newtonsoft.Json.Formatting.None));
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
