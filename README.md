# A Better U VR Companion

Meta Global AI Developer Hackathon entry (build window: Oct 26 to Nov 4, 2026).

A **Muse Spark-powered AI coach inside a Meta Quest 3S**. It watches your reps, talks you
through the set by voice in real time, and logs the finished set to your A Better U account.

**Hackathon scope:** one bodyweight exercise (squats) in a solo VR scene, with full
voice-to-voice coaching and set logging. The shared, live-coached virtual gym is the roadmap,
not part of the demo.

## Stack

| Layer | Choice |
|---|---|
| Engine | Unity (Android/Quest build module) |
| Device | Meta Quest 3S |
| AI coach | Muse Spark via Meta Model API |
| Voice in | On-device speech-to-text |
| Voice out | Text-to-speech |
| Data | A Better U API |

## Voice loop (the core risk, built first)

`mic -> speech-to-text -> prompt (rep count + pace context) -> Muse Spark -> text-to-speech -> headset audio`

Latency must stay low enough that coaching feels live.

## Repo layout

```
PLAN.md                  10-evening schedule, prep checklist, cut order
docs/HANDOFF.md          How work is split between Claude Code and Muse Code
unity-scaffold/Scripts/  C# skeleton to copy into the Unity project's Assets/Scripts
scripts/deploy-quest.sh  Sideload the latest APK to the Quest over adb
```

The two PDFs in the root are the original application and build plan.

## Setup

1. Install Unity with the Android Build Support module.
2. Put the Quest 3S in developer mode and confirm `adb devices` lists it.
3. Create the Unity project in `unity/` (Unity Hub), then copy `unity-scaffold/Scripts/` into `unity/Assets/Scripts/`.
4. Create `ModelApiSecrets` (see `ModelApiConfig.cs`) with your Meta Model API key. It is git-ignored.
5. Build to APK, then `./scripts/deploy-quest.sh path/to/build.apk`.
