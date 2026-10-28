# Hackathon compliance notes

Source: the Participant Guide for the Meta Global AI Developer Hackathon (sections 1, 4 and 8), re-read on Oct 28.
Track: **Indie Developer** (solo). Theme: **Build for Your World**.

## What the rules actually require

| Rule | Wording (short) | Our status |
|---|---|---|
| Meta models | "Your project must incorporate Meta's models" (Muse Spark series, Muse Wonder, Muse Image, Muse Voice Transcribe, SAM 3.1 via the Meta Model API; open weights Muse Glimmer and Spark Open; Muse Code to "code faster") | **Met inside the product:** Muse Spark (tool-calling agent) and Muse Voice Transcribe, both live in the headset. |
| Stage 1 (pass/fail) | Fits the theme and "reasonably used Meta Model API and/or Meta's open-weight models to build the project" | Met. Not a spend threshold; there is no minimum credit usage. |
| Public repo | Repositories must be public | To do: create and push (no secrets; `ModelApiSecrets` is git-ignored). |
| Demo | Must "only highlight the specific features, code, and functionality that you built during the Hackathon"; failing to identify original contributions = **immediate disqualification** | See "New vs existing" below. This is the rule to take most seriously. |
| New work only | What you present "must have been built during the event" (startups may build on existing products, but must label what is new) | We entered as **Indie, not Startup**, so the safest reading is: the VR app and agent are the submission, and the existing platform is a backend we integrate with. Clarify with organisers. |
| Banned projects | Disqualified if they violate legal, ethical or platform policies, or use code, data or assets you lack rights to | Fine: own platform, own data, own logo. Check the rights on any third-party media before using it (e.g. a generated video). |
| Anti-projects | Not a disqualifier by itself ("generic or violate platform policies"); the list includes AI nutrition coach, sports coach, medical advice | Positioned as an agent acting on a real platform. No medical advice; injury notes only filter exercises. |

### Do we have to build *with* Meta tools, or use them *inside* the experience?
The guide says the project must **incorporate** Meta's models, and lists Muse Code as an optional way to "code faster".
So the requirement is about the product (and Stage 1 mentions using them "to build the project"). We satisfy both
readings of the product requirement. Using Muse Code for coding is optional. If it is used, say so; do not claim it wrote
code it did not. Claude Code was used as a coding assistant on this project and should be disclosed honestly.

### Judging rubric (Stage 2, 0 to 10 each)
Impact 30 · Use of Meta AI tooling 25 (creative, beyond a basic integration) · Technical depth 20 · Demo 25.

## New vs existing (to state clearly in the video, README and submission)

**Existing before the hackathon** (not part of the submission):
- The A Better U web app and phone app, and their Supabase database (food database, exercise library, accounts, diary).

**Built during the hackathon** (this repo, Oct 26 onwards):
- The whole Unity VR app: login, dashboards, retreat environment, gym, mannequin, real mirror, squat tracking.
- Muse Spark tool-calling agent (search_foods, log_food, report_injury, build_workout, log_set) with honesty checks.
- Muse Voice Transcribe client, voice loop, Wit.ai speech output.
- Live dashboards and food diary reading the real account.
- Backend changes made for this project: `user_injuries` table and RLS (docs/migrations/001_user_injuries.sql);
  web app injuries card, injury history page and servings calorie fix (web repo commits 13769f13, e17eb74f, 3be96973; Oct 27).

**Honest disclosure of a pre-kickoff commit:** this repo's first commit is **Oct 25**, the day before kickoff. It contained
planning docs, a deploy script and an interface scaffold (`VoiceLoopController.cs` interfaces, an empty rep counter stub).
All working functionality was written after kickoff. Say so in the README rather than let a judge find it.

**Checked by Danny on Oct 28:** the rules allow preparation and setup before Oct 26, as long as coding does not start
before Oct 26. The Oct 25 commit is prep (docs, deploy script, interface scaffold with stubs), so it is within the rules.
Keep the one-line disclosure anyway; it costs nothing. Do not describe the scaffold as "no code": it holds interfaces and stubs.

**Confirmed by Danny on Oct 28 (Discord and Meta AI documentation):**
- Building with Claude Code, Muse Code or Codex is all fine, **as long as the project uses at least 3 Meta AI tools within the platform.**
- Judges will open the app and use **Judge Instant Login**, so the button stays and appears in the demo video. Nothing in the rules asks for a pre-authenticated or login-free build.

### Meta tool tally (the "at least 3" requirement)
| # | Meta tool | Status |
|---|---|---|
| 1 | Muse Spark (tool-calling agent) | Live in the headset |
| 2 | Muse Voice Transcribe | Live in the headset |
| 3 | Muse Image (session card after a squat set) | Endpoint verified Oct 28 ($0.01 per image, about 8 s). Built into the set summary and **seen working on the headset (Oct 28)**. Artwork only; the rep number is drawn by the app from the headset count. |

## Open questions for the organisers (Discord #questions)
1. Indie entry building a VR companion for my own existing production platform. The VR app, agent and a new database table are new; the web app, phone app and databases already existed. Is integrating with my own existing backend acceptable if the new work is clearly labelled?
2. Is it fine to use non-Meta tools (Supabase, a Meta-owned speech service, an AI coding assistant) as long as Meta models are core to the product?

## To do
- [x] Pre-kickoff prep is allowed (checked Oct 28).
- [ ] Send question 1 and 2 above in Discord #questions (still worth it: the existing-backend point is separate from the prep point).
- [x] README section "New versus existing" written (Oct 28).
- [ ] Put an on-screen "Existing / New" label in the demo video.
- [ ] Create the public GitHub repo, push, check it works logged out.
- [x] Third Meta tool: Muse Image session card built (verify on headset, then confirm the count with the organisers). Optional fourth: SAM 3.1 if time allows around Nov 2.
