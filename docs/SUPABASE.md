# A Better U backend (Supabase): what the VR app uses

Project: **ABetterU** (`eu-west-2`). Read-only schema review done Oct 26; **no data was read or written**.
All public tables have RLS enabled. Keys: only the public **anon/publishable** key ever goes in the app; never the service-role key.

## Tables the agent's tools map to

| Tool | Writes | Notes |
|---|---|---|
| `log_food` | `food_log_entries` | needs `user_id`, `log_date`, `meal_slot` (breakfast/lunch/dinner/snack), `item_type` ('food' or 'meal'), `food_id`, `quantity_g` or `servings`, `position`. `source` must be one of manual / ai_scan / **ai_assistant** / alexa (use `ai_assistant`). Resolve `food_id` first with RPC `search_foods(q, lim, uid)`. |
| `build_workout` | reads `get_exercise_library(p_include_inactive)` (RPC, security definer) | `exercise_library` is not directly readable by a normal user; use the RPC. Filter by `avoid_areas` in code or in the prompt. Result is shown as a card; saving a plan is optional. |
| `log_set` | `exercise_logs` (`exercise_name`, `sets_completed`, `reps_completed` text, `date`, `duration_min`, `notes`, `source` text, no check constraint, use `'vr'`) and/or `strength_sets` (`exercise_name`, `set_number`, `reps`, `weight_kg` NOT NULL, `date`) | `workout_plan_id` is optional. |

## RLS (row-level security) as reviewed

- `food_log_entries`: ALL where `user_id = auth.uid()`.
- `exercise_logs`: select/insert/delete own (`user_id = auth.uid()`).
- `strength_sets`: ALL own.
- `profiles`: select/insert/update own (`user_id = auth.uid()`).
- `foods`: readable if active and from shared sources, or the user's own manual rows.

So a signed-in judge account can only read and write its own rows.

## Judge test account (plan)

- A normal email/password user created in the Supabase dashboard (Authentication > Users, auto-confirm), not an admin, free tier, `leaderboard_visibility` private.
- Seed only that user's own profile and a little demo history, after explicit approval.
- "Judge Instant Login" signs in with that user via `POST /auth/v1/token?grant_type=password` (anon key in the `apikey` header).
- Credentials and the anon key live in the git-ignored `ModelApiSecrets` asset, never in the public repo.
- Anyone with the APK can extract those credentials, which is acceptable only because RLS limits that user to its own data.
