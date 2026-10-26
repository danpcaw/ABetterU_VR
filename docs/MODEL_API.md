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
