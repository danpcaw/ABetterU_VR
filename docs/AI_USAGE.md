# AI tools used on this project

This project was built with AI coding assistants, directed and tested by the developer on a real Quest 3S. This page says which tools were used and where the record of that work is, so judges can tell what was built how.

| Tool | Used for | Where to see it |
|---|---|---|
| **Claude Code** (Anthropic) | Most of the code: the agent, voice loop, realtime transcription, dashboards, gym, plate scan, sharing, README and docs. Directed and tested by the developer. | Every commit it helped with ends with a `Co-Authored-By: Claude` line (`git log`). |
| **Muse Code** (Meta, Muse Spark) | A second pair of eyes reviewing the repo from Oct 28, and UI work on Oct 29: a session called "Card Overlay Visual Issue" investigated pale overlays on the dashboard cards and washed-out image backgrounds in the card styling code (`VRGlass.cs`, `VRArt.cs`). | The UI polish landed in commit `42a12b4` ("UI polish and premium gym"). Muse Code reviews are mentioned in the README. |
| **OpenAI Codex** | Contributed some changes on Oct 29. | Committed under the developer's name. |

## Honest notes on the record

- **All commits are made under the developer's name.** Only Claude Code's work carries a marker (the co-author line). Work done with Muse Code or Codex was committed by the developer without one, so git history alone cannot separate it. That is why this page exists.
- **Parts were written by the developer**, including the squat rep-detection step.
- **Nothing here is claimed beyond what the tools' own session records and the developer's account support.** Muse Code session records are stored locally on the developer's machine (`~/.local/share/muse`) and an exported record can be provided on request.
- **No third-party art, audio or models are bundled.** Sounds are generated in code and pictures are made at run time by Muse Image.
