import React from 'react';
import {AbsoluteFill} from 'remotion';
import {color, fontFamily} from './brand';

// A still that shows whether the browser doing the rendering has the real Segoe UI
// Variable Display. Each family is measured with a monospace fallback behind it: a
// family that is not installed measures the same as the fallback.
const SAMPLE = 'Export before 16 October';
const families = ['Segoe UI Variable Display', 'Segoe UI Variable Text', 'Segoe UI'];

const measure = (font: string) => {
  const ctx = document.createElement('canvas').getContext('2d')!;
  ctx.font = font;
  return ctx.measureText(SAMPLE).width;
};

export const FontCheck: React.FC = () => {
  const fallback = measure('700 100px monospace');
  const rows = families.map((f) => {
    const w = measure(`700 100px "${f}", monospace`);
    return {f, w, installed: Math.abs(w - fallback) > 0.5};
  });
  const display = rows[0];
  const text = rows[1];
  const ok = display.installed && Math.abs(display.w - text.w) > 0.5;
  return (
    <AbsoluteFill style={{background: color.paper, color: color.navy, fontFamily, padding: 80}}>
      <div style={{fontSize: 44, fontWeight: 700, color: ok ? color.navy : color.pen}}>
        {ok ? 'Segoe UI Variable Display is installed and in use.' : 'Segoe UI Variable Display is missing: the text below is a fallback.'}
      </div>
      <div style={{fontSize: 30, fontWeight: 600, marginTop: 30, fontFamily: 'monospace'}}>
        {rows.map((r) => (
          <div key={r.f}>
            {r.f}: {r.w.toFixed(1)} px {r.installed ? '' : '(not installed)'}
          </div>
        ))}
        <div>monospace: {fallback.toFixed(1)} px</div>
      </div>
      <div style={{fontSize: 110, fontWeight: 700, marginTop: 50}}>{SAMPLE}</div>
      <div style={{fontSize: 110, fontWeight: 600, marginTop: 10}}>{SAMPLE}</div>
      <div style={{fontSize: 110, fontWeight: 700, marginTop: 10, fontFamily: '"Segoe UI Variable Text"'}}>{SAMPLE}</div>
      <div style={{fontSize: 26, marginTop: 8}}>The last line is Segoe UI Variable Text, for comparison: its letters sit wider apart.</div>
    </AbsoluteFill>
  );
};
