// The four screen captures, by the file names the shot list in README.md gives them.
// Until a file exists in captures/, the film shows a labelled placeholder in its place.
//
// Times are in seconds of the capture itself, so they stay in step with the recording
// when a beat moves. Boxes are fractions of the captured frame: [x, y, width, height].
// The positions below were read from the 3840 x 2160 captures of 27 September 2026 with
// the application maximized; a new recording with a different layout needs new ones.

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
  seconds: 18.6,
  description: 'A finished board open in the Microsoft Whiteboard app for Windows.',
  // Shown as a still frame taken at this time.
  from: 4,
  rate: 1,
  callouts: [],
};

export const C2: Capture = {
  id: 'C2',
  file: 'microsoft-whiteboard-export.mp4',
  seconds: 16.6,
  description: 'Settings gear, Export, Zip (HTML+JSON), the save dialog, and the saved file.',
  // The gear is clicked at about 1.5 s, the Export panel is open by 3.5 s, and the
  // "Zip file exported" dialog is up by 4.5 s, so the beat reads 0.8 to 5.4 s.
  from: 0.8,
  rate: 1.4,
  callouts: [
    {n: 1, box: [0.972, 0.038, 0.022, 0.04], at: 1.2},
    {n: 2, box: [0.768, 0.088, 0.222, 0.046], at: 2.4},
    {n: 3, box: [0.78, 0.478, 0.212, 0.05], at: 3.6},
  ],
};

export const C3: Capture = {
  id: 'C3',
  file: 'sqlbi-open-zip.mp4',
  seconds: 27.9,
  description: 'SQLBI Whiteboard: File, Open on the ZIP, and the board appears.',
  // The file picker is open by 4 s and the imported board is on screen by 10 s.
  from: 3,
  rate: 1.6,
  callouts: [],
};

export const C4: Capture = {
  id: 'C4',
  file: 'sqlbi-saved-board.mp4',
  seconds: 10.9,
  description: 'The imported board, then Save; the title bar shows the saved name.',
  // Save As runs from 2 to 6 s; the title bar carries the new name from 8 s.
  from: 1,
  rate: 2.5,
  callouts: [{box: [0.004, 0.004, 0.24, 0.028], at: 9}],
};

export const captures = [C1, C2, C3, C4];
