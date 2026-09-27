// Renders review stills of every shape at 2, 6, 10, 14, 20, 25, and 29 seconds, one in
// each beat, and the font check, into out/stills/.
//   node scripts/stills.mjs            every shape
//   node scripts/stills.mjs 9x16 1x1   the shapes named
//   node scripts/stills.mjs --at=18.2  other times, in seconds
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
import {renderStill, selectComposition} from '@remotion/renderer';
import {shapes} from '../src/layout.ts';
import {browserOptions, makeBundle, out} from './lib.mjs';
import {prepareMedia} from './prepare-media.mjs';

const FPS = 30;
const args = process.argv.slice(2);
const at = args.find((a) => a.startsWith('--at='));
const SECONDS = at ? at.slice(5).split(',').map(Number) : [2, 6, 10, 14, 20, 25, 29];

const wanted = args.filter((a) => !a.startsWith('--'));
const list = wanted.length ? shapes.filter((s) => wanted.includes(s.id)) : shapes;

prepareMedia();
const serveUrl = await makeBundle();
const dir = join(out, 'stills');
mkdirSync(dir, {recursive: true});

const fontCheck = await selectComposition({serveUrl, id: 'font-check', ...browserOptions()});
await renderStill({composition: fontCheck, serveUrl, output: join(dir, 'font-check.png'), ...browserOptions()});
console.log('out/stills/font-check.png');

for (const s of list) {
  const inputProps = {shapeId: s.id};
  const composition = await selectComposition({serveUrl, id: `film1-${s.id}`, inputProps, ...browserOptions()});
  for (const sec of SECONDS) {
    const name = `film1-${s.id}-${String(sec).padStart(2, '0')}s.png`;
    await renderStill({
      composition,
      serveUrl,
      inputProps,
      frame: Math.round(sec * FPS),
      output: join(dir, name),
      ...browserOptions(),
    });
    console.log(`out/stills/${name}`);
  }
}
