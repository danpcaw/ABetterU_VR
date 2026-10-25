# Build Plan

**Track:** Indie (solo). Judged on creativity, technical ambition and **whether it works**.
**Theme:** Build for Your World.

## Schedule (confirmed from the event page)

- Event window: **Mon Oct 26, 12:00 PM ET** to **Wed Nov 4, 9:00 PM ET** (page says EST; if it is really EDT, kickoff is 16:00 GMT, so confirm on the day).
- Hard deadline: **Nov 5, ~01:00-02:00 GMT**. **Our target: submitted by 22:00 GMT Nov 4**, leaving a 3+ hour safety buffer.
- Kickoff livestream Oct 26. Workshops and Discord office hours Oct 27 to Nov 4.
- Judging Nov 4-8. Winners Nov 9.
- Personal start: **18:30 GMT Mon Oct 26** (about 2.5 hours lost). Make it back with the weekend (Sat Oct 31, Sun Nov 1) and by doing prep before then.

## The demo (one continuous ~2-minute story)

1. Headset on, coach greets the user by name (profile hard-coded or pre-authed, **no login screen**).
2. User says "I had eggs on toast for breakfast". A food card appears: logged.
3. User says "My knee is sore, build me a workout". Muse Spark returns a knee-friendly 3-exercise plan.
4. User enters the virtual gym and does squats. Coach counts reps and comments live.
5. Set lands in the user's A Better U account (or is shown mocked, say so).

One voice loop, three intents: **food**, **workout**, **coaching**. Everything not in this story is cut.

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
| 1 (short, from 18:30) | Mon Oct 26 | Unity project + Quest build pipeline | Bare scene runs on the headset |
| 2 | Tue Oct 27 | Mic permission, audio out, first STT/TTS test | Hear a TTS line in the headset |
| 3 | Wed Oct 28 | **Voice loop**: mic to STT to Muse Spark | Spoken question returns a text answer |
| 4 | Thu Oct 29 | Voice loop: TTS out, latency tuning | Round trip feels live |
| 5 | Fri Oct 30 | Intents: food log + injury-aware workout | "Knee is sore" returns a safe plan, food card shows |
| 6 | Sat Oct 31 (long day) | Gym scene + squat rep counting + live coaching | Reps counted, coach comments mid-set |
| 7 | Sun Nov 1 (long day) | A Better U sync (food, workout, set) | Set appears in account, or mocked |
| 8 | Mon Nov 2 | Polish, "your A Better U today" panel, bug bash | Full story runs start to finish |
| 9 | Tue Nov 3 | Demo recording | Clean take captured |
| 10 | Wed Nov 4 | Pitch video, edit, **submit by 22:00 GMT** | Submitted with buffer |

## Cut order (if evenings run short)

1. Badge unlock / Muse Image flex
2. Dashboard panel (use a static card)
3. A Better U live sync (show as mocked/planned in narration, say so honestly)
4. Voice food logging (keep workout generation)
5. Form-cue sophistication (simple rep counting only)

**Never cut:** the voice loop (eves 3-4) and the demo recording (eves 9-10).

## Risks to watch

- Model API access arriving late (use fallback model).
- Voice latency. Log the round-trip time every turn (already in `VoiceLoopController`).
- Credits ($150): cap replies (~80 tokens), keep prompts short, check spend after each session.
- First Unity/Quest project: expect build-pipeline surprises on eve 1. Ask Muse Code for the exact fix, not open-ended help.
