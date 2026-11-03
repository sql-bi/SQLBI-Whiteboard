import type {CutId} from './cuts';

// Film 2's compositions: the film in three shapes, and the three portrait shorts. The
// render and stills scripts read this list too.
export const film2Compositions: {id: string; shapeId: string; cutId: CutId}[] = [
  {id: 'film2-16x9', shapeId: '16x9', cutId: 'film'},
  {id: 'film2-1x1', shapeId: '1x1', cutId: 'film'},
  {id: 'film2-4x5', shapeId: '4x5', cutId: 'film'},
  {id: 'film2-short-liveview', shapeId: '9x16', cutId: 'liveview'},
  {id: 'film2-short-dax', shapeId: '9x16', cutId: 'dax'},
  {id: 'film2-short-shapes', shapeId: '9x16', cutId: 'shapes'},
];
