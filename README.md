# A Better U VR

**Talk to Atlas. She does things on a real platform.**

Entry in the **Meta Global AI Developer Hackathon** (Indie Developer track, theme "Build for Your World"). Build window: Oct 26 to Nov 4, 2026.

A Meta Quest 3S app where you talk to **Atlas**, a voice assistant powered by Meta's Muse Spark, and it **does things for you on a real platform**. Say "log my usual breakfast" and the meal lands in your A Better U food diary. Look at a plate and Atlas names the foods, outlines them with SAM 3.1, measures them and logs what you confirm. Say "set me up a squat workout, two sets of ten" and the headset tracks your reps and saves each set. Say "share my session" and it drafts a post for your **A Better U social feed** (not Meta, Facebook or Instagram), with a picture made by Muse Image, for you to approve.

**The proof point:** the plate scan chains Muse Spark and SAM 3.1 to measure food from one photo. In a weigh-in of two foods on one plate it landed within **about 1% and 4% of the scales**, against 56% and 37% low for a vision guess alone.

**Atlas is an assistant, not a coach.** It gives no medical or fitness advice, and says so when it introduces itself. Every request ends in a verified write to a live database, and the app only says "saved" or "posted" after the backend confirmed it.

## Try it in about three minutes

1. Tap **Judge Instant Login**. Atlas introduces herself.
2. Say **"Hey Atlas, log a Big Mac for lunch."** Watch the Food Diary update.
3. Open **My World**, then **Scan a Plate**: look at a plate from slightly above, press the left trigger, check the outlines and the weights, tap **Log**.
4. Say **"Hey Atlas, set me up a squat workout, two sets of ten."** The gym opens, you stand tall, calibrate and squat. A session card appears; tap **Share to feed** and approve the post.
5. Open **My Profile** and **Exercise Log** to see everything that was saved.

## Meta tools used

| # | Meta tool | What it does in the app |
|---|---|---|
| 1 | **Muse Spark** (`muse-spark-1.3`, Meta Model API), used two ways | **(a) Agent:** the tool-calling brain behind Atlas. **(b) Vision:** in the plate scan it names the foods on a photo and says how each is shaped and how dense it is. |
| 2 | **Muse Voice Transcribe** (`muse-voice-transcribe-1.0`) | Turns speech into text, over a realtime WebSocket (about 0.3 s after you stop speaking) with an upload fallback, with UK English and keyword biasing. |
| 3 | **Muse Image** (`muse-image-1.0`) | Makes the photoreal 3D session card after a workout and the picture on a shared post. Numbers on the card are drawn by the app from the headset's own count, never by the model. |
| 4 | **SAM 3.1** (`sam-3.1`, Meta Model API) | Outlines each food and the plate in the plate-scan photo. The plate's known width gives the scale for measuring the food. |

Speech output uses Wit.ai (Meta-owned, a US voice). It is not counted above.

## What Atlas can do

Tools Muse Spark can call, each acting on the signed-in user's real A Better U account:

| Tool | Effect |
|---|---|
| `search_foods`, `offer_choices` | Searches the food database (UK supermarket, restaurant and CoFID items) and shows a picker when several products fit. |
| `log_food`, `find_my_meals`, `log_meal` | Logs a food by servings or grams, or one of the user's saved meals. |
| `go_to` | Opens Home, My World, the Food Diary, the Exercise Log, My Profile, My Experiences or the Virtual Gym. |
| `start_squat_session`, `control_squat_session` | Sets up a headset-tracked squat workout (sets, reps, rest), then finishes it, skips or extends rest, or changes the number of sets. |
| `log_set` | Saves a set the user did outside the tracked workout. |
| `draft_post`, `publish_post` | Drafts a feed post with a preview, then publishes it only after the user approves. |
| `report_injury`, `build_workout` | Notes a sore area so exercise suggestions avoid it (filtered in code, not just by the model). No medical advice is given. |

