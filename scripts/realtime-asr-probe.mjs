// Realtime transcription probe (Oct 28). Speaks a phrase with Wit TTS, streams it to Muse Voice Transcribe at real-time pace and
// prints every server event with timing, so the latency of the realtime endpoint can be compared with the HTTP upload.
//   npm i ws   (Node's built-in WebSocket failed to connect; the "ws" package works)
//   MODEL_API_KEY=... WIT_SERVER_TOKEN=... node scripts/realtime-asr-probe.mjs "Hey Atlas, log my usual breakfast" ENDPOINTING
// Reads keys from the environment only. Nothing secret is stored here.
import WebSocket from "ws";
const KEY = process.env.MODEL_API_KEY, WIT = process.env.WIT_SERVER_TOKEN;
const phrase = process.argv[2] || "Hey Atlas, log my usual breakfast";
const mode = process.argv[3] || "ENDPOINTING";

const r = await fetch("https://api.wit.ai/synthesize?v=20240304", { method: "POST",
  headers: { Authorization: "Bearer " + WIT, "Content-Type": "application/json", Accept: "audio/raw" },
  body: JSON.stringify({ q: phrase, voice: "wit$Rebecca", style: "default" }) });
const pcm = Buffer.from(await r.arrayBuffer());
const RATE = 24000, FRAME = RATE * 2 * 80 / 1000, speechMs = pcm.length / 2 / RATE * 1000;
console.log(`phrase: "${phrase}"  mode ${mode}  audio ${speechMs.toFixed(0)} ms`);

const ws = new WebSocket("wss://api.meta.ai/v1/asr/realtime?sessionId=t-" + Date.now());
let audioStart = 0, events = [], sent = false;
const rel = () => (performance.now() - audioStart);
ws.on("open", () => ws.send(JSON.stringify({ authorization: { accessToken: "Bearer " + KEY },
  audioEncoding: "PCM_24KHZ", model: "muse-voice-transcribe-1.0", mode, languageBias: ["English"], keywords: ["Atlas", "A Better U"] })));
ws.on("message", (d, isBinary) => {
  if (isBinary) return;
  const e = JSON.parse(d.toString());
  if (e.sessionId && !e.type) { if (!sent) { sent = true; stream(); } return; }
  const t = audioStart ? rel() : 0;
  events.push({ type: e.type, t, final: e.final });
  console.log(`[audio+${t.toFixed(0).padStart(5)} ms] ${e.type}${e.final !== undefined ? (e.final ? " FINAL" : " partial") : ""} ${e.transcript ? JSON.stringify(e.transcript) : ""}`);
});
async function stream() {
  const frames = [];
  for (let o = 0; o < pcm.length; o += FRAME) { let f = pcm.subarray(o, o + FRAME); if (f.length < FRAME) f = Buffer.concat([f, Buffer.alloc(FRAME - f.length)]); frames.push(f); }
  for (let i = 0; i < 30; i++) frames.push(Buffer.alloc(FRAME));          // 2.4 s of quiet afterwards
  audioStart = performance.now();
  for (let i = 0; i < frames.length; i++) {
    if (mode === "PUSH_TO_TALK" && i === Math.ceil(pcm.length / FRAME)) break;   // push-to-talk: stop when the speech stops
    ws.send(frames[i]);
    await new Promise(r => setTimeout(r, Math.max(0, audioStart + (i + 1) * 80 - performance.now())));
  }
  console.log(`[audio+${rel().toFixed(0).padStart(5)} ms] audio finished (speech ended at ${speechMs.toFixed(0)} ms)`);
  ws.send(JSON.stringify({ type: "endStream" }));
}
ws.on("close", (c, reason) => {
  console.log("closed", c, reason.toString());
  const done = events.find(e => e.type === "speechComplete") || events.filter(e => e.type === "transcript" && e.final).pop();
  if (done) console.log(`\n==> final transcript ${(done.t - speechMs).toFixed(0)} ms after the speech ended`);
  process.exit(0);
});
ws.on("error", e => console.log("error", e.message));
setTimeout(() => { console.log("timeout"); process.exit(1); }, 45000);
