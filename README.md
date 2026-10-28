# A Better U VR

Entry in the **Meta Global AI Developer Hackathon** (Indie Developer track, theme "Build for Your World"). Build window: Oct 26 to Nov 4, 2026.

A Meta Quest 3S app where you **talk to Muse Spark and it takes real actions on a real health platform**. Say "log my usual breakfast" and the meal lands in your A Better U food diary. Say "my knee is sore" and your workouts avoid it. Finish a squat set in the virtual gym and it is saved to your activity, with a shareable session card made by Muse Image.

Not a chatbot in a headset: every spoken request ends in a verified write to a live database, and the app only says "saved" after the backend confirmed it.

## Meta tools used

| # | Meta tool | What it does in the app |
|---|---|---|
| 1 | **Muse Spark** (`muse-spark-1.3`, Meta Model API) | The tool-calling agent. Decides which action to take and talks the user through it. |
| 2 | **Muse Voice Transcribe** (`muse-voice-transcribe-1.0`) | Turns the user's voice (hold right trigger) into text, with UK English and food/fitness keyword biasing. |
| 3 | **Muse Image** (`muse-image-1.0`) | Makes the artwork for the session card after a squat set. The rep count on the card is drawn by the app from the headset's own count, never by the model. |

Speech output uses Wit.ai (Meta-owned, a US voice). It is not counted above.

## What the agent can do

Tools Muse Spark can call, each writing to the signed-in user's real A Better U account:

| Tool | Effect |
|---|---|
| `search_foods` | Searches the food database (UK supermarket and restaurant items). |
| `log_food` | Logs a food to the diary, by servings or grams. |
| `find_my_meals`, `log_meal` | Finds the user's saved meals and logs one as a single entry. |
| `report_injury` | Records a sore or injured area. Later workouts are filtered by it in code, not just by the model. |
| `build_workout` | Builds a workout that avoids the recorded injuries. |
| `log_set` | Saves a squat set to the user's activity. |

**Honesty by design:** tool results are the only source of truth. A code-level check replaces any "logged" or "saved" claim in a reply when no write actually succeeded, and the "Saved" confirmation card appears only after the backend returned ok.

## The experience

1. **Login**, including a one-tap **Judge Instant Login** for reviewers.
2. **Home** and **My World**: live dashboards from the real account (calories, macros, workouts this week, weight, active injuries).
3. **Food Diary** with day navigation, showing single foods and saved meals.
4. **My Experiences** then the **Virtual Gym**: a guided squat session. The headset counts reps from head height, shows a large rep count and a depth bar with the real "counts as down" line, and gives live cues.
5. **Form check:** leaning forward or back, or tilting the head down, shows "Keep straight and crouch down straight", and that movement is **not counted** (a false rep is worse than a missed one). The Quest only tracks the head, so this judges head position, not the back or knees.
6. **Session summary** with reps, pace, depth, save status and the Muse Image card.

## New versus existing (original contributions)

**Existed before the hackathon, not part of the submission:** the A Better U web app and phone app, and their Supabase database (accounts, food database, exercise library, diary).

**Built during the hackathon (this repo, from Oct 26):**
- The whole Unity VR app: login, dashboards, environments, gym, mannequin and mirror, squat tracking and form check, HUD, session card.
- The Muse Spark tool-calling agent, the honesty checks, the voice loop, and the Muse Voice Transcribe and Muse Image clients.
- Backend work for this project: the `user_injuries` table and its row-level security ([001](docs/migrations/001_user_injuries.sql)), and demo saved meals for the judge account ([002](docs/migrations/002_seed_demo_meals.sql)). On the web app: an injuries card, an injury history page and a servings calorie fix.

**Pre-kickoff commit, disclosed:** the first commit in this repo is **Oct 25**, the day before kickoff. It was preparation only: planning documents, a deploy script and an interface scaffold with stubs. All working functionality was written from Oct 26. Preparation before kickoff was confirmed as allowed, provided coding had not started.

**How it was built:** with Claude Code as the coding assistant, directed and tested by the developer on a real Quest 3S. Parts of the code were written by the developer, including the rep-detection step.

**Muse Code:** from Oct 28, Muse Code (Meta) is also used on the project, both implementing changes and as a second pair of eyes reviewing the repo. Its reviews so far caught submission risks such as a duplicate scene and uncommitted build settings.

More detail in [docs/HACKATHON_COMPLIANCE.md](docs/HACKATHON_COMPLIANCE.md).

## Known limits

- A voice turn takes about 6 seconds (speech to text, then Muse Spark, then speech out). Reducing it is the next engineering task.
- Squat counting and the form check use head tracking only. Thresholds were tuned from logged on-device data (lean limit 15 cm) and may need adjusting for different body types.
- The gym is built from simple shapes, and the voice is a US voice.
- The Meta Model API key is bundled in the APK for the demo build. **Do not distribute built APKs publicly.**

## Run it

You need a Quest 3S in developer mode, Unity 6000.6.7f1 with Android Build Support, and your own Meta Model API key (this repo contains no secrets).

1. Open `Abetteru_VR/` in Unity Hub.
2. Create `Assets/Resources/ModelApiSecrets` (Assets > Create > ABetterU > Model API Secrets). It is git-ignored. Fill in your Meta Model API key, Wit.ai token, Supabase URL and anon key, and the demo account's credentials for Judge Instant Login.
3. Build for Android and sideload: `./scripts/deploy-quest.sh path/to/build.apk`.

Controls: hold **right trigger** to talk; **left trigger** starts and ends a squat set; **left Menu** opens the in-gym menu.

## Repo layout

```
Abetteru_VR/Assets/Scripts/Coach/   the app (agent, voice loop, dashboards, gym, HUD)
docs/                               API notes, Supabase notes, compliance notes, SQL migrations
scripts/deploy-quest.sh             sideload an APK to the Quest over adb
PLAN.md                             the original build plan
```

The two PDFs in the root are the original application and build plan.
