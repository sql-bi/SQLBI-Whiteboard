// Renders the films for the platforms, into out/.
//   node scripts/render.mjs 16x9 | 1x1 | 4x5 | 9x16 | poster | all             film 1
//   node scripts/render.mjs film2 16x9 | 1x1 | 4x5 | short-liveview | short-dax |
//                                 short-shapes | store | site | all            film 2
//
// Film 1: each shape is rendered once at twice its size (3840 x 2160 for 16:9) into
// out/master/, then scaled down with ffmpeg into the H.264 file the platforms take.
//
// Film 2: each composition is rendered once at its own size into out/master/, because
// its captures are 1080p and a larger master would only enlarge them. The renditions are
// encoded from there, with the music bed mixed in by ffmpeg; `site` writes the two
// teaser files and the poster into site/ at the root of the repository.
import {existsSync, mkdirSync} from 'node:fs';
import {join} from 'node:path';
import {renderMedia, renderStill, selectComposition} from '@remotion/renderer';
import beats from '../src/film1/beats.json' with {type: 'json'};
import {film2Compositions} from '../src/film2/compositions.ts';
import {cuts} from '../src/film2/cuts.ts';
import {shapes} from '../src/layout.ts';
import {browserOptions, ffmpeg, makeBundle, meanVolume, out, progressLogger, root} from './lib.mjs';
import {prepareMedia} from './prepare-media.mjs';

const FPS = 30;
const master = join(out, 'master');

const args = process.argv.slice(2);
if (args[0] === 'film2') {
  await renderFilm2(args.slice(1));
} else {
  await renderFilm1(args);
}

async function renderFilm1(targets) {
  const DURATION = beats.durationSeconds;
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
}

async function renderFilm2(targets) {
  const renditions = ['16x9', '1x1', '4x5', 'short-liveview', 'short-dax', 'short-shapes', 'store', 'site'];
  if (targets.length === 0 || targets.some((t) => t !== 'all' && !renditions.includes(t))) {
    console.error(`Name a rendition: ${renditions.join(', ')}, or all.`);
    process.exit(1);
  }
  const wanted = targets.includes('all') ? renditions : targets;
  // The store trailer and the site files come from the 16:9 master.
  const compositionOf = (r) => (r === 'store' || r === 'site' ? 'film2-16x9' : `film2-${r}`);
  const needed = [...new Set(wanted.map(compositionOf))];

  prepareMedia();
  mkdirSync(master, {recursive: true});
  const serveUrl = await makeBundle();

  const masters = {};
  for (const id of needed) {
    const c = film2Compositions.find((x) => x.id === id);
    const inputProps = {shapeId: c.shapeId, cutId: c.cutId};
    const composition = await selectComposition({serveUrl, id, inputProps, ...browserOptions()});
    masters[id] = join(master, `${id}-master.mp4`);
    await renderMedia({
      composition,
      serveUrl,
      inputProps,
      codec: 'h264',
      outputLocation: masters[id],
      crf: 10,
      imageFormat: 'png',
      pixelFormat: 'yuv420p',
      muted: true,
      onProgress: progressLogger(`${id} master`),
      ...browserOptions(),
    });
  }

  const bed = join(root, 'audio', 'film2-bed.mp3');
  const hasBed = existsSync(bed);
  if (!hasBed) console.log('audio/film2-bed.mp3 is missing: the social files carry a silent track.');

  for (const r of wanted) {
    const id = compositionOf(r);
    const c = film2Compositions.find((x) => x.id === id);
    const seconds = cuts[c.cutId].seconds;
    if (r === 'site') {
      encodeSite(masters[id], seconds);
      continue;
    }
    const name = r === 'store' ? 'film2-store-1920x1080.mp4' : `film2-${r}.mp4`;
    encodeSocial(masters[id], join(out, name), seconds, c.cutId === 'film', r === 'store' ? 16 : 18, hasBed ? bed : null);
    console.log(`Wrote out/${name}`);
  }
}

// The music bed under a cut: the film takes the bed from its start, where it rises on
// its own, and fades it over the end card; a short takes 12 seconds from the middle of
// the bed, with a short fade at each end. The level is set so the mix averages -20 dB.
function bedFilter(bed, seconds, fromStart) {
  const offset = fromStart ? 0 : 14;
  const fadeIn = fromStart ? 0 : 0.5;
  const fadeOut = fromStart ? 2.5 : 1.5;
  const shape = [
    `atrim=start=${offset}:end=${offset + seconds}`,
    'asetpts=PTS-STARTPTS',
    ...(fadeIn ? [`afade=t=in:st=0:d=${fadeIn}`] : []),
    `afade=t=out:st=${seconds - fadeOut}:d=${fadeOut}`,
  ].join(',');
  const gain = -20 - meanVolume(bed, shape);
  return `${shape},volume=${gain.toFixed(2)}dB,alimiter=limit=0.9:level=disabled`;
}

function encodeSocial(masterFile, file, seconds, fromStart, crf, bed) {
  const audioIn = bed ? ['-i', bed] : ['-f', 'lavfi', '-t', String(seconds), '-i', 'anullsrc=r=48000:cl=stereo'];
  const audioMap = bed ? ['-filter_complex', `[1:a]${bedFilter(bed, seconds, fromStart)}[a]`, '-map', '0:v', '-map', '[a]'] : ['-map', '0:v', '-map', '1:a'];
  ffmpeg([
    '-y',
    '-i', masterFile,
    ...audioIn,
    ...audioMap,
    '-vf', 'scale=in_range=auto:out_range=tv,format=yuv420p',
    '-color_range', 'tv',
    '-c:v', 'libx264',
    '-profile:v', 'high',
    '-preset', 'slow',
    '-crf', String(crf),
    '-pix_fmt', 'yuv420p',
    '-r', String(FPS),
    '-c:a', 'aac',
    '-b:a', '128k',
    '-ar', '48000',
    '-t', String(seconds),
    '-movflags', '+faststart',
    file,
  ]);
}

// The commands in docs/teaser/shot-script.md. The site file loops, so the end card fades
// to paper over the last half second and the restart cuts to the white board. The camera
// is always moving, so every frame differs from the last; the CRFs are higher than the
// 42-second cut's to keep each file near 3.5 MB.
function encodeSite(masterFile, seconds) {
  const site = join(root, '..', '..', 'site');
  const fade = `fade=t=out:st=${seconds - 0.5}:d=0.5:color=0xF7F6F2`;
  ffmpeg(['-y', '-i', masterFile, '-vf', `scale=1920:-2:flags=lanczos,${fade}`, '-c:v', 'libsvtav1', '-crf', '46', '-preset', '6', '-movflags', '+faststart', '-an', join(site, 'teaser-av1.mp4')]);
  ffmpeg(['-y', '-i', masterFile, '-vf', `scale=1920:-2:flags=lanczos,${fade}`, '-c:v', 'libx264', '-crf', '31', '-preset', 'veryslow', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', join(site, 'teaser-h264.mp4')]);
  // The poster is the whole board, from the 41–47 s beat.
  ffmpeg(['-y', '-ss', '46.0', '-i', masterFile, '-frames:v', '1', '-vf', 'scale=1920:-2:flags=lanczos', '-q:v', '3', join(site, 'teaser-poster.jpg')]);
  console.log('Wrote site/teaser-av1.mp4, site/teaser-h264.mp4, and site/teaser-poster.jpg');
}
