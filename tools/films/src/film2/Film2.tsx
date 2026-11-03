import React from 'react';
import {AbsoluteFill, useCurrentFrame, useVideoConfig} from 'remotion';
import {color, ease, fontFamily, progress} from '../brand';
import {CaptureView} from '../components/CaptureView';
import {EndCard} from '../components/EndCard';
import {WindowFrame} from '../components/WindowFrame';
import {layoutFor, shapes, type Rect} from '../layout';
import {boardFor, DRIFT, LINE_HEIGHT, type Board, type Camera, type Caption, type Station} from './board';
import {captures} from './captures';
import {CLOSING, captions, cuts, type Cut, type CutId, type Segment} from './cuts';

export const FPS = 30;

const lerp = (a: number, b: number, p: number) => a + (b - a) * p;
const lerpLog = (a: number, b: number, p: number) => Math.exp(lerp(Math.log(a), Math.log(b), p));

const arrive = (s: Station): Camera => s.camera;
const depart = (s: Station): Camera => ({...s.camera, z: s.camera.z * (1 + DRIFT)});

const segmentCamera = (board: Board, seg: Segment, t: number): Camera => {
  const p = ease(Math.min(1, Math.max(0, (t - seg.t0) / (seg.t1 - seg.t0))));
  switch (seg.kind) {
    case 'hold': {
      const s = board.stations[seg.at];
      return {...s.camera, z: lerpLog(arrive(s).z * (seg.zoomFrom ?? 1), depart(s).z, p)};
    }
    case 'move': {
      const a = depart(board.stations[seg.from]);
      const b = arrive(board.stations[seg.to]);
      return {
        x: lerp(a.x, b.x, p),
        y: lerp(a.y, b.y, p),
        z: lerpLog(a.z, b.z, p) * (1 - seg.dip * Math.sin(Math.PI * p)),
      };
    }
    case 'pull': {
      // The zoom changes at an even rate, and the centre moves with the visible width,
      // so the board widens around a point that stays where it is in the frame.
      const a = depart(board.stations[seg.from]);
      const b = board.whole;
      const z = lerpLog(a.z, b.z, p);
      const f = (1 / z - 1 / a.z) / (1 / b.z - 1 / a.z);
      return {x: lerp(a.x, b.x, f), y: lerp(a.y, b.y, f), z};
    }
    case 'whole':
      return {...board.whole, z: board.whole.z * lerp(1, 1.01, p)};
  }
};

export const cameraAt = (board: Board, cut: Cut, t: number): Camera => {
  const segs = cut.camera;
  const seg = segs.find((s) => t < s.t1) ?? segs[segs.length - 1];
  return segmentCamera(board, seg, t);
};

export const Film2: React.FC<{shapeId: string; cutId: CutId}> = ({shapeId, cutId}) => {
  const frame = useCurrentFrame();
  const {fps, width, height} = useVideoConfig();
  const shape = shapes.find((s) => s.id === shapeId);
  if (!shape) throw new Error(`Unknown shape ${shapeId}`);
  const cut = cuts[cutId];
  const board = boardFor(shape);
  const t = frame / fps;
  const cam = cameraAt(board, cut, t);

  // Only what overlaps the frame is drawn, so captures off screen cost nothing.
  const inFrame = (r: Rect, pad = 0.05) => {
    const x = (r.x - cam.x) * cam.z + width / 2;
    const y = (r.y - cam.y) * cam.z + height / 2;
    const w = r.w * cam.z;
    const h = r.h * cam.z;
    const mx = width * pad;
    const my = height * pad;
    return x < width + mx && x + w > -mx && y < height + my && y + h > -my;
  };

  const boardOpacity = 1 - progress(t, cut.endCard, 0.5);

  return (
    <AbsoluteFill style={{background: color.paper, fontFamily, overflow: 'hidden'}}>
      {boardOpacity > 0 ? (
        <div
          style={{
            position: 'absolute',
            left: 0,
            top: 0,
            transformOrigin: '0 0',
            transform: `translate(${width / 2}px, ${height / 2}px) scale(${cam.z}) translate(${-cam.x}px, ${-cam.y}px)`,
            opacity: boardOpacity,
          }}
        >
          {Object.values(board.stations).map((s) =>
            inFrame(s.window) ? <StationWindow key={s.id} station={s} cut={cut} t={t} fps={fps} /> : null,
          )}
          {Object.values(board.stations).map((s) =>
            captions[s.id] && inFrame(s.caption.rect) ? (
              <CaptionText key={`${s.id}-caption`} caption={s.caption} text={captions[s.id]} underline={s.id === 'D1'} />
            ) : null,
          )}
          {cut.closing && inFrame(board.closing.rect) ? (
            <ClosingLine caption={board.closing} opacity={closingOpacity(cut, t)} />
          ) : null}
        </div>
      ) : null}
      {t >= cut.endCard ? <EndCard L={layoutFor(shape)} t={t - cut.endCard} address="whiteboard.sqlbi.com" /> : null}
    </AbsoluteFill>
  );
};

