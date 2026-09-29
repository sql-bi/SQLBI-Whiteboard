// Generates the voice track and the music bed for a film with the ElevenLabs API, and
// reads phrase starts out of a voice track so beats.json can follow the read.
//
//   node scripts/elevenlabs.mjs status
//   node scripts/elevenlabs.mjs voice --film film1 --voice <voice_id> [--model eleven_multilingual_v2]
//   node scripts/elevenlabs.mjs music --film film1 --seconds 30 [--prompt "..."]
//   node scripts/elevenlabs.mjs timings --film film1 [--write]
//
// The key is read from ELEVENLABS_API_KEY and never written anywhere. On Windows the
// variable set in the user profile is not visible to a shell that was already open, so
// start a new terminal, or set it for the session first.

import { readFile, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const api = 'https://api.elevenlabs.io/v1';

const args = process.argv.slice(2);
const command = args[0];
const option = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const flag = (name) => args.includes(`--${name}`);

const key = process.env.ELEVENLABS_API_KEY;
if (!key) {
  console.error('ELEVENLABS_API_KEY is not set in this process.');
  process.exit(2);
}

async function call(route, init = {}) {
  const response = await fetch(`${api}${route}`, {
    ...init,
    headers: { 'xi-api-key': key, ...(init.headers ?? {}) },
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${init.method ?? 'GET'} ${route} → HTTP ${response.status}: ${text}`);
  }
  return response;
}

const film = option('film', 'film1');
const audioDir = path.join(root, 'audio');

async function status() {
  const s = await (await call('/user/subscription')).json();
  console.log(`tier: ${s.tier} | credits used: ${s.character_count} of ${s.character_limit}`);
  console.log(`instant voice cloning: ${s.can_use_instant_voice_cloning} | professional: ${s.can_use_professional_voice_cloning}`);
  const v = await (await call('/voices?page_size=100')).json();
  const own = v.voices.filter((x) => ['cloned', 'professional', 'generated'].includes(x.category));
  if (own.length === 0) console.log('no cloned voices yet');
  for (const x of own) console.log(`voice: ${x.name} | id: ${x.voice_id} | ${x.category}`);
}

// The text to read is the last paragraph of the "Read" section of audio/<film>-vo.txt when
// that section exists (the script with <break> tags), and otherwise the "Script" section.
async function scriptText() {
  const text = await readFile(path.join(audioDir, `${film}-vo.txt`), 'utf8');
  const read = text.match(/\nRead\r?\n-+\r?\n([\s\S]*?)\r?\n\r?\nBeats/);
  if (read) {
    const paragraphs = read[1].trim().split(/\r?\n\r?\n/);
    return paragraphs[paragraphs.length - 1].trim();
  }
  const match = text.match(/Script\r?\n-+\r?\n\r?\n([\s\S]*?)\r?\n\r?\n(Read|Beats)/);
  if (!match) throw new Error(`could not find the Script section in ${film}-vo.txt`);
  return match[1].trim();
}

async function voice() {
  const voiceId = option('voice');
  if (!voiceId) throw new Error('--voice <voice_id> is required; "status" lists your voices');
  const model = option('model', 'eleven_multilingual_v2');
  // 1.0 is the voice's own pace; the API accepts 0.7 to 1.2. A read that runs past the
  // film's length is brought back with a value above 1 before any pause is shortened.
  const speed = Number(option('speed', '1'));
  const text = await scriptText();
  console.log(`${text.split(/\s+/).length} words → ${model}, voice ${voiceId}, speed ${speed}`);
  const response = await call(`/text-to-speech/${voiceId}?output_format=mp3_44100_128`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      text,
      model_id: model,
      voice_settings: { stability: 0.55, similarity_boost: 0.8, style: 0.1, use_speaker_boost: true, speed },
    }),
  });
  const out = path.join(audioDir, `${film}-vo.mp3`);
  await writeFile(out, Buffer.from(await response.arrayBuffer()));
  console.log(`wrote ${out} (${duration(out)} s)`);
}

async function music() {
  const seconds = Number(option('seconds', '30'));
  const prompt = option(
    'prompt',
    'Calm, minimal, instrumental. Soft piano and a warm pad, no drums, no vocals, unhurried, ' +
      'steady, suitable under spoken narration. Ends with a gentle fade.',
  );
  const response = await call('/music?output_format=mp3_44100_128', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ prompt, music_length_ms: seconds * 1000, force_instrumental: true }),
  });
  const out = path.join(audioDir, `${film}-bed.mp3`);
  await writeFile(out, Buffer.from(await response.arrayBuffer()));
  console.log(`wrote ${out} (${duration(out)} s)`);
}

function duration(file) {
  const r = spawnSync('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file], {
    encoding: 'utf8',
  });
  return r.status === 0 ? Number(r.stdout).toFixed(2) : '?';
}

// Phrase starts are the ends of the silences between phrases. The threshold and the
// minimum pause are tuned for a single unhurried voice; a fast read may need d=0.2.
async function timings() {
  const file = path.join(audioDir, `${film}-vo.mp3`);
  const r = spawnSync(
    'ffmpeg',
    ['-hide_banner', '-i', file, '-af', 'silencedetect=noise=-35dB:d=0.3', '-f', 'null', '-'],
    { encoding: 'utf8' },
  );
  const starts = [0, ...[...r.stderr.matchAll(/silence_end: ([\d.]+)/g)].map((m) => Number(m[1]))];
  console.log(`voice track: ${duration(file)} s; phrase starts: ${starts.map((s) => s.toFixed(2)).join(', ')}`);
  const beatsFile = path.join(root, 'src', film, 'beats.json');
  const data = JSON.parse(await readFile(beatsFile, 'utf8'));
  const ids = data.beats.map((b) => b.id);
  if (starts.length !== ids.length) {
    console.log(`${starts.length} phrases found, ${ids.length} beats expected (${ids.join(', ')}); edit beats.json by hand.`);
    return;
  }
  const next = { ...data, beats: ids.map((id, i) => ({ id, start: Number(starts[i].toFixed(2)) })) };
  console.log(JSON.stringify(next.beats));
  if (flag('write')) {
    await writeFile(beatsFile, JSON.stringify(next, null, 2) + '\n');
    console.log(`wrote ${beatsFile}`);
  }
}

const commands = { status, voice, music, timings };
if (!commands[command]) {
  console.error('usage: node scripts/elevenlabs.mjs status | voice | music | timings');
  process.exit(2);
}
commands[command]().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
