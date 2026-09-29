// The four screen captures, by the file names the shot list in README.md gives them.
// Until a file exists in captures/, the film shows a labelled placeholder in its place.
//
// Times are in seconds of the capture itself, so they stay in step with the recording
// when a beat moves. Boxes are fractions of the captured frame: [x, y, width, height].
// The positions below were read from the 3840 x 2160 captures of 28 September 2026 with
// the application maximized; a new recording with a different layout needs new ones.
// `seconds` is the length of the video track, which can be shorter than the file's audio,
// so a trim must end inside it.

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
  seconds: 6.2,
  description: 'A finished board open in the Microsoft Whiteboard app for Windows.',
  // Shown as a still frame taken at this time.
  from: 3,
  rate: 1,
  callouts: [],
};

export const C2: Capture = {
  id: 'C2',
  file: 'microsoft-whiteboard-export.mp4',
  seconds: 10.5,
  description: 'Settings gear, Export, Zip (HTML+JSON), the save dialog, and the saved file.',
  // The gear is clicked at about 1.5 s, the Export panel is open by 3 s, Zip is clicked at
  // about 4.2 s, and the "Zip file exported" dialog is up by 5 s; the beat reads 1.0 to 6.3 s.
  from: 1.0,
  rate: 1.6,
  callouts: [
    {n: 1, box: [0.972, 0.038, 0.022, 0.04], at: 1.4},
    {n: 2, box: [0.768, 0.088, 0.222, 0.046], at: 2.6},
    {n: 3, box: [0.78, 0.478, 0.212, 0.05], at: 4.1},
  ],
};

export const C3: Capture = {
  id: 'C3',
  file: 'sqlbi-open-zip.mp4',
  seconds: 8.37,
  description: 'SQLBI Whiteboard: File, Open on the ZIP, and the board appears.',
  // The Open dialog is up by 3 s and the imported board is on screen from about 5.8 s.
  // The beat plays 5.3 s of the film, so this reads 1.4 to 8.0 s.
  from: 1.4,
  rate: 1.25,
  callouts: [],
};

export const C4: Capture = {
  id: 'C4',
  file: 'sqlbi-saved-board.mp4',
  seconds: 19.5,
  description: 'The imported board, then Save; the title bar shows the saved name.',
  // The Save dialog is open from 4 s and the title bar carries Memory.wboard from 14 s;
  // the beat reads 4 to 15.8 s.
  from: 4,
  rate: 2.8,
  callouts: [{box: [0.004, 0.004, 0.24, 0.028], at: 14.4}],
};

export const captures = [C1, C2, C3, C4];
