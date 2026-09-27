import React from 'react';
import {AbsoluteFill, Freeze, OffthreadVideo, Sequence, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {brandRedAlpha, color, ease, fontFamily, paperAlpha} from '../brand';
import type {Capture} from '../film1/captures';
import {mediaFile} from '../media';

// Plays a capture from `startFrame` (a frame of the film), or holds one frame of it
// when `still` is set. The size comes from the parent.
export const CaptureView: React.FC<{
  capture: Capture;
  startFrame: number;
  still?: boolean;
  // Capture time to hold; the last frame when `still` is set and this is omitted.
  holdAt?: number;
  width: number;
  height: number;
  showCallouts?: boolean;
}> = ({capture, startFrame, still = false, holdAt, width, height, showCallouts = true}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const src = mediaFile(`captures/${capture.file}`);

  const captureTime = still
    ? (holdAt ?? capture.from)
    : capture.from + ((frame - startFrame) / fps) * capture.rate;

  let picture: React.ReactNode;
  if (src === null) {
    picture = <Placeholder capture={capture} width={width} />;
  } else if (still) {
    // A frame just inside the end, so a hold on the last frame always finds one.
    const t = Math.min(captureTime, capture.seconds - 0.05);
    picture = (
      <Freeze frame={0}>
        <OffthreadVideo src={src} muted trimBefore={Math.round(t * fps)} style={{width: '100%', height: '100%', objectFit: 'cover'}} />
      </Freeze>
    );
  } else {
    picture = (
      <Sequence from={startFrame} layout="none">
        <OffthreadVideo
          src={src}
          muted
          trimBefore={Math.round(capture.from * fps)}
          playbackRate={capture.rate}
          style={{width: '100%', height: '100%', objectFit: 'cover'}}
        />
      </Sequence>
    );
  }

  return (
    <AbsoluteFill>
      {picture}
      {showCallouts
        ? capture.callouts.map((c, i) => {
            if (captureTime < c.at) return null;
            const since = still ? 1 : (captureTime - c.at) / capture.rate;
            const t = ease(Math.min(1, since / 0.35));
            return <Ring key={i} n={c.n} box={c.box} width={width} height={height} progress={t} />;
          })
        : null}
    </AbsoluteFill>
  );
};

const Ring: React.FC<{
  n?: number;
  box: [number, number, number, number];
  width: number;
  height: number;
  progress: number;
}> = ({n, box, width, height, progress}) => {
  const [bx, by, bw, bh] = box;
  const x = bx * width;
  const y = by * height;
  const w = bw * width;
  const h = bh * height;
  const stroke = Math.max(3, width * 0.004);
  const r = Math.max(14, width * 0.021);
  // The badge sits beside the ring, on the side facing the middle of the frame, so it
  // never covers what the ring marks.
  const gap = r * 0.5;
  const onLeft = x + w / 2 > width * 0.5;
  const cx = onLeft ? x - gap - r : x + w + gap + r;
  const cy = Math.min(height - r - stroke, Math.max(r + stroke, y + h / 2));
  const scale = interpolate(progress, [0, 1], [0.92, 1]);
  return (
    <>
      <div
        style={{
          position: 'absolute',
          left: x,
          top: y,
          width: w,
          height: h,
          borderRadius: Math.min(h, w) * 0.22,
          border: `${stroke}px solid ${color.iconTop}`,
          background: brandRedAlpha(0.08),
          opacity: progress,
          transform: `scale(${scale})`,
          boxSizing: 'border-box',
        }}
      />
      {n !== undefined ? (
        <div
          style={{
            position: 'absolute',
            left: cx - r,
            top: cy - r,
            width: 2 * r,
            height: 2 * r,
            borderRadius: r,
            background: color.iconTop,
            color: color.paper,
            fontFamily,
            fontWeight: 700,
            fontSize: r * 1.15,
            lineHeight: `${2 * r}px`,
            textAlign: 'center',
            opacity: progress,
            transform: `scale(${scale})`,
          }}
        >
          {n}
        </div>
      ) : null}
    </>
  );
};

const Placeholder: React.FC<{capture: Capture; width: number}> = ({capture, width}) => {
  const s = width / 1000;
  return (
    <AbsoluteFill
      style={{
        background: color.navy,
        color: color.paper,
        fontFamily,
        justifyContent: 'center',
        padding: `0 ${80 * s}px`,
      }}
    >
      <div style={{fontSize: 30 * s, fontWeight: 700, letterSpacing: '-0.01em'}}>
        {capture.id} · {capture.file}
      </div>
      <div style={{fontSize: 22 * s, fontWeight: 600, marginTop: 14 * s, color: paperAlpha(0.72), maxWidth: 700 * s}}>
        {capture.description} {capture.seconds} s capture.
      </div>
    </AbsoluteFill>
  );
};
