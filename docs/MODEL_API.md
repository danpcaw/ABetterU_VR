# Meta Model API: verified facts (Oct 26, 2026)

Dashboard: dev.meta.ai (API keys, Chat playground, Billing, Usage, Models, Audio, Segment).
**Never commit the key.** Locally it lives in `~/.zshrc` as `MODEL_API_KEY`.

## Gotchas hit during setup

- API keys returned `401 invalid_api_key` until the **team's billing was verified** (card on file). The playground Chat worked before the key did.
- A key created while billing was blocked stayed rejected. **Create a fresh key after billing clears.**
- Muse Code is a separate paid subscription (~£3.49/month). API usage is billed to the card on file.

## Endpoint

```
POST https://api.meta.ai/v1/responses
Authorization: Bearer $MODEL_API_KEY
```

Request body (Responses style):

```json
{
  "model": "muse-spark-1.3",
  "instructions": "system prompt here",
  "input": [{"role": "user", "content": [{"type": "input_text", "text": "..."}]}],
  "reasoning": {"effort": "low"},
  "max_output_tokens": 400,
  "stream": false
}
```

Response: `output[]` has a `reasoning` item, then a `message` item; the text is at
`output[i].content[0].text` (`type: output_text`).

## Models (GET /v1/models)

`muse-spark-1.3`, `muse-spark-1.2`, `muse-spark-1.1` (plus `-contributor` variants),
`muse-wonder-1.0`, `muse-image-1.0`, `muse-voice-transcribe-1.0` (speech-to-text),
`sam-3.1` (segmentation). No text-to-speech model listed: use the headset/Android TTS unless the Audio page shows otherwise.

## Latency and the voice loop

- Tiny call on `muse-wonder-1.0` at default reasoning ("max"): 2.0 s.
- `muse-spark-1.3` coaching reply: 1.4 to 2.5 s at `low`/`minimal` effort.
- **Reasoning tokens count against `max_output_tokens`.** At 120 tokens the whole budget went on reasoning and the reply was empty. Keep the cap at 400+.
- To feel live: stream the response and speak the first sentence as soon as it arrives, and test the lowest reasoning setting that still gives good coaching.

## Tool calling (verified Oct 26)

`muse-spark-1.3` supports function tools on the Responses API. Tools are declared as
`{"type":"function","name":...,"description":...,"parameters":{JSON schema}}` in `tools[]`.
One user sentence ("two eggs on toast, my left knee is sore, build me a leg workout") produced
**two tool calls in a single turn** plus a short spoken line:

- `log_food {"description":"two eggs on toast","meal":"breakfast"}`
- `build_workout {"avoid_areas":["left knee"],"focus":"legs"}`

In `output[]` these appear as items with `type: "function_call"` (`name`, `arguments` JSON string).
Round trip was ~2.9 s at `low` reasoning. After running a tool, send its result back as a
`function_call_output` input item and let the model speak the confirmation (TODO: verify shape).

## Speech-to-text (verified Oct 26)

`POST https://api.meta.ai/v1/asr/transcribe` (HTTP file upload), `Authorization: Bearer`, multipart:
- `request` (application/json): `{"mode":"PUSH_TO_TALK","model":"muse-voice-transcribe-1.0","audioEncoding":"WAV"}`
- `audio` (audio/wav): RIFF/WAVE, mono, 16-bit PCM, 16 kHz or 24 kHz. Max 32 MB / 10 min.

Response: `{"sessionId","transcript","audioDurationMs","turns":[]}` (`turns` only in ENDPOINTING/DIARIZATION).
Test: a 3.8 s clip transcribed correctly in ~2.1 s.

Realtime option: `wss://api.meta.ai/v1/asr/realtime` (auth in the first JSON frame, not the header;
raw PCM16 binary frames; `mode: ENDPOINTING` gives automatic end-of-speech). Use it if HTTP latency is too high.
No text-to-speech in the API: use Android TTS on the headset.

### Gotcha: Unity's built-in multipart form is rejected

`UnityWebRequest.Post(url, List<IMultipartFormSection>)` returned `400 invalid_request_error: "Malformed multipart body"`
on Quest, while curl and a hand-built body (boundary, `\r\n` line endings, `Content-Type` per part) returned 200.
`MetaTranscribeClient` builds the body itself. Also: the Quest mic gave a native 16 kHz mono clip, so no resampling was
needed (the resampler is kept as a safety net).

### Reasoning effort for the coach (verified Oct 26, on-device)

`muse-spark-1.3`, ~20-word spoken reply, same prompt:
- `low` + 400 tokens: **no reply** (398 reasoning tokens, `status: incomplete`, `incomplete_details.reason: max_output_tokens`), 3.1 s.
- `low` + 1500 tokens: replied, but used 440 reasoning tokens, 3.5 s.
- **`minimal` + 600-800 tokens: replied every time, 1.6-2.7 s.** This is the default (`replyEffort`, `replyMaxTokens`).
- `none` is rejected (`reasoning_effort 'none' is not supported`). Allowed: minimal, low, medium, high, xhigh, max.

## Text-to-speech (Wit.ai, verified Oct 26)

Meta's Model API has no TTS, and the Quest's own engine logs `TTS disabled by MobileConfig` (switched off remotely),
so spoken replies use Wit.ai (Meta-owned, free). Server token lives in `$WIT_SERVER_TOKEN` locally and in the
git-ignored `ModelApiSecrets` asset for builds.

