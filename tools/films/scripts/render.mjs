// Renders the film for the platforms, into out/.
//   node scripts/render.mjs 16x9 | 1x1 | 4x5 | 9x16 | poster | all
//
// Each shape is rendered once at twice its size (3840 x 2160 for 16:9) into
// out/master/, then scaled down with ffmpeg into the H.264 file the platforms take.
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
import {renderMedia, renderStill, selectComposition} from '@remotion/renderer';
import beats from '../src/film1/beats.json' with {type: 'json'};
import {shapes} from '../src/layout.ts';
import {browserOptions, ffmpeg, makeBundle, out, progressLogger} from './lib.mjs';
import {prepareMedia} from './prepare-media.mjs';

const FPS = 30;
const DURATION = beats.durationSeconds;
const master = join(out, 'master');

const targets = process.argv.slice(2);
if (targets.length === 0) {
  console.error('Name a rendition: 16x9, 1x1, 4x5, 9x16, poster, or all.');
  process.exit(1);
}
const all = targets.includes('all');
const shapeList = shapes.filter((s) => all || targets.includes(s.id));
const poster = all || targets.includes('poster');

prepareMedia();
mkdirSync(master, {recursive: true});
const serveUrl = await makeBundle();

for (const s of shapeList) {
  const inputProps = {shapeId: s.id};
  const composition = await selectComposition({serveUrl, id: `film1-${s.id}`, inputProps, ...browserOptions()});
  const masterFile = join(master, `film1-${s.id}-master.mp4`);
  await renderMedia({
    composition,
    serveUrl,
    inputProps,
    codec: 'h264',
    outputLocation: masterFile,
    scale: 2,
    crf: 12,
    imageFormat: 'jpeg',
    jpegQuality: 95,
    pixelFormat: 'yuv420p',
    // A silent track stands in for the voice until audio/film1-vo.mp3 exists.
    enforceAudioTrack: true,
    audioCodec: 'aac',
    audioBitrate: '320k',
    onProgress: progressLogger(`film1-${s.id} master`),
    ...browserOptions(),
  });

  const file = join(out, `film1-${s.id}.mp4`);
  ffmpeg([
    '-y',
    '-i', masterFile,
    // The master is flagged full range; the platforms expect limited range.
    '-vf', `scale=${s.width}:${s.height}:flags=lanczos:out_range=tv,format=yuv420p`,
    '-color_range', 'tv',
    '-c:v', 'libx264',
    '-profile:v', 'high',
    '-preset', 'slow',
    '-crf', '20',
    '-pix_fmt', 'yuv420p',
    '-r', String(FPS),
    '-c:a', 'aac',
    '-b:a', '128k',
    '-t', String(DURATION),
    '-movflags', '+faststart',
    file,
  ]);
  console.log(`Wrote out/film1-${s.id}.mp4`);
}

if (poster) {
  // The poster is a frame from the beat that shows the saved board.
  const i = beats.beats.findIndex((b) => b.id === 'saved');
  const start = beats.beats[i].start;
  const end = beats.beats[i + 1]?.start ?? DURATION;
  const frame = Math.round((start + (end - start) * 0.875) * FPS);
  const inputProps = {shapeId: '16x9'};
  const composition = await selectComposition({serveUrl, id: 'film1-16x9', inputProps, ...browserOptions()});
  const png = join(master, 'film1-poster-master.png');
  await renderStill({composition, serveUrl, inputProps, frame, scale: 2, output: png, ...browserOptions()});
  ffmpeg(['-y', '-i', png, '-vf', 'scale=1920:1080:flags=lanczos', '-q:v', '2', join(out, 'film1-poster.jpg')]);
  console.log('Wrote out/film1-poster.jpg');
}
