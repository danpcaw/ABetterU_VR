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
