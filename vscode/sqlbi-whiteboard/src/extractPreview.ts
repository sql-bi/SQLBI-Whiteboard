import { unzipSync } from 'fflate';

export const PreviewEntryName = 'preview.png';

export type PreviewResult =
  | { kind: 'preview'; bytes: Uint8Array }
  | { kind: 'missing' }
  | { kind: 'invalid' };

export function extractPreview(archive: Uint8Array): PreviewResult {
  let files: Record<string, Uint8Array>;
  try {
    files = unzipSync(archive);
  } catch {
    return { kind: 'invalid' };
  }

  for (const [name, data] of Object.entries(files)) {
    if (normalizeEntryName(name) === PreviewEntryName) {
      return data.length === 0 ? { kind: 'missing' } : { kind: 'preview', bytes: data };
    }
  }

  return { kind: 'missing' };
}

function normalizeEntryName(name: string): string {
  return name.replaceAll('\\', '/').replace(/^\.\//, '').toLowerCase();
}
