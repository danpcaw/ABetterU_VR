# Build Plan

**Track:** Indie (solo). Judged on creativity, technical ambition and **whether it works**.
**Theme:** Build for Your World.

## Schedule (confirmed from the event page)

- Event window: **Mon Oct 26, 12:00 PM ET** to **Wed Nov 4, 9:00 PM ET** (page says EST; if it is really EDT, kickoff is 16:00 GMT, so confirm on the day).
- Hard deadline: **Nov 5, ~01:00-02:00 GMT**. **Our target: submitted by 22:00 GMT Nov 4**, leaving a 3+ hour safety buffer.
- Kickoff livestream Oct 26. Workshops and Discord office hours Oct 27 to Nov 4.
- Judging Nov 5-8. Closing ceremony Mon Nov 9, 12:00 PM ET (top 10 live). Community vote winner Nov 16.
- Daily Meta office hours in Discord #office-hours, 10-11 AM ET on weekdays (text only). Sessions Oct 27, 28, 30 at 12-1 PM ET.
- Submit at cerebralvalley.ai/e/meta-ai-developer-hackathon/hackathon/submit. Late submissions are not considered.

## Official rules that affect us (from the Participant Guide)

- **Repo must be PUBLIC.** Never commit keys (`.gitignore` covers `.env` and `ModelApiSecrets.asset`). Create the GitHub repo early and push often.
- **Demo video: 3 minutes**, showing only what we built during the hackathon. It is a primary judging artifact.
- **New work only.** Judges must clearly see what was built in the event. Startups may build on existing products; we must label what is new vs the existing A Better U platform.
- **Stage 1 is pass/fail:** fits a theme and genuinely uses Meta Model API / open-weight models.
- **Credits ($150) must be claimed via a Google form** (link in the Participant Guide, section 5) by Nov 4, 9 PM ET. A card must be on file. Set a Spend Limit alert on the Billing page.
- Everyone who worked on the project is listed on the submission page. Test that the repo and video links work logged out.
- **Anti-projects list** includes "AI Nutrition Coach", "Sports analyzers or coaches" and "AI medical advice". See the risk note below.

### Risk: the anti-project list

Our idea touches three of those. Positioning must lead with what is novel: a **voice-driven agent in VR that takes real actions on a production platform** (tool calls into A Better U: log food, build a program from the existing exercise library, record a set). Avoid presenting it as a generic nutrition/fitness coach. Injury input should only **filter exercises from the existing library**, never diagnose or give medical advice, with a visible disclaimer. Ask the organisers in Discord #questions if unsure.
- Personal start: **18:30 GMT Mon Oct 26** (about 2.5 hours lost). Make it back with the weekend (Sat Oct 31, Sun Nov 1) and by doing prep before then.

## The demo (one continuous story, fitted into the 3-minute video)

1. Headset on, coach greets the user by name (profile hard-coded or pre-authed, **no login screen**).
2. User says "I had eggs on toast for breakfast". A food card appears: logged.
3. User says "My knee is sore, build me a workout". Muse Spark returns a knee-friendly 3-exercise plan.
4. User enters the virtual gym and does squats. Coach counts reps and comments live.
5. Set lands in the user's A Better U account (or is shown mocked, say so).

One voice loop, three intents: **food**, **workout**, **coaching**. Everything not in this story is cut.

## How we're scored (Stage 2, 0-10 per category)

| Category | Weight | What judges ask | Our answer |
|---|---|---|---|
| Impact | 30% | Real-world potential, who benefits, could people use it? | Real existing platform and users; hands-free logging and training in VR |
| Use of Meta AI tooling | 25% | Creative, beyond a basic integration, surprising capabilities | Spark as a tool-calling agent + Voice Transcribe, plus one more model if cheap (Muse Image) |
| Technical depth & execution | 20% | Sound, refined engineering | Clean voice loop, latency logged and tuned, real API sync |
| Demo | 25% | Working, impressive, holds up live, video is cool | One continuous, rehearsed story; record several takes |

Indie prizes (top 5 only): 1st $150K, 2nd $100K, 3rd $75K, 4th $50K, 5th $25K. Top 5 per track
go to a final round where judges watch the demo videos live.

### Bonus prizes worth aiming at (extra to the track prizes)

- **Best Agentic Use-Case, $5K:** autonomy, effectiveness, multi-step workflows. Fits us if Spark
  **calls tools** (`log_food`, `build_workout`, `log_set`) instead of just chatting. Verify tool calling on the Responses API.
