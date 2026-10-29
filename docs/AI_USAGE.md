# AI tools used on this project

This project was built with AI coding assistants, directed and tested by the developer on a real Quest 3S. This page says which tool did what, and where the record is, so judges can tell what was built how. It is based on the tools' own session records and the developer's account.

| Tool | What it did | Where to see it |
|---|---|---|
| **Claude Code** (Anthropic) | Most of the code: the Muse Spark agent and honesty checks, the voice loop and realtime transcription, the first dashboards and gym, plus (from Oct 29 evening) the Exercise Log, My Profile, share-to-feed, the plate-scan pipeline (camera, Spark vision, SAM 3.1, measurement, matching), the mannequin and Atlas-bar redesign, workout-control fixes, and the README and docs. | Commits it helped with end with a `Co-Authored-By: Claude` line, with the one exception below. |
| **Muse Code** (Meta, Muse Spark) | Reviewed the repo from Oct 28 (catching submission risks). On Oct 29 it added the nutrition donut charts and the extra My World cards drawn from Supabase data (weight, sleep, mood, streak), and investigated pale card overlays ("Card Overlay Visual Issue" session). | Its work was uncommitted until it was included in commit `42a12b4`. |
| **OpenAI Codex** | On Oct 29, in one session: editor tools to capture and preview screens (`Assets/Editor/WellnessUiCapture.cs`, `WellnessUiScreens.cs`); a fix for washed-out panels (texture mip levels in `VRArt.cs`); mouse selection in the editor; My World layout alignment; the **Food Diary** redesign (meal tabs, food thumbnails, per-entry macros, delete with confirmation and totals rings); the compact Atlas bar; Quit App; the **gym studio** makeover (oak floor, timber panels, lighting, wall display, branding); the vertical squat depth gauge; and the **multi-set squat workout** (`SquatWorkoutSession.cs`, `MeasuredSetWriter.cs`, `VoiceLoopTest.Workout.cs`, the `start_squat_session` and `control_squat_session` tools). Its session ended at a usage limit before that last piece was finished and checked. | Codex's session log, available on request. The files above first appear in commit `42a12b4`. |

## Honest notes on the record

- **Commit `42a12b4` ("UI polish and premium gym", 26 files) is Codex and Muse Code's work, not Claude Code's.** Claude Code committed it, because it was sitting uncommitted in the working folder, and its message carries a Claude co-author line. That line is not accurate for this commit. It is left as it is rather than rewriting the public history. Claude Code did later change several of those files (for example the workout controls, mannequin and Atlas bar).
- **All commits are made under the developer's name.** Only Claude Code's work carries a marker, so git history alone cannot separate the three tools. This page exists for that reason.
- **Parts were written by the developer**, including the squat rep-detection step.
- **Session records** are stored locally on the developer's machine (Muse Code: `~/.local/share/muse`; Codex: `~/.codex`) and exported copies can be provided on request.
- **No third-party art, audio or models are bundled.** Sounds are generated in code and pictures are made at run time by Muse Image.