// A capture in its window. It holds its first frame until its play starts and its last
// frame after it ends. A short plays only its own station; the others never enter its
// frame, and hold their first frame.
const StationWindow: React.FC<{station: Station; cut: Cut; t: number; fps: number}> = ({station, cut, t, fps}) => {
  const base = captures[station.id];
  const play = cut.plays[station.id] ?? {start: Infinity, from: 0, rate: 1};
  const capture = {...base, from: play.from, rate: play.rate};
  const end = play.start + (base.seconds - play.from) / play.rate;
  const {w, h} = station.window;

  let view: React.ReactNode;
  if (t >= end) {
    view = <CaptureView capture={capture} startFrame={0} still holdAt={base.seconds} width={w} height={h} />;
  } else if (t < play.start) {
    view = <CaptureView capture={capture} startFrame={0} still holdAt={play.from} width={w} height={h} />;
  } else {
    view = <CaptureView capture={capture} startFrame={Math.round(play.start * fps)} width={w} height={h} />;
  }
  return <WindowFrame rect={station.window}>{view}</WindowFrame>;
};

const CaptionText: React.FC<{caption: Caption; text: string; underline: boolean}> = ({caption, text, underline}) => {
  const {rect, size} = caption;
  const underlineW = size * 7.4;
  return (
    <div
      style={{
        position: 'absolute',
        left: rect.x,
        top: rect.y,
        width: rect.w,
        color: color.navy,
        fontWeight: 700,
        fontSize: size,
        lineHeight: LINE_HEIGHT,
        letterSpacing: '-0.01em',
        textWrap: 'balance',
      }}
    >
      {text}
      {underline ? (
        <svg
          width={underlineW}
          height={(underlineW * 26) / 360}
          viewBox="0 0 360 26"
          style={{position: 'absolute', left: -size * 0.02, top: size * 1.06, overflow: 'visible'}}
        >
          <path d="M6 14c88-7 178-9 252-6 32 1 62 4 90 8" stroke={color.pen} strokeWidth="3.6" fill="none" strokeLinecap="round" opacity={0.92} />
        </svg>
      ) : null}
    </div>
  );
};

// The closing line appears in the last second of the pull back, once it is inside the
// frame, so a move that passes near it earlier does not show it.
const closingOpacity = (cut: Cut, t: number) => {
  const pull = cut.camera.find((s) => s.kind === 'pull');
  return pull ? progress(t, pull.t1 - 1, 1) : 1;
};

const ClosingLine: React.FC<{caption: Caption; opacity: number}> = ({caption, opacity}) => {
  const {rect, size} = caption;
  return (
    <div
      style={{
        opacity,
        position: 'absolute',
        left: rect.x,
        top: rect.y,
        width: rect.w,
        textAlign: 'center',
        color: color.navy,
        fontWeight: 700,
        fontSize: size,
        lineHeight: 1.2,
        letterSpacing: '-0.01em',
        whiteSpace: 'nowrap',
      }}
    >
      {CLOSING}
    </div>
  );
};