- `GET https://api.wit.ai/voices?v=20240304` lists voices. en_US: Rebecca, Charlie, Cooper, Remi, Cody, Cam, Connor... (names are `wit$Name`);
  en_GB has only character voices (Pirate, British Butler...).
- `POST https://api.wit.ai/synthesize?v=20240304`, `Authorization: Bearer`, JSON `{"q","voice","style"}`,
  `Accept: audio/raw` (24 kHz 16-bit mono PCM) or `audio/wav`. First byte ~0.7 s, a short sentence ~1.3-1.7 s total.
- Client splits replies into sentences, fetches them in parallel and plays in order.

## Tool calling round trip (verified Oct 27)

1. POST `/v1/responses` with `tools:[{type:"function",name,description,parameters}]`.
2. `output[]` may hold a `message` with `phase:"commentary"` (a short spoken filler such as "Logging your breakfast, one sec.") plus a `function_call` item `{call_id,name,arguments(JSON string)}`.
3. Run the tool, then POST again with `input` = original input + the `function_call` item (`type,call_id,name,arguments`) + `{type:"function_call_output",call_id,output:"<JSON string>"}`.
4. The final `message` item is the reply. Same `minimal` effort and 800-token cap work.


## Realtime transcription (tested Oct 28, from the Mac, not yet in the app)

Official reference: https://dev.meta.ai/docs/api-reference/voice/realtime and https://dev.meta.ai/docs/speech-to-text

- `wss://api.meta.ai/v1/asr/realtime?sessionId=<any id>`. The Authorization header is ignored. The **first text frame** is the handshake (within 10 s):
  `{"authorization":{"accessToken":"Bearer <key>"},"audioEncoding":"PCM_24KHZ","model":"muse-voice-transcribe-1.0","mode":"ENDPOINTING","languageBias":["English"],"keywords":["Atlas","A Better U"]}`.
  The ack is `{"sessionId":...}` (the only frame without a `type`).
- Then raw 16-bit mono PCM as **binary frames, paced at real time** (the probe uses 80 ms frames). Docs: close code 1008 for "backlog or below-real-time ingress", so do not burst audio or stall. End with `{"type":"endStream"}` and read until the server closes with 1000.
- `ENDPOINTING` mode sends `speechStart`, repeated partial `transcript` (final:false), `speechEnd`, then `speechComplete` with the cleaned transcript (key state on `turnId`). Also `audioProgress` events (ignore). Partials carry no turnId.
- **Measured:** phrase "Hey Atlas, log my usual breakfast" (3.3 s of speech). Partial "Hey" at 0.8 s, "Hey Atlas," at 1.5 s, `speechComplete` **0.31 s after the speech ended**. The HTTP upload takes 2.0 to 2.5 s after speech ends, so realtime saves about 2 s per turn.
- Node's built-in `WebSocket` failed (error 1006, no detail); the `ws` npm package connected fine. In Unity use `System.Net.WebSockets.ClientWebSocket` (untested on Quest).
- Probe script: `scripts/realtime-asr-probe.mjs`.

### Unity integration (built Oct 29, `RealtimeTranscriber.cs`, not yet run on a headset)

Mac measurements that shaped the design:
- **Handshake takes 1.2 to 2.4 s** (connect plus ack). A session per phrase would lose most of the saving on short phrases, so the app keeps one session **warm**.
- **An idle session is killed**: no audio for 15 s gave `1008 Ingress below real-time` (3 s and 6 s were fine). The docs recommend sending PCM silence in quiet periods, and 30 s of streamed zeros then a phrase worked, final transcript 91 ms after speech ended.
- **Bursts are accepted**: 640 ms and 1280 ms of audio sent at once at the start gave no 1008. So the local detector's pre-roll can go out as a burst, then live audio.
- **One session handles several phrases** (turn 0, turn 1, each with `speechStart` and `speechComplete`).
- `PCM_16KHZ` with `languageBias: ["en-GB"]` works, so the Quest's native 16 kHz microphone needs no resampling.
- Pricing: **$0.18 per hour** of audio, the same as file upload; sessions last up to 60 minutes (the app reconnects at 55); 256 concurrent streams per tenant. The silent stream is about $0.003 a minute.

How the app uses it:
1. While hands-free is allowed (signed in, not in a set or the gym menu) a background thread keeps the session open and sends real-time silence. **No microphone audio leaves the headset until the local detector hears speech.**
2. On speech start, the 0.55 s pre-roll goes out as a burst (with a gain lift, since the Quest microphone is quiet), then live 50 ms chunks.
3. `speechComplete` is handled like an uploaded transcript: wake name, follow-up window, command. About 0.1 to 0.3 s after you stop, against 2 to 2.5 s for an upload.
4. The upload path stays as the fallback: if the session is not connected when a phrase starts (first seconds after login, a dropped connection), that phrase is uploaded as before. Whichever path takes a phrase first claims it.
5. Switch off with `realtimeAsr` in the `ModelApiSecrets` asset.

Still to measure on the Quest: handshake time, whether `ClientWebSocket` behaves under IL2CPP, and real end-to-end turn time. Logs are prefixed `[Realtime]` (connected, complete) and `[HandsFree] ... via realtime|upload`.
