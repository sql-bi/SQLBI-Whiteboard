// Renders review stills and the font check into out/stills/.
//   node scripts/stills.mjs                 film 1, every shape, one still in each beat
//   node scripts/stills.mjs 9x16 1x1        film 1, the shapes named
//   node scripts/stills.mjs film2           film 2, every composition, every 5 seconds
//   node scripts/stills.mjs film2 16x9 short-dax
//   ... --at=18.2,19                        other times, in seconds
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
import {renderStill, selectComposition} from '@remotion/renderer';
import {film2Compositions} from '../src/film2/compositions.ts';
import {cuts} from '../src/film2/cuts.ts';
import {shapes} from '../src/layout.ts';
import {browserOptions, makeBundle, out} from './lib.mjs';
import {prepareMedia} from './prepare-media.mjs';

const FPS = 30;
let args = process.argv.slice(2);
const film2 = args[0] === 'film2';
if (film2) args = args.slice(1);
const at = args.find((a) => a.startsWith('--at='));
const times = at ? at.slice(5).split(',').map(Number) : null;
const wanted = args.filter((a) => !a.startsWith('--'));

// Each item: a composition, its props, and the times to render.
const jobs = film2
  ? film2Compositions
      .filter((c) => wanted.length === 0 || wanted.includes(c.id.replace(/^film2-/, '')))
      .map((c) => {
        const seconds = cuts[c.cutId].seconds;
        const every5 = [];
        for (let s = 0; s < seconds; s += 5) every5.push(s);
        every5.push(seconds - 1 / FPS);
        return {id: c.id, inputProps: {shapeId: c.shapeId, cutId: c.cutId}, seconds: times ?? every5};
      })
  : (wanted.length ? shapes.filter((s) => wanted.includes(s.id)) : shapes).map((s) => ({
      id: `film1-${s.id}`,
      inputProps: {shapeId: s.id},
      seconds: times ?? [2, 6, 10, 14, 20, 25, 29],
    }));

prepareMedia();
const serveUrl = await makeBundle();
const dir = join(out, 'stills');
mkdirSync(dir, {recursive: true});

const fontCheck = await selectComposition({serveUrl, id: 'font-check', ...browserOptions()});
await renderStill({composition: fontCheck, serveUrl, output: join(dir, 'font-check.png'), ...browserOptions()});
console.log('out/stills/font-check.png');

for (const job of jobs) {
  const composition = await selectComposition({serveUrl, id: job.id, inputProps: job.inputProps, ...browserOptions()});
  for (const sec of job.seconds) {
    const label = Number.isInteger(sec) ? String(sec).padStart(2, '0') : sec.toFixed(2).padStart(5, '0');
    const name = `${job.id}-${label}s.png`;
    await renderStill({
      composition,
      serveUrl,
      inputProps: job.inputProps,
      frame: Math.min(composition.durationInFrames - 1, Math.round(sec * FPS)),
      output: join(dir, name),
      ...browserOptions(),
    });
    console.log(`out/stills/${name}`);
  }
}
