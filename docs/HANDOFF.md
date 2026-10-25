# Division of labour

Work is relayed manually between the two agents. Give each **specific, scoped** instructions
("wire this script to this scene object"), not open-ended asks. One agent is primary per task.

| Agent | Owns |
|---|---|
| Claude Code | Architecture, Muse Spark / Meta Model API integration, A Better U account sync |
| Muse Code | Unity scene setup, C# wiring inside Unity, Quest CLI builds and deployment |

## Handoff template (Claude Code to Muse Code)

```
Task: <one sentence>
Files: <paths in unity-scaffold/ or Assets/>
Scene objects involved: <names>
Done when: <observable result on the headset>
Do not change: <anything out of scope>
```

## Handoff template (Muse Code to Claude Code)

```
Problem: <what failed>
Where: <script / scene / build step>
Log or error: <paste>
What was tried: <short list>
```
