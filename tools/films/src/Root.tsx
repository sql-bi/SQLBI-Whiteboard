import React from 'react';
import {Composition, Folder, Still} from 'remotion';
import {DURATION_SECONDS, FPS, Film1} from './film1/Film1';
import {FontCheck} from './FontCheck';
import {shapes} from './layout';

// One composition per shape, all rendering the same film with the layout of its shape.
export const Root: React.FC = () => (
  <>
    <Folder name="film1-export-before-16-october">
      {shapes.map((s) => (
        <Composition
          key={s.id}
          id={`film1-${s.id}`}
          component={Film1}
          width={s.width}
          height={s.height}
          fps={FPS}
          durationInFrames={Math.round(DURATION_SECONDS * FPS)}
          defaultProps={{shapeId: s.id}}
        />
      ))}
    </Folder>
    <Still id="font-check" component={FontCheck} width={1920} height={1080} />
  </>
);
