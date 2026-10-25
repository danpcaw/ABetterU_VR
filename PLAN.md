# Build Plan

Window: **Oct 26 to Nov 4, 2026** (starts 17:00 GMT). About 2-3 hours per evening, 10 evenings.
Applications close Oct 25. Demo submitted Nov 4.

## Prep (before 17:00 GMT Oct 26)

- [ ] Meta Model API credentials issued and **one real Muse Spark call succeeds**
- [ ] Quest 3S in developer mode, `adb devices` shows it, a test build sideloads
- [ ] Unity installed with Android/Quest build module
- [ ] 1-2 hours on a Quest "hello world" tutorial (scenes, GameObjects, C# scripts)
- [ ] **Decide squat detection:** headset/controller motion vs passthrough camera + SAM
- [ ] Scaffold boilerplate that needs no hardware (done in `unity-scaffold/`)

## Evenings

| Eve | Date | Goal | Done when |
|---|---|---|---|
| 1 | Oct 26 | Unity project + Quest build pipeline | Bare scene runs on the headset |
| 2 | Oct 27 | Scene polish, mic permission, audio out | Can hear TTS test line in headset |
| 3 | Oct 28 | **Voice loop**: mic to STT to Muse Spark | Spoken question returns a text answer |
| 4 | Oct 29 | Voice loop: TTS out, latency tuning | End-to-end voice round trip feels live |
| 5 | Oct 30 | Workout scene + rep counting | Squats counted accurately |
| 6 | Oct 31 | Live coaching on pace/form/rep count | Coach comments mid-set |
| 7 | Nov 1 | A Better U sync | Finished set appears in account (or mocked) |
| 8 | Nov 2 | Badge + Muse Image flex | Cut first if behind |
| 9 | Nov 3 | Demo recording | Clean take captured |
| 10 | Nov 4 | Pitch video + submit | Submitted |

## Cut order (if evenings run short)

1. Badge unlock + Muse Image auto-flex (eve 8)
2. A Better U sync (eve 7): show as mocked/planned in narration
3. Form-cue sophistication: ship simple rep counting

**Never cut:** the voice loop (eves 3-4) and the demo recording (eves 9-10).

## Judging criteria to design toward

Project clarity, feasibility, theme relevance (Muse Spark *is* the coach), technical skill
(real-time voice-to-voice in VR, synced to a production platform). Be honest about scope.
