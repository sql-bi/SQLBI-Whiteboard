import {getStaticFiles, staticFile} from 'remotion';

// scripts/prepare-media.mjs links captures/ and audio/ into public/ before each preview
// or render, so a file that is missing there is missing on disk too.
export const mediaFile = (path: string): string | null =>
  getStaticFiles().some((f) => f.name === path) ? staticFile(path) : null;
