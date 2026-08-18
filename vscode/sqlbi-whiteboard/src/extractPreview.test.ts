import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { extractPreview } from './extractPreview';

const fixtures = path.join(__dirname, '..', 'fixtures');

test('extracts preview.png from a board that has one', () => {
  const archive = readFileSync(path.join(fixtures, 'with-preview.wboard'));
  const result = extractPreview(archive);
  assert.equal(result.kind, 'preview');
  if (result.kind === 'preview') {
    assert.equal(result.bytes[0], 0x89);
    assert.equal(result.bytes[1], 0x50);
    assert.equal(result.bytes[2], 0x4e);
    assert.equal(result.bytes[3], 0x47);
    assert.ok(result.bytes.length > 8);
  }
});

test('reports boards that have no preview', () => {
  const archive = readFileSync(path.join(fixtures, 'without-preview.wboard'));
  assert.equal(extractPreview(archive).kind, 'missing');
});

test('reports junk that is not a zip', () => {
  assert.equal(extractPreview(Buffer.from('not a board')).kind, 'invalid');
});
