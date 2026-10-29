using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Plate scan. Two Meta models read one photo: Muse Spark (vision) names the foods and estimates how much of each is on the plate,
    /// and SAM 3.1 finds each food in the picture and returns its outline box. Each food is then matched to the user's real food
    /// database. Nothing is logged until the user confirms, and calories come only from the database match, never from the model.
    public partial class VoiceLoopTest
    {
        public sealed class PlateItem
        {
            public string Name;
            public double Grams;                          // starts as Spark's visual estimate; the user can correct it with - and + (weigh it for a true figure)
            public double EstimatedGrams;                 // what Spark first guessed, kept so the log can show how far out it was
            public readonly List<Rect> Boxes = new List<Rect>();   // from SAM, as fractions of the photo, y measured from the top
            public string FoodId, MatchedName;
            public double? Kcal;                          // for Grams, from the database match
            public bool Dropped;                          // the user removed it

            // The best few database entries for this food, best first. The user can tap to swap to the next one.
            public readonly List<PlateCandidate> Candidates = new List<PlateCandidate>();
            public int Choice;

            public void Choose(int index)
            {
                if (Candidates.Count == 0) return;
                Choice = ((index % Candidates.Count) + Candidates.Count) % Candidates.Count;
                var c = Candidates[Choice];
                FoodId = c.FoodId; MatchedName = c.Label;
                Kcal = c.KcalPerServing.HasValue && c.ServingG > 0 ? Math.Round(c.KcalPerServing.Value * Grams / c.ServingG.Value) : (double?)null;
            }

            public void Next() => Choose(Choice + 1);

            public void AdjustGrams(double delta)
            {
                Grams = Math.Max(10, Math.Min(2000, Math.Round(Grams + delta)));
                Choose(Choice);                           // recalculates the calories for the new weight
            }
        }

        public struct PlateCandidate { public string FoodId, Label; public double? KcalPerServing, ServingG; }

        public sealed class PlateResult { public string Error; public readonly List<PlateItem> Items = new List<PlateItem>(); }

        public CoachAgent Agent => agent;

        // The same settings asset the rest of the voice loop loads in Start (a local there), fetched once for the scan code.
        ModelApiConfig scanConfig;
        ModelApiConfig Cfg => scanConfig != null ? scanConfig : (scanConfig = ModelApiConfig.Load());

        const string PlatePrompt =
            "You are looking at a photo of a plate or tray of food. List each distinct food or drink you can clearly see, at most 5. " +
            "For each give name: a plain, common food name that would be found in a food database, including how it is cooked when you can tell " +
            "(for example grilled chicken breast, boiled potatoes, white rice, raw red pepper, fried egg). A whole uncooked potato is called baking potato (or new potato if it is small), and a whole sweet or bell pepper is named by its colour, for example orange pepper or red pepper. Then grams: your best estimate of the total weight of that food on the plate, a whole number. " +
            "Think about real sizes: a baking potato is about 200 to 300 g, a whole bell pepper about 150 to 220 g, a chicken breast about 150 g, a cooked portion of rice about 180 g, a slice of bread about 35 g. " +
            "Reply with JSON only and nothing else, exactly like {\"items\":[{\"name\":\"white rice\",\"grams\":150}]}. " +
            "If there is no food, reply {\"items\":[]}.";

        public IEnumerator ScanPlate(byte[] jpg, Action<PlateResult> done, Action<string> progress)
        {
            busy = true;
            var result = new PlateResult();
            string dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(jpg);

            // 1. Muse Spark looks at the photo.
            progress("Looking at your plate...");
            string text = null, error = null;
            yield return ImageModelCall(Cfg.model, PlatePrompt, dataUrl, false, true, t => text = t, e => error = e);
            if (error != null || string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("[Scan] Spark failed: " + error);
                result.Error = "I couldn't read the photo. Try again.";
                busy = false; done(result); yield break;
            }
            try
            {
                int open = text.IndexOf('{'), close = text.LastIndexOf('}');
                var items = (JArray)JObject.Parse(text.Substring(open, close - open + 1))["items"];
                foreach (var i in items.Take(5))
                {
                    string name = ((string)i["name"] ?? "").Trim();
                    double grams = DashboardData.Num(i["grams"]) ?? 100;
                    if (name.Length > 0) { double g = Math.Max(10, Math.Min(1500, Math.Round(grams))); result.Items.Add(new PlateItem { Name = name, Grams = g, EstimatedGrams = g }); }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Scan] Could not read Spark's answer: " + e.Message + " :: " + text); }
            Debug.Log($"[Scan] Spark found {result.Items.Count}: {string.Join(", ", result.Items.Select(i => i.Name + " " + i.Grams + "g"))}");
            if (result.Items.Count == 0)
            {
                result.Error = "I couldn't see any food. Move closer and try again.";
                busy = false; done(result); yield break;
            }

            // 2. SAM 3.1 outlines each food, all at once.
            progress("Outlining each food...");
            int pending = result.Items.Count;
            foreach (var item in result.Items) StartCoroutine(SegmentItem(item, dataUrl, () => pending--));
            for (float t = 0f; pending > 0 && t < 60f; t += Time.unscaledDeltaTime) yield return null;

            // 3. Match each food to the user's own food database. The search ranks by name only, so a plain "potato" can come back as
            // dehydrated potato powder first. Look at the top results and prefer the ordinary food: if the full name finds nothing, try
            // its last word ("grilled chicken" then "chicken"), and mark down powders, mixes, sauces and the like unless they were asked for.
            progress("Matching your food database...");

            // What this person has logged before: a food they have eaten is more likely to be the one they mean than a lookalike.
            var used = new Dictionary<string, int>();
            string history = null;
            yield return supabase.Get("food_log_entries?select=food_id&food_id=not.is.null&order=created_at.desc&limit=300", r => history = r, e => { });
            try
            {
                foreach (var row in JArray.Parse(history))
                {
                    string id = (string)row["food_id"];
                    if (id != null) used[id] = used.TryGetValue(id, out var n) ? n + 1 : 1;
                }
            }
            catch (Exception) { /* no history is fine: ranking falls back to generic entries first */ }

            foreach (var item in result.Items)
            {
                var words = item.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var queries = new List<string> { item.Name };
                if (words.Length > 1) queries.Add(words[words.Length - 1]);

                foreach (var query in queries)
                {
                    string found = null;
                    yield return agent.SearchForScan(query, r => found = r);
                    try
                    {
                        var j = JObject.Parse(found);
                        var list = j["results"] as JArray;
                        if (list == null || list.Count == 0) continue;
                        string exact = (string)j["exact_match_food_id"];

                        var scored = new List<KeyValuePair<int, JToken>>();
                        for (int k = 0; k < Math.Min(10, list.Count); k++)
                        {
                            string id = (string)list[k]["food_id"];
                            int score = MatchScore((string)list[k]["name"], query, k) - (id == exact ? 5 : 0) - (used.TryGetValue(id, out var times) ? Math.Min(14, 8 + times) : 0);
                            scored.Add(new KeyValuePair<int, JToken>(score, list[k]));
                        }
                        var ranked = scored.OrderBy(x => x.Key).ToList();
                        var good = ranked.Where(x => x.Key < 15).Take(5).ToList();      // drop the powders, mixes and the like from the swap list
                        if (good.Count == 0) good = ranked.Take(1).ToList();

                        foreach (var pair in good)
                        {
                            var food = pair.Value;
                            string brand = (string)food["brand"];
                            item.Candidates.Add(new PlateCandidate
                            {
                                FoodId = (string)food["food_id"],
                                Label = (string)food["name"] + (string.IsNullOrEmpty(brand) || ((string)food["name"]).Contains(brand) ? "" : $" ({brand})"),
                                KcalPerServing = DashboardData.Num(food["kcal_per_serving"]),
                                ServingG = DashboardData.Num(food["serving_g"]),
                            });
                        }
                        item.Choose(0);
                        Debug.Log($"[Scan] '{item.Name}' matched '{item.MatchedName}' (searched '{query}', {item.Candidates.Count} choice(s), {(used.ContainsKey(item.FoodId) ? "logged before" : "new to you")})");
                        break;
                    }
                    catch (Exception e) { Debug.LogWarning("[Scan] Match failed for " + item.Name + ": " + e.Message); }
                }
            }
            busy = false;
            done(result);
        }

        // Lower is better. The search's own order is the baseline; processed or dried forms and prepared dishes are pushed down unless the
        // user's food name asked for them, and a name that starts with what we searched for, or is a plain raw or cooked food, is pulled up.
        static int MatchScore(string foodName, string query, int rank)
        {
            string n = (foodName ?? "").ToLowerInvariant(), q = query.ToLowerInvariant();
            int score = rank;
            foreach (var word in new[] { "powder", "dried", "dehydrated", "instant", "flour", "crisps", "mix", "sauce", "soup", "extract", "paste", "puree", "canned", "tinned", "juice", "oil", "starch", "flakes", "granules", "chips", "cakes", "fried", "roast", "rings", "waffle", "wedges", "hash", "dauphinoise" })
                if (n.Contains(word) && !q.Contains(word)) score += 20;
            if (n.StartsWith(q)) score -= 3;
            if (n.Contains("raw") || n.Contains("boiled") || n.Contains("cooked") || n.Contains("grilled") || n.Contains("baked")) score -= 2;
            return score;
        }

        IEnumerator SegmentItem(PlateItem item, string dataUrl, Action finished)
        {
            // SAM is asked for one concept at a time. The full name Spark gave ("baking potato") sometimes finds nothing where the plain
            // noun ("potato") does, so if the first try outlines nothing, try the last word before giving up.
            var words = item.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var prompts = new List<string> { item.Name };
            if (words.Length > 1) prompts.Add(words[words.Length - 1]);

            foreach (var prompt in prompts)
            {
                string text = null, error = null;
                yield return ImageModelCall("sam-3.1", prompt, dataUrl, true, false, t => text = t, e => error = e);
                if (error != null) { Debug.LogWarning($"[Scan] SAM failed for '{prompt}': {error}"); continue; }
                if (text == null) continue;

                // Each object comes back as <|box;x1=..;y1=..;x2=..;y2=..;w=..;h=..;c=..|> followed by its mask; the boxes are enough here.
                foreach (Match m in Regex.Matches(text, @"<\|box;x1=(\d+);y1=(\d+);x2=(\d+);y2=(\d+);w=(\d+);h=(\d+)(?:;c=([\d.]+))?\|>"))
                {
                    float w = float.Parse(m.Groups[5].Value), h = float.Parse(m.Groups[6].Value);
                    float x1 = float.Parse(m.Groups[1].Value) / w, y1 = float.Parse(m.Groups[2].Value) / h;
                    float x2 = float.Parse(m.Groups[3].Value) / w, y2 = float.Parse(m.Groups[4].Value) / h;
                    float conf = m.Groups[7].Success ? float.Parse(m.Groups[7].Value, System.Globalization.CultureInfo.InvariantCulture) : 1f;
                    if (conf < 0.4f || (x2 - x1) * (y2 - y1) < 0.004f || item.Boxes.Count >= 6) continue;
                    item.Boxes.Add(new Rect(x1, y1, x2 - x1, y2 - y1));
                }
                Debug.Log($"[Scan] SAM '{prompt}': {item.Boxes.Count} box(es)");
                if (item.Boxes.Count > 0) break;
            }
            finished();
        }

        // One image request to the Meta Model API. Spark gets reasoning settings; SAM is asked to stream, with small masks and confidences.
        IEnumerator ImageModelCall(string model, string prompt, string dataUrl, bool sam, bool reasoning, Action<string> ok, Action<string> fail)
        {
            var body = new JObject
            {
                ["model"] = model,
                ["input"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "message", ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "input_text", ["text"] = prompt },
                            new JObject { ["type"] = "input_image", ["image_url"] = dataUrl },
                        },
                    },
                },
            };
            if (reasoning) { body["reasoning"] = new JObject { ["effort"] = Cfg.replyEffortV2 }; body["max_output_tokens"] = Cfg.replyMaxTokens; }
            if (sam) { body["stream"] = true; body["metadata"] = new JObject { ["include_confidence"] = "true", ["mask_encoding"] = "one_bit" }; }

            using var req = new UnityWebRequest(Cfg.endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 60,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + Cfg.apiKey);
            if (sam) req.SetRequestHeader("Accept", "text/event-stream");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string snippet = req.downloadHandler.text ?? "";
                fail($"{req.responseCode} {req.error} {(snippet.Length > 200 ? snippet.Substring(0, 200) : snippet)}");
                yield break;
            }
            ok(OutputText(req.downloadHandler.text));
        }

        // Plain JSON reply (message text) or a streamed reply (the deltas joined), whichever the API sent.
        static string OutputText(string raw)
        {
            raw = (raw ?? "").Trim();
            var sb = new StringBuilder();
            try
            {
                if (raw.StartsWith("{"))
                {
                    foreach (var item in (JArray)JObject.Parse(raw)["output"])
                        if ((string)item["type"] == "message")
                            foreach (var c in (JArray)item["content"]) sb.Append((string)c["text"]);
                    return sb.ToString();
                }
                foreach (var line in raw.Split('\n'))
                {
                    var l = line.Trim();
                    if (!l.StartsWith("data:")) continue;
                    var j = JObject.Parse(l.Substring(5).Trim());
                    if ((string)j["type"] == "response.output_text.delta") sb.Append((string)j["delta"]);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Scan] Could not read the model's reply: " + e.Message); }
            return sb.ToString();
        }

        // ── Confirmed logging ────────────────────────────────────────────────────

        public IEnumerator LogScan(List<PlateItem> items, Action<string> done)
        {
            busy = true;
            string meal = CoachAgent.MealNow();
            var logged = new List<string>();
            double kcal = 0;
            foreach (var item in items.Where(i => !i.Dropped && i.FoodId != null))
            {
                string reply = null;
                yield return agent.LogForScan(item.FoodId, meal, item.Grams, r => reply = r);
                try
                {
                    var j = JObject.Parse(reply);
                    if ((bool?)j["ok"] != true) { Debug.LogWarning("[Scan] Log failed: " + reply); continue; }
                    logged.Add((string)j["logged_name"]);
                    Debug.Log($"[Scan] logged '{item.Name}' as '{(string)j["logged_name"]}': Spark guessed {item.EstimatedGrams:0} g, logged {item.Grams:0} g");
                    kcal += item.Kcal ?? 0;
                }
                catch (Exception) { }
            }
            busy = false;
            if (logged.Count == 0) { done("Nothing was logged. Try again."); yield break; }

            string text = $"{logged.Count} item{(logged.Count == 1 ? "" : "s")} logged to {meal}";
            OnSaved?.Invoke(text);                                   // refreshes the dashboards from the database
            Report(AssistantState.Done, "Saved", text);
            readyAt = Time.unscaledTime + 8f;
            if (tts != null && tts.Ready)
                tts.Speak($"Done, {string.Join(", ", logged.Take(3))}{(logged.Count > 3 ? " and more" : "")} logged to {meal}{(kcal > 0 ? $", about {kcal:0} calories" : "")}.",
                    () => { }, (s, secs) => ShowCaption(AssistantState.Speaking, WakeName, s, secs));
            done(text + (kcal > 0 ? $" · about {kcal:0} kcal" : ""));
        }
    }
}
