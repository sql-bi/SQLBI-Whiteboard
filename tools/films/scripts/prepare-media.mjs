// Links the captures and audio into public/, where Remotion serves static files from,
// and reports what is present. The film uses a placeholder for anything missing.
// Run by every npm script that previews or renders.
import {copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, rmSync} from 'node:fs';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {captures} from '../src/film1/captures.ts';
import {root} from './lib.mjs';

export const prepareMedia = () => {
  const pub = join(root, 'public');
  rmSync(pub, {recursive: true, force: true});
  for (const dir of ['captures', 'audio']) {
    mkdirSync(join(pub, dir), {recursive: true});
    const src = join(root, dir);
    if (!existsSync(src)) continue;
    for (const name of readdirSync(src)) {
      if (!/\.(mp4|mov|mp3|wav|m4a)$/i.test(name)) continue;
      // A hard link costs nothing; a copy is the fallback across drives.
      try {
        linkSync(join(src, name), join(pub, dir, name));
      } catch {
        copyFileSync(join(src, name), join(pub, dir, name));
      }
    }
  }

  const has = (p) => existsSync(join(pub, p));
  for (const c of captures) {
    console.log(`${c.id} ${c.file}: ${has(`captures/${c.file}`) ? 'present' : 'missing, placeholder shown'}`);
  }
  console.log(`Voice audio/film1-vo.mp3: ${has('audio/film1-vo.mp3') ? 'present' : 'missing, silent track'}`);
  console.log(`Music audio/film1-bed.mp3: ${has('audio/film1-bed.mp3') ? 'present' : 'missing, no music'}`);
};

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  prepareMedia();
}
