using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// One day of the user's food diary, read from their real food_log_entries. Totals use the same calorie
    /// arithmetic as the dashboards (DashboardData.Portion), so the numbers match the web app.
    public class DiaryData
    {
        public struct Item { public string Id, Slot, Name, Amount, ImageUrl; public double Kcal, Protein, Carbs, Fat; }

        public DateTime Date = DateTime.Now.Date;
        public bool Loading, Loaded, HadError;
        public readonly List<Item> Items = new List<Item>();
        public double Kcal, Protein, Carbs, Fat;

        public IEnumerator Load(SupabaseClient db, DateTime date)
        {
            Date = date.Date;
            Loading = true; Loaded = false; HadError = false;
            Items.Clear();
            Kcal = Protein = Carbs = Fat = 0;

            const string foodCols = "name,brand,image_url,serving_size_g,energy_kcal_100g,energy_kcal_serving,protein_g_100g,protein_g_serving," +
                                    "carbohydrate_g_100g,carbohydrate_g_serving,fat_g_100g,fat_g_serving";
            string day = Date.ToString("yyyy-MM-dd");
            string raw = null, err = null;
            yield return db.Get($"food_log_entries?select=id,meal_slot,servings,quantity_g,created_at,food:food_id({foodCols}),{DashboardData.MealEmbed.Replace("meal:meal_id(name,", "meal:meal_id(name,image_url,")}&log_date=eq.{day}&order=created_at.asc",
                r => raw = r, e => err = e);

            JArray rows = null;
            if (err == null) { try { rows = JArray.Parse(raw); } catch (Exception) { /* handled below */ } }
            if (rows == null)
            {
                Debug.LogWarning($"[Diary] load failed for {day}: {err}");
                HadError = true;
                Loading = false; Loaded = true;
                yield break;
            }

            foreach (var entry in rows)
            {
                if (!DashboardData.EntryMacros(entry, out string itemName, out double kcal, out double protein, out double carbs, out double fat)) continue;
                Kcal += kcal; Protein += protein; Carbs += carbs; Fat += fat;

                var food = entry["food"];
                string brand = food != null && food.Type != JTokenType.Null ? (string)food["brand"] : null;
                string name = itemName + (string.IsNullOrEmpty(brand) ? "" : $" ({brand})");
                double servings = DashboardData.Num(entry["servings"]) ?? 0;
                double qty = DashboardData.Num(entry["quantity_g"]) ?? 0;
                // Show the weight that was logged (a scanned 110 g potato reads "110 g", not "x1.1"). A whole number of servings is shown
                // too ("x2 · 430 g"), because that is how the person logged it; a saved meal has no weight, so it stays "x1".
                string amount = qty > 0
                    ? (servings >= 1 && Math.Abs(servings - Math.Round(servings)) < 0.001 ? $"x{servings:0} · {qty:0.#} g" : $"{qty:0.#} g")
                    : servings > 0 ? $"x{servings:0.##}" : "";

                var meal = entry["meal"];
                bool savedMeal = (string)entry["item_type"] == "meal";
                string image = savedMeal && meal != null && meal.Type != JTokenType.Null
                    ? (string)meal["image_url"]
                    : food != null && food.Type != JTokenType.Null ? (string)food["image_url"] : null;
                Items.Add(new Item { Id = (string)entry["id"], Slot = (string)entry["meal_slot"], Name = name, Amount = amount,
                    Kcal = kcal, Protein = protein, Carbs = carbs, Fat = fat, ImageUrl = image });
            }

            Loading = false;
            Loaded = true;
        }
    }
}
