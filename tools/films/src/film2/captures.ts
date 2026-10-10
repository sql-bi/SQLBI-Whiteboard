import type {Capture} from '../film1/captures';

// The seven captures of film 2, by the file names in README.md. All are 30 fps; D1 is
// 3840 x 2160 and the others 1920 x 1080. Each sits on the board at 1920 x 1080 board
// units, so D1 is shown at half its size and the others at their own size, and the camera
// never zooms past 1, so no capture is ever enlarged.
//
// `seconds` is the length of the video track, read with ffprobe; a trim must end inside
// it. `from` and `rate` here are the defaults; each cut in film.ts sets its own. Times
// in the comments are seconds of the capture, read from frame sheets of the recordings
// of 10 October 2026.

export type StationId = 'D1' | 'D2' | 'D3' | 'D4' | 'D5' | 'D6' | 'D7';

const capture = (id: StationId, file: string, seconds: number, description: string): Capture => ({
  id,
  file,
  seconds,
  description,
  from: 0,
  rate: 1,
  callouts: [],
});

export const captures: Record<StationId, Capture> = {
  // A dot at 0.1 s; three boxes by 5.8 s, the two connectors at 6.3 and 7.0 s.
  D1: capture('D1', 'ink-diagram.mp4', 8.566, 'The pen draws a diagram; the pressure shows in the stroke.'),
  // View at 0.5 s, the window picker from 2 s, the Power BI window on the board at 5 s,
  // the highlighter marks from 6.5 s.
  D2: capture('D2', 'liveview-powerbi.mp4', 9.566, 'View, LiveView, the Power BI window, Freeze, and highlighter marks.'),
  // The measure is on the board on one line at 3 s; F6 at 5.3 s; formatted by 6.5 s.
  D3: capture('D3', 'dax-f6.mp4', 9.233, 'A long one-line measure is pasted; F6 formats it.'),
  // Insert at 0.8 s, a rectangle at 1.6 s, a diamond at 4.4 s, the connector at 6 s, the
  // rectangle moved from 6.6 to 7.6 s with the arrow following.
  D4: capture('D4', 'shapes-connectors.mp4', 10.1, 'Insert: shapes and a connector; a shape moves and the arrow follows.'),
  // The board's content, the Export dialog at 3 s, Save at 7.3 s, PowerPoint from 8.4 s.
  D5: capture('D5', 'frames-export.mp4', 11.033, 'Frames on the board, Export, and the deck open in PowerPoint.'),
  // The .pptx is dropped at 0.8 s, the import dialog runs to 3.8 s, the slides are on the
  // board at 4.4 s, and the view zooms out over them from 5 to 6.6 s.
  D6: capture('D6', 'open-pptx-zip.mp4', 8.466, 'A .pptx dropped on the board becomes its slides.'),
  // Zoomed in on ink; a double-click at 3.6 s fits the whole board by 4 s.
  D7: capture('D7', 'full-board.mp4', 6.366, 'A double-click on empty canvas fits the whole board in the window.'),
};
