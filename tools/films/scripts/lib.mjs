import {spawnSync} from 'node:child_process';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {bundle} from '@remotion/bundler';

export const root = dirname(dirname(fileURLToPath(import.meta.url)));
export const out = join(root, 'out');

// Remotion renders with its own Chrome Headless Shell, which uses the fonts installed
// on Windows. Set FILMS_CHROME to a Chrome executable to render with that instead.
export const browserOptions = () =>
  process.env.FILMS_CHROME
    ? {browserExecutable: process.env.FILMS_CHROME, chromeMode: 'chrome-for-testing'}
    : {};

export const makeBundle = async () => {
  console.log('Bundling the project...');
  return bundle({
    entryPoint: join(root, 'src', 'index.ts'),
    publicDir: join(root, 'public'),
  });
};

export const ffmpeg = (args) => {
  const exe = process.env.FFMPEG ?? 'ffmpeg';
  const r = spawnSync(exe, ['-hide_banner', '-loglevel', 'error', ...args], {stdio: 'inherit'});
  if (r.status !== 0) throw new Error(`ffmpeg failed: ${args.join(' ')}`);
};

export const progressLogger = (label) => {
  let last = -1;
  return ({progress}) => {
    const pct = Math.floor(progress * 100);
    if (pct >= last + 10) {
      last = pct - (pct % 10);
      console.log(`${label}: ${pct} %`);
    }
  };
};
