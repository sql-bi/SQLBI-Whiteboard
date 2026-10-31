// The cuts of film 2: the 50-second film and the three 12-second shorts. Each is a list
// of camera moves over the board and, for each capture, when it plays.
//
// Every move uses the one easing curve in brand.ts. The camera rests on a station with a
// slow push-in, leaves it, and dips back a little in the middle of each move so the
// board around the stations shows as it passes.

import type {StationId} from './captures';

export type CutId = 'film' | 'liveview' | 'dax' | 'shapes';

export type Segment =
  // Rest on a station, pushing in. `zoomFrom` starts the push-in further back, as a
  // fraction of the station's zoom.
  | {kind: 'hold'; t0: number; t1: number; at: StationId; zoomFrom?: number}
  // Move from one station to the next. `dip` is how far the zoom drops mid-move.
  | {kind: 'move'; t0: number; t1: number; from: StationId; to: StationId; dip: number}
  // Pull back from a station until the whole board is in frame.
  | {kind: 'pull'; t0: number; t1: number; from: StationId}
  // Rest on the whole board.
  | {kind: 'whole'; t0: number; t1: number};

// When a capture starts playing (seconds of the cut), where in the capture it starts,
// and how fast it plays. Before `start` the window holds the frame at `from`; after the
// capture ends it holds the last frame.
export type Play = {start: number; from: number; rate: number};

export type Cut = {
  id: CutId;
  seconds: number;
  camera: Segment[];
  plays: Partial<Record<StationId, Play>>;
  // When the board gives way to the end card.
  endCard: number;
  // The on-screen copy of the closing line, shown on the board below the stations.
  closing: boolean;
};

// The on-screen text of each station. It is final copy.
export const captions: Record<StationId, string> = {
  D1: 'SQLBI Whiteboard',
  D2: 'Capture a running application onto the board and draw on it.',
  D3: 'Paste DAX or SQL. F6 formats it.',
  D4: 'Shapes, sticky notes, and arrows that follow.',
  D5: 'Frames become slides. Export the deck.',
  D6: 'Open a PowerPoint deck or a Microsoft Whiteboard export.',
  D7: '',
};

export const CLOSING = 'One board. A file on your PC.';

// The film: 0–6 s D1, 6–13 D2, 13–20 D3, 20–27 D4, 27–34 D5, 34–41 D6, 41–47 the pull
// back to the whole board, 47–50 the end card. Each move is centred on the beat change.
const film: Cut = {
  id: 'film',
  seconds: 50,
  camera: [
    {kind: 'hold', t0: 0, t1: 5, at: 'D1'},
    {kind: 'move', t0: 5, t1: 7, from: 'D1', to: 'D2', dip: 0.2},
    {kind: 'hold', t0: 7, t1: 12, at: 'D2'},
    {kind: 'move', t0: 12, t1: 14, from: 'D2', to: 'D3', dip: 0.2},
    {kind: 'hold', t0: 14, t1: 18.7, at: 'D3'},
    {kind: 'move', t0: 18.7, t1: 21.3, from: 'D3', to: 'D4', dip: 0.32},
    {kind: 'hold', t0: 21.3, t1: 26, at: 'D4'},
    {kind: 'move', t0: 26, t1: 28, from: 'D4', to: 'D5', dip: 0.2},
    {kind: 'hold', t0: 28, t1: 33, at: 'D5'},
    {kind: 'move', t0: 33, t1: 35, from: 'D5', to: 'D6', dip: 0.2},
    {kind: 'hold', t0: 35, t1: 41, at: 'D6'},
    {kind: 'pull', t0: 41, t1: 45.2, from: 'D6'},
    {kind: 'whole', t0: 45.2, t1: 47.4},
  ],
  plays: {
    // The diagram is drawn by 7.2 s of the capture, at 5.1 s of the film.
    D1: {start: 0, from: 0.2, rate: 1.4},
    // The Power BI window lands at 9.4 s and the marks go on from 10.4 s.
    D2: {start: 6, from: 0.3, rate: 1.4},
    // The paste reads at 15 s and F6 at 16.7 s.
    D3: {start: 13, from: 0.4, rate: 1.3},
    // The shapes go in from 21.3 s; the arrow follows the moved shape at 25 s.
    D4: {start: 20, from: 0.3, rate: 1.3},
    // The Export dialog at 28.9 s, PowerPoint from 32.3 s.
    D5: {start: 27, from: 0.2, rate: 1.6},
    // The import dialog from 35.1 s, the slides on the board at 38 s.
    D6: {start: 34.2, from: 0, rate: 1.15},
    // The double-click fits the board at 44.6 s, while the camera pulls back.
    D7: {start: 41.6, from: 1.0, rate: 1},
  },
  endCard: 47,
  closing: true,
};

// A short is one station of the film in portrait: the camera pushes in on it from a
// little further back for the whole clip, the capture plays at its own speed, and the
// clip ends on a 2-second end card.
const short = (id: CutId, at: StationId, play: Play): Cut => ({
  id,
  seconds: 12,
  camera: [{kind: 'hold', t0: 0, t1: 10.4, at, zoomFrom: 0.9}],
  plays: {[at]: play},
  endCard: 10,
  closing: false,
});

export const cuts: Record<CutId, Cut> = {
  film,
  liveview: short('liveview', 'D2', {start: 0.3, from: 0.3, rate: 1}),
  dax: short('dax', 'D3', {start: 0.6, from: 0.4, rate: 1}),
  shapes: short('shapes', 'D4', {start: 0.2, from: 0.3, rate: 1}),
};