> **Food data is UK-based.** Foods, restaurant items (a McDonald's Big Mac, for example) and calories come from the A Better U UK food database (UK supermarket, restaurant and CoFID items), so portion sizes and calories can differ from the US versions of the same product. The Food Diary screen says so in the headset.

**Honesty by design:** tool results are the only source of truth. A code-level check replaces any "logged" or "saved" claim in a reply when no write actually succeeded, and the "Saved" confirmation card appears only after the backend returned ok.

A post is only called "posted" after the platform's own `create_post` function succeeds and the post is read back as its author. Posts default to **Friends** with a Friends/Public toggle on the preview, and a draft is never published without the user's tap or spoken go-ahead.

## Plate scan: a chain of Meta models

1. **Capture:** the headset's passthrough camera (with a viewfinder and a controller trigger, so it works while looking down at the plate).
2. **Muse Spark** looks at the photo and names each food, estimates its weight, and says its shape, density and thickness.
3. **SAM 3.1** outlines each food and the plate. The plate is round, so its outline gives the scale on each axis, which also cancels the camera's tilt.
4. The app turns each food's outline into centimetres and works out a **measured weight** (footprint x thickness x shape x density). Spark's own guess is shown beside it.
5. Each food is matched to the user's real food database (preferring foods they have logged before), with tap-to-swap and weight correction. **Nothing is logged until the user confirms**, and calories come only from the database, never from a model.

In a weigh-in on a real plate (two foods), measured weights were within **about 1% (a baking potato) and 4% (a pepper)** of the scales, against **56% and 37% low** for the guess alone. Two foods is a small test, not a benchmark. The angle and framing change the result, and the screen says so. Foods that are flat or piled (rice, a steak) rely more on Spark's thickness estimate and will be less exact.

## The experience

1. **Login**, including a one-tap **Judge Instant Login** for reviewers, then Atlas's introduction.
2. **Home** and **My World**: live dashboards from the real account (calories, macros, weight, sleep, mood, streak, workouts this week).
3. **Food Diary** with day navigation, single foods and saved meals, showing the weight that was logged.
4. **Exercise Log**: every recorded exercise for a day, merged from the platform's strength sets and activities, with day totals.
5. **My Profile**: the user's picture, goals, numbers and recent feed posts with their pictures.
6. **Scan a Plate** (above).
7. **My Experiences** and the **Virtual Gym**: a tracked squat workout. The headset counts reps from head height and shows the count and a depth bar. Between sets there is a rest timer with controls, and generated sounds and controller haptics mark each rep, rejected rep, set and countdown. A mirror shows a body built from the head and controller positions.
5. **Form check:** leaning forward or back, or tilting the head down, shows "Keep straight and crouch down straight", and that movement is **not counted** (a false rep is worse than a missed one). The Quest only tracks the head, so this judges head position, not the back or knees.
8. **Session card** made by Muse Image, with a **Share to feed** button and a spoken offer.

## New versus existing (original contributions)

**Existed before the hackathon, not part of the submission:** the A Better U web app and phone app, and their Supabase database (accounts, food database, exercise library, diary, social feed).

**Built during the hackathon (this repo, from Oct 26):**
- The whole Unity VR app: login, dashboards, environments, gym, mannequin and mirror, squat tracking and form check, HUD, session card, Exercise Log, My Profile.
- The Muse Spark tool-calling agent, the honesty checks, hands-free voice with realtime transcription, and the Muse Voice Transcribe and Muse Image clients.
- The plate-scan pipeline (camera, Spark vision, SAM 3.1 outlines, measurement, matching and confirmation) and the share-to-feed flow.
- Backend work for this project: the `user_injuries` table and its row-level security ([001](docs/migrations/001_user_injuries.sql)), demo saved meals for the judge account ([002](docs/migrations/002_seed_demo_meals.sql)), and a storage bucket for post pictures ([003](docs/migrations/003_atlas_post_images_bucket.sql)). On the web app: an injuries card, an injury history page and a servings calorie fix.

**Pre-kickoff commit, disclosed:** the first commit in this repo is **Oct 25**, the day before kickoff. It was preparation only: planning documents, a deploy script and an interface scaffold with stubs. All working functionality was written from Oct 26. Preparation before kickoff was confirmed as allowed, provided coding had not started.

**How it was built:** with AI coding assistants, directed and tested by the developer on a real Quest 3S. **Claude Code** wrote most of the code. **OpenAI Codex** and **Muse Code** (Meta) did much of the Oct 29 interface and gym polish, the Food Diary redesign and the multi-set workout; that work was committed together in `42a12b4`. Parts of the code were written by the developer, including the rep-detection step. All commits are under the developer's name, and Claude Code's co-author line appears on most of them, so history alone does not separate the tools. [docs/AI_USAGE.md](docs/AI_USAGE.md) says which tool did what, including the one commit where that line is not accurate.

**Muse Code:** from Oct 28, Muse Code (Meta) is also used on the project, both implementing changes and as a second pair of eyes reviewing the repo. Its reviews so far caught submission risks such as a duplicate scene and uncommitted build settings.

**Assets and licences:** no third-party art, audio or models are bundled. The rep, set and countdown sounds are generated in code, and the pictures are made at run time by Muse Image. The app uses the Meta XR SDK, Newtonsoft JSON, Wit.ai for speech output and Supabase for the platform's backend.

More detail in [docs/HACKATHON_COMPLIANCE.md](docs/HACKATHON_COMPLIANCE.md).

## Roadmap (not built yet)

- **Optional posting to Facebook and Instagram** from the same approve-then-post step, once OAuth approval for those networks is in place. Each user would connect their own accounts, and nothing would be posted outside A Better U without an explicit go-ahead each time. Today, posts go to the A Better U social feed only.

## Known limits

- **Speech is only acted on if it starts with "Atlas".** Anything else is transcribed, ignored and not stored by the app.
- Hands-free uses "transcribe, then filter", not on-device wake-word detection. The app holds a realtime connection to Muse Voice Transcribe open and sends it silence; the headset's own sound detector decides when someone is speaking, and only then is microphone audio streamed. The app acts only if the phrase starts with "Atlas" (or follows a reply within 8 seconds). Other phrases are discarded and not kept. If the realtime connection is down, a phrase is uploaded for transcription instead. Listening pauses while the assistant is thinking or speaking, during a squat set and on the login screen.
- A voice turn takes about 5 to 6 seconds on the upload path (speech to text, then Muse Spark, then speech out). The realtime path cuts the speech-to-text wait from about 2 seconds to under half a second; measured numbers are in docs/MODEL_API.md.
- Squat counting and the form check use head tracking only. Thresholds were tuned from logged on-device data (lean limit 15 cm) and may need adjusting for different body types.
- The plate scan measures from a single photo, so its weights depend on the camera angle, the framing and the plate-width setting (set it once to your plate). It is an estimate for the user to check, not a scale.
- The optional plate calibration needs a bank or credit card that stands out from the plate: it finds a dark or white card easily, but a card close to the plate's colour (a blue card on a teal plate was not found) cannot be seen. The screen says so. You can always set the plate width by hand.
- The scan needs the headset camera permission, which Quest asks for the first time you open Scan a Plate.
- The gym is built from simple shapes, and the voice is a US voice.
- The Meta Model API key is bundled in the APK for the demo build. **Do not distribute built APKs publicly**, so no APK is in this repository.

## Run it

You need a Quest 3S in developer mode, Unity 6000.6.7f1 with Android Build Support, and your own Meta Model API key (this repo contains no secrets).

1. Open `Abetteru_VR/` in Unity Hub.
2. Create `Assets/Resources/ModelApiSecrets` (Assets > Create > ABetterU > Model API Secrets). It is git-ignored. Fill in your Meta Model API key, Wit.ai token, Supabase URL and anon key, and the demo account's credentials for Judge Instant Login.
3. Build for Android and sideload: `./scripts/deploy-quest.sh path/to/build.apk`.
4. On the Quest, open your **Library**, use the filter at the top left (the small round button) and choose **Unknown Sources**. The app appears there as "A Better U VR". The first time you use it, allow the **microphone** and, for Scan a Plate, the **headset camera**.

Controls: **hands-free** is the default. Say "Hey Atlas" and then your request, for example "Hey Atlas, log my usual breakfast". After a reply you can keep talking for about 8 seconds without saying the name again. As a fallback, **hold the right trigger** to talk. **Left trigger** starts and ends a squat set; **left Menu** opens the in-gym menu.

## Repo layout

```
Abetteru_VR/Assets/Scripts/Coach/   the app (agent, voice loop, dashboards, gym, HUD)
docs/                               API notes, Supabase notes, compliance notes, SQL migrations
scripts/deploy-quest.sh             sideload an APK to the Quest over adb
PLAN.md                             the original build plan
```

The two PDFs in the root are the original application and build plan.