- **Best Creative Build, $5K:** built with Muse Image or Muse Wonder. A cheap Muse Image touch (e.g. generated session card) could qualify.
- **Most Viral Project, $20K:** post the demo on X and submit the link through the platform; judged on engagement until Nov 8 EOD.
- **Community Vote, $10K:** public gallery, one vote per verified account, winner Nov 16.
- Best Build on Meta Open Weight Models ($10K) needs Muse Glimmer / Spark Open: not our path.

## Prep (before 18:30 GMT Oct 26)

- [ ] Unity 6 LTS installed with Android Build Support (SDK, NDK, OpenJDK)
- [ ] Quest 3S in Developer Mode, USB-C data cable, `adb devices` lists it, USB debugging accepted
- [ ] Read the Event Waiver and accept Official Rules when posted; join the Discord
- [ ] Watch kickoff (or the recording) for **Meta Model API access and credits instructions**
- [ ] One real Muse Spark call succeeds (key in an environment variable, never in chat or git)
- [ ] Short Quest hello world (Unity "Meta Quest XR" template, cube visible in the headset)
- [ ] Decide squat detection: **default = headset height** (RepCounter.cs). SAM 3.1 form checks are a stretch goal only.
- [ ] Fallback: if the Model API key is late, point `ModelApiConfig` at another chat model so the voice loop is not blocked

## Evenings

| Eve | Date | Goal | Done when |
|---|---|---|---|
| 1 (short, from 18:30) | Mon Oct 26 | Unity project + Quest build pipeline | **DONE 21:50 GMT**: scene with floor and cube runs on the Quest 3S |
| 2 | Tue Oct 27 | Mic permission, audio out, first STT/TTS test | **DONE Oct 26**: mic, transcribe, Spark and spoken reply (Wit.ai TTS) all work on the Quest |
| 3 | Wed Oct 28 | **Voice loop** works; now make Spark an **agent with tools** (`log_food`, `build_workout`, `log_set`) | One sentence triggers real tool calls and the coach confirms them aloud |
| 4 | Thu Oct 29 | Latency: realtime ASR WebSocket, stream Spark, speak first sentence early | Round trip under ~3 s (now ~6 s: ASR 3.1 + Spark 1.6 + TTS 1-2) |
| 5 | Fri Oct 30 | Intents: food log + injury-aware workout | "Knee is sore" returns a safe plan, food card shows |
| 6 | Sat Oct 31 (long day) | Gym scene + squat rep counting + live coaching | Reps counted, coach comments mid-set |
| 7 | Sun Nov 1 (long day) | A Better U sync (food, workout, set) | Set appears in account, or mocked |
| 8 | Mon Nov 2 | Polish, "your A Better U today" panel, bug bash | Full story runs start to finish |
| 9 | Tue Nov 3 | Demo recording | Clean take captured |
| 10 | Wed Nov 4 | Pitch video, edit, **submit by 22:00 GMT** | Submitted with buffer |

## Stretch: "where this is going" (only after gym + squats + sync work)

- **Roadmap wall** in the gym scene: three glowing panels labelled ROADMAP (Virtual Workouts, Social Posts, Coach Studio). Static quads with text, ~20 min.
- **Voice teaser:** "what's coming next?" answered from a short roadmap paragraph in the coach prompt.
- **Make one real:** Social Post Creator. After a set, the coach writes a caption from the real logged data, plus a Muse Image card (targets Best Creative Build, $5K).
- **Honesty rule:** teasers are always labelled ROADMAP on screen and in the voiceover. The submission states: existing = A Better U web app; new = this VR voice agent. The web app is our credibility edge, not something to hide.

## Cut order (if evenings run short)

0. Roadmap wall / teasers (above)
1. Badge unlock / Muse Image flex
2. Dashboard panel (use a static card)
3. A Better U live sync (show as mocked/planned in narration, say so honestly)
4. Voice food logging (keep workout generation)
5. Form-cue sophistication (simple rep counting only)

**Never cut:** the voice loop (eves 3-4) and the demo recording (eves 9-10).

## Known quirks (for UI polish, eve 8)

- Left X and Y never reach the app on our Quest build (verified Oct 27); the **left trigger** starts and ends a squat set. Any on-screen help text or the demo script must say "left trigger", not X/Y.
- Rep counts at the top of the squat (down then up); a tick and buzz fire at depth. `downFraction` 0.20 and `upFraction` 0.08 are Inspector-tunable on RepCounter.
- Social cost: Wit.ai voice is US-only; greeting uses the profile name ("Judge" for the judge account).

## Risks to watch

- Model API access arriving late (use fallback model).
- Voice latency. Log the round-trip time every turn (already in `VoiceLoopController`).
- Credits ($150): cap replies (~80 tokens), keep prompts short, check spend after each session.
- First Unity/Quest project: expect build-pipeline surprises on eve 1. Ask Muse Code for the exact fix, not open-ended help.
