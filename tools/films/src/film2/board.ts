// The board of film 2 and the camera that moves across it.
//
// The board is one plane measured in board units, where a 1920 x 1080 capture is 1920 x
// 1080 units. Each station is a capture in a window with its caption on the board below
// it. The camera is a centre on the board and a zoom, the output pixels per board unit;
// it never goes above 1, so no capture is enlarged past its own size.
//
// The board is laid out once per shape: each shape has its own station framing, caption
// size, and gaps, chosen so that at a station the caption sits inside the safe area of
// that shape and no neighbouring window shows at the edge of the frame.

import type {Rect, Shape} from '../layout';
import type {StationId} from './captures';

export const WINDOW = {w: 1920, h: 1080};

// The slow push-in while the camera rests on a station, as a fraction of the zoom.
export const DRIFT = 0.025;

export type Camera = {x: number; y: number; z: number};

export type Caption = {rect: Rect; size: number};

export type Station = {
  id: StationId;
  window: Rect;
  caption: Caption;
  // The camera on arrival; it pushes in by DRIFT before it leaves.
  camera: Camera;
};

export type Board = {
  shape: Shape;
  stations: Record<StationId, Station>;
  // The closing line below the stations, and the camera that frames the whole board.
  closing: Caption;
  whole: Camera;
  bounds: Rect;
};

// Where each station sits, in columns and rows. The camera visits them in the order of
// the beats: right, down, left past D7, up, then down and left to D6.
const grid: Record<StationId, [number, number]> = {
  D1: [0, 0],
  D2: [1, 0],
  D3: [1, 1],
  D7: [0, 1],
  D4: [-1, 1],
  D5: [-1, 0],
  D6: [-2, 0.5],
};

type Spec = {
  // Caption size in output pixels at a station, its line height, and the lines it may use.
  captionPx: number;
  captionLines: number;
  // Gap between the window and its caption, in output pixels.
  gapPx: number;
  // The window fits the safe area, or is this many times the width of the frame and runs
  // off both sides (portrait shorts, where a window that fits would be too small to read).
  windowSpan: 'safe' | number;
  // Size of the closing line in output pixels when the whole board is in frame.
  closingPx: number;
};

export const LINE_HEIGHT = 1.12;

const specs: Record<string, Spec> = {
  '16x9': {captionPx: 50, captionLines: 2, gapPx: 30, windowSpan: 'safe', closingPx: 64},
  '1x1': {captionPx: 46, captionLines: 3, gapPx: 28, windowSpan: 'safe', closingPx: 50},
  '4x5': {captionPx: 50, captionLines: 3, gapPx: 30, windowSpan: 'safe', closingPx: 54},
  '9x16': {captionPx: 64, captionLines: 4, gapPx: 40, windowSpan: 1.25, closingPx: 60},
};

// Where a window wider than the frame is cropped: 0.5 crops both sides equally, a lower
// value keeps more of the left. The table in D2, the measure in D3, and the moved shape
// in D4 sit against the left edge; the toolbar on the right gives way.
const focus: Partial<Record<StationId, number>> = {D2: 0.2, D3: 0.15, D4: 0.2};

// The safe area shrunk about the centre of the frame, so that what fits inside it stays
// inside the safe area through the push-in.
const innerSafe = (shape: Shape, drift: number): Rect => {
  const cx = shape.width / 2;
  const cy = shape.height / 2;
  const k = 1 / (1 + drift);
  const {x, y, w, h} = shape.safe;
  return {x: cx + (x - cx) * k, y: cy + (y - cy) * k, w: w * k, h: h * k};
};

export const boardFor = (shape: Shape): Board => {
  const spec = specs[shape.id];
  if (!spec) throw new Error(`Film 2 has no board for shape ${shape.id}`);
  const inner = innerSafe(shape, DRIFT);
  const captionH = spec.captionPx * LINE_HEIGHT * spec.captionLines;

  // Station framing in output pixels: the window, then the caption below it, centred
  // vertically in the inner safe area.
  let z: number;
  let capX: number;
  let capW: number;
  if (spec.windowSpan === 'safe') {
    z = Math.min(inner.w / WINDOW.w, (inner.h - spec.gapPx - captionH) / WINDOW.h);
    capX = Math.max(inner.x, (shape.width - WINDOW.w * z) / 2);
    capW = Math.min(WINDOW.w * z, inner.w);
  } else {
    z = (shape.width * spec.windowSpan) / WINDOW.w;
    capX = inner.x;
    capW = inner.w;
  }
  const winXFor = (id: StationId) => (shape.width - WINDOW.w * z) * (spec.windowSpan === 'safe' ? 0.5 : (focus[id] ?? 0.5));
  const winX = winXFor('D1');
  const blockH = WINDOW.h * z + spec.gapPx + captionH;
  const winY = inner.y + (inner.h - blockH) / 2;
  const capY = winY + WINDOW.h * z + spec.gapPx;

  // Gaps between stations, in board units, wide enough that no neighbour shows in the
  // frame at a station: what the frame shows beyond the window and caption, plus a margin
  // that covers the shorts, which start 10 % further back.
  const margin = 320;
  // A window wider than the frame covers it from side to side, so only the vertical gap
  // matters there.
  const gapX = Math.max(560, winX / z + margin, (shape.width - winX - WINDOW.w * z) / z + margin);
  const stationH = WINDOW.h + (spec.gapPx + captionH) / z;
  const gapY = Math.max(420, winY / z + margin, (shape.height - capY - captionH) / z + margin);
  const pitchX = WINDOW.w + gapX;
  const pitchY = stationH + gapY;

  const stations = {} as Record<StationId, Station>;
  for (const id of Object.keys(grid) as StationId[]) {
    const [col, row] = grid[id];
    const window: Rect = {x: col * pitchX, y: row * pitchY, w: WINDOW.w, h: WINDOW.h};
    const winX = winXFor(id);
    const caption: Caption = {
      rect: {
        x: window.x + (capX - winX) / z,
        y: window.y + (capY - winY) / z,
        w: capW / z,
        h: captionH / z,
      },
      size: spec.captionPx / z,
    };
    // The board point at the centre of the frame when the window sits at (winX, winY).
    const camera: Camera = {
      x: window.x + (shape.width / 2 - winX) / z,
      y: window.y + (shape.height / 2 - winY) / z,
      z,
    };
    stations[id] = {id, window, caption, camera};
  }

  // The stations' extent: windows and captions.
  const all = Object.values(stations);
  const left = Math.min(...all.map((s) => s.window.x));
  const right = Math.max(...all.map((s) => s.window.x + s.window.w));
  const top = Math.min(...all.map((s) => s.window.y));
  const bottom = Math.max(...all.map((s) => s.caption.rect.y + s.caption.rect.h));
  const gridW = right - left;
  const gridH = bottom - top;

  // The closing line sits one gap below the stations and is sized for the whole-board
  // framing, which in turn has to make room for it: zW * (gridH + gapY) + closing = inner.h.
  const wholeInner = innerSafe(shape, 0.01);
  const closingH = spec.closingPx * 1.2;
  const zW = Math.min(wholeInner.w / gridW, (wholeInner.h - closingH) / (gridH + gapY));
  const closing: Caption = {
    rect: {x: left, y: bottom + gapY, w: gridW, h: closingH / zW},
    size: spec.closingPx / zW,
  };
  const bounds: Rect = {x: left, y: top, w: gridW, h: gridH + gapY + closingH / zW};
  const whole: Camera = {x: left + gridW / 2, y: top + bounds.h / 2, z: zW};

  return {shape, stations, closing, whole, bounds};
};
