// The four screen captures, by the file names the shot list in README.md gives them.
// Until a file exists in captures/, the film shows a labelled placeholder in its place.
//
// Times are in seconds of the capture itself, so they stay in step with the recording
// when a beat moves. Boxes are fractions of the captured frame: [x, y, width, height].
// The positions below are estimates for a 2560 x 1440 display capture at 150 % scaling
// with the application maximized; correct them once the real captures exist.

export type Box = [number, number, number, number];

export type Callout = {
  // Number shown in the badge. A ring without a number marks something to look at.
  n?: number;
  box: Box;
  // Capture time at which the ring appears.
  at: number;
};

export type Capture = {
  id: string;
  file: string;
  seconds: number;
  description: string;
  // Where the film starts reading the capture, and how fast it plays it.
  from: number;
  rate: number;
  callouts: Callout[];
};

export const C1: Capture = {
  id: 'C1',
  file: 'microsoft-whiteboard-board.mp4',
  seconds: 8,
  description: 'A finished board open in the Microsoft Whiteboard app for Windows.',
  // Shown as a still frame taken at this time.
  from: 4,
  rate: 1,
  callouts: [],
};

export const C2: Capture = {
  id: 'C2',
  file: 'microsoft-whiteboard-export.mp4',
  seconds: 12,
  description: 'Settings gear, Export, Zip (HTML+JSON), the save dialog, and the saved file.',
  from: 0,
  rate: 2,
  callouts: [
    {n: 1, box: [0.948, 0.045, 0.036, 0.058], at: 1.5},
    {n: 2, box: [0.79, 0.11, 0.19, 0.058], at: 4},
    {n: 3, box: [0.79, 0.53, 0.19, 0.062], at: 7},
  ],
};

export const C3: Capture = {
  id: 'C3',
  file: 'sqlbi-open-zip.mp4',
  seconds: 10,
  description: 'SQLBI Whiteboard: File, Open on the ZIP, and the board appears.',
  from: 0,
  rate: 2.5,
  callouts: [],
};

export const C4: Capture = {
  id: 'C4',
  file: 'sqlbi-saved-board.mp4',
  seconds: 8,
  description: 'The imported board, then Save; the title bar shows Workshop.wboard.',
  from: 0,
  rate: 2,
  callouts: [{box: [0.004, 0.004, 0.2, 0.04], at: 5}],
};

export const captures = [C1, C2, C3, C4];
