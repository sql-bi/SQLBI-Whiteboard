import React from 'react';
import {Composition, Folder, Still} from 'remotion';
import {DURATION_SECONDS, FPS, Film1} from './film1/Film1';
import {FPS as FPS2, Film2} from './film2/Film2';
import {film2Compositions} from './film2/compositions';
import {cuts} from './film2/cuts';
import {FontCheck} from './FontCheck';
import {shapes} from './layout';

// Film 1 has one composition per shape, all rendering the same film with the layout of
// its shape. Film 2 has the film in three shapes and three portrait shorts.
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
    <Folder name="film2-one-board">
      {film2Compositions.map((c) => {
        const shape = shapes.find((s) => s.id === c.shapeId)!;
        return (
          <Composition
            key={c.id}
            id={c.id}
            component={Film2}
            width={shape.width}
            height={shape.height}
            fps={FPS2}
            durationInFrames={Math.round(cuts[c.cutId].seconds * FPS2)}
            defaultProps={{shapeId: c.shapeId, cutId: c.cutId}}
          />
        );
      })}
    </Folder>
    <Still id="font-check" component={FontCheck} width={1920} height={1080} />
  </>
);
