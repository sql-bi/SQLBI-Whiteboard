import React from 'react';
import {AbsoluteFill, Audio, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';
import {color, ease, fontFamily, navyGradient} from '../brand';
import {AppIcon, SqlbiWindow, SqlbiWordmark, ZipFile} from '../components/Artwork';
import {Calendar} from '../components/Calendar';
import {CaptureView} from '../components/CaptureView';
import {WindowFrame} from '../components/WindowFrame';
import {layoutFor, shapes, type Layout, type Rect} from '../layout';
import {mediaFile} from '../media';
import beatsData from './beats.json';
import {C1, C2, C3, C4} from './captures';

export const FPS = 30;
export const DURATION_SECONDS = beatsData.durationSeconds;

type BeatId = 'board' | 'retire' | 'rewind' | 'export' | 'open' | 'saved' | 'end';

const beat = (id: BeatId): {start: number; end: number} => {
  const i = beatsData.beats.findIndex((b) => b.id === id);
  if (i < 0) throw new Error(`beats.json has no beat "${id}"`);
  const next = beatsData.beats[i + 1];
  return {start: beatsData.beats[i].start, end: next ? next.start : beatsData.durationSeconds};
};

// The on-screen text of each beat. It is final copy, and the captions of the film.
const captions: [BeatId, string][] = [
  ['board', 'A board in the Microsoft Whiteboard app.'],
  ['retire', 'On 16 October 2026 the app stops opening.'],
  ['rewind', 'Before then, export each board.'],
  ['export', 'Settings, Export, Zip (HTML+JSON).'],
  ['open', 'Open the ZIP in SQLBI Whiteboard.'],
  ['saved', 'The board is a file on your PC. It opens at any time, including after 16 October.'],
];

// Names and dates are not broken across lines.
const unbroken = ['16 October', 'SQLBI Whiteboard'];
const keepTogether = (text: string) =>
  unbroken.reduce((s, phrase) => s.split(phrase).join(phrase.replace(/ /g, ' ')), text);

// Microsoft's own words, from the retirement notice the guide links to.
const RETIREMENT_QUOTE = '“a retirement experience instead of opening”';

const BED_GAIN = Math.pow(10, -22 / 20);

// Eased progress from 0 to 1 over [start, start + length], in seconds.
const progress = (t: number, start: number, length: number) =>
  interpolate(t, [start, start + length], [0, 1], {easing: ease, extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});

const lerp = (a: number, b: number, p: number) => a + (b - a) * p;
const lerpRect = (a: Rect, b: Rect, p: number): Rect => ({
  x: lerp(a.x, b.x, p),
  y: lerp(a.y, b.y, p),
  w: lerp(a.w, b.w, p),
  h: lerp(a.h, b.h, p),
});

export const Film1: React.FC<{shapeId: string}> = ({shapeId}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const shape = shapes.find((s) => s.id === shapeId);
  if (!shape) throw new Error(`Unknown shape ${shapeId}`);
  const L = layoutFor(shape);
  const t = frame / fps;
  const end = beat('end');

  const sceneOpacity = 1 - progress(t, end.start, 0.45);
  const voice = mediaFile('audio/film1-vo.mp3');
  const bed = mediaFile('audio/film1-bed.mp3');

  return (
    <AbsoluteFill style={{background: color.paper, fontFamily}}>
      {voice ? <Audio src={voice} /> : null}
      {bed ? (
        <Audio
          src={bed}
          volume={(f) =>
            interpolate(f / fps, [end.start, DURATION_SECONDS - 0.2], [BED_GAIN, 0], {
              extrapolateLeft: 'clamp',
              extrapolateRight: 'clamp',
            })
          }
        />
      ) : null}

      <AbsoluteFill style={{opacity: sceneOpacity}}>
        <div
          style={{
            position: 'absolute',
            left: L.panel.x,
            top: L.panel.y,
            width: L.panel.w,
            height: L.panel.h,
            background: navyGradient,
          }}
        />
        <Captions L={L} t={t} />
        <MicrosoftWindow L={L} t={t} />
        <SqlbiScene L={L} t={t} />
      </AbsoluteFill>

      {t >= end.start ? <EndCard L={L} t={t - end.start} /> : null}
    </AbsoluteFill>
  );
};

const Captions: React.FC<{L: Layout; t: number}> = ({L, t}) => (
  <>
    {captions.map(([id, text], i) => {
      const b = beat(id);
      if (t < b.start - 0.01 || t > b.end) return null;
      const fadeIn = i === 0 ? 1 : progress(t, b.start, 0.4);
      const fadeOut = 1 - progress(t, b.end - 0.3, 0.3);
      const o = Math.min(fadeIn, fadeOut);
      return (
        <div
          key={id}
          style={{
            position: 'absolute',
            left: L.text.x,
            top: L.text.y,
            width: L.text.w,
            height: L.text.h,
            display: 'flex',
            alignItems: L.landscape ? 'center' : 'flex-start',
            opacity: o,
            transform: `translateY(${(1 - fadeIn) * L.textSize * 0.25}px)`,
          }}
        >
          <div
            style={{
              color: color.paper,
              fontWeight: 700,
              fontSize: L.textSize,
              lineHeight: 1.16,
              letterSpacing: '-0.01em',
              textWrap: 'balance',
            }}
          >
            {keepTogether(text)}
          </div>
        </div>
      );
    })}
  </>
);

// The Microsoft Whiteboard app: C1 held, the retirement and the rewind, C2, and in the
// open beat the same window stepping aside for the ZIP to leave it.
const MicrosoftWindow: React.FC<{L: Layout; t: number}> = ({L, t}) => {
  const {fps} = useVideoConfig();
  const retire = beat('retire');
  const rewind = beat('rewind');
  const exp = beat('export');
  const open = beat('open');
  if (t >= open.start + 2.4) return null;

  const stage = L.stage;
  const aside = sideBySide(L).left;
  const rect = lerpRect(stage, aside, progress(t, open.start, 0.45));
  const fade = 1 - progress(t, open.start + 1.9, 0.4);

  // The slight push-in of the first beat, undone by the rewind.
  const push = 1 + 0.05 * progress(t, 0, 4) - 0.05 * progress(t, rewind.start, 1.4);
  const grey = progress(t, retire.start + 1.1, 0.5) - progress(t, rewind.start + 0.2, 0.5);
  const quote = progress(t, retire.start + 1.5, 0.4) - progress(t, rewind.start, 0.3);
  const calendarShown = progress(t, retire.start + 0.15, 0.4) - progress(t, rewind.start + 2.2, 0.4);
  const flip = progress(t, retire.start + 0.6, 0.45) - progress(t, rewind.start + 0.8, 0.45);
  const toExport = progress(t, exp.start, 0.4);

  const calSize = rect.w * 0.13;

  return (
    <WindowFrame rect={rect} opacity={fade}>
      {toExport < 1 ? (
        <div style={{position: 'absolute', inset: 0, transform: `scale(${push})`}}>
          <CaptureView capture={C1} startFrame={0} still width={rect.w} height={rect.h} />
        </div>
      ) : null}
      {t >= exp.start ? (
        <div style={{position: 'absolute', inset: 0, opacity: toExport}}>
          {t < open.start ? (
            <CaptureView capture={C2} startFrame={Math.round(exp.start * fps)} width={rect.w} height={rect.h} />
          ) : (
            <CaptureView capture={C2} startFrame={0} still holdAt={C2.seconds} showCallouts={false} width={rect.w} height={rect.h} />
          )}
        </div>
      ) : null}
      {grey > 0 ? (
        <div
          style={{
            position: 'absolute',
            inset: 0,
            background: color.sqlbiGrey,
            opacity: grey,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <div
            style={{
              opacity: quote,
              color: color.paper,
              fontWeight: 600,
              fontSize: rect.w * 0.05,
              lineHeight: 1.2,
              textAlign: 'center',
              maxWidth: rect.w * 0.66,
              textWrap: 'balance',
            }}
          >
            {RETIREMENT_QUOTE}
          </div>
        </div>
      ) : null}
      {calendarShown > 0 ? (
        <div
          style={{
            position: 'absolute',
            right: rect.w * 0.035,
            top: rect.h * 0.08,
            opacity: calendarShown,
            transform: `translateY(${(1 - calendarShown) * calSize * 0.1}px)`,
          }}
        >
          <Calendar size={calSize} flip={flip} />
        </div>
      ) : null}
    </WindowFrame>
  );
};

const sideBySide = (L: Layout): {left: Rect; right: Rect} => {
  const s = L.stage;
  const w = s.w * 0.46;
  const h = (w * 9) / 16;
  const y = s.y + (s.h - h) / 2;
  return {left: {x: s.x, y, w, h}, right: {x: s.x + s.w - w, y, w, h}};
};

// SQLBI Whiteboard: the drawn window receives the ZIP, grows to fill the stage, and
// hands over to C3 and then C4.
const SqlbiScene: React.FC<{L: Layout; t: number}> = ({L, t}) => {
  const {fps} = useVideoConfig();
  const open = beat('open');
  const saved = beat('saved');
  if (t < open.start) return null;
  const e = open.start;
  const {left, right} = sideBySide(L);

  const enter = progress(t, e + 0.15, 0.45);
  const grow = progress(t, e + 2.0, 0.5);
  const base: Rect = {...right, x: right.x + L.stage.w * 0.04 * (1 - enter)};
  const rect = lerpRect(base, L.stage, grow);
  const toC3 = progress(t, e + 2.4, 0.4);
  const toC4 = progress(t, saved.start, 0.4);

  // The ZIP leaves the Microsoft window in an arc and lands in SQLBI Whiteboard.
  const zipIn = progress(t, e + 0.55, 0.3);
  const fly = progress(t, e + 0.9, 0.9);
  const absorb = progress(t, e + 1.8, 0.3);
  const from = {x: left.x + left.w / 2, y: left.y + left.h / 2};
  const to = {x: right.x + right.w / 2, y: right.y + right.h / 2};
  const lift = L.stage.h * 0.32;
  const ctrl = {x: (from.x + to.x) / 2, y: Math.min(from.y, to.y) - lift};
  const zx = (1 - fly) ** 2 * from.x + 2 * (1 - fly) * fly * ctrl.x + fly ** 2 * to.x;
  const zy = (1 - fly) ** 2 * from.y + 2 * (1 - fly) * fly * ctrl.y + fly ** 2 * to.y;
  const zipSize = Math.max(96, L.stage.w * 0.13);
  const zipScale = lerp(0.92, 1, zipIn) * lerp(1, 0.8, absorb);

  return (
    <>
      <WindowFrame rect={rect} opacity={enter}>
        {toC3 < 1 ? <SqlbiWindow /> : null}
        {t >= e + 2.4 && toC4 < 1 ? (
          <div style={{position: 'absolute', inset: 0, opacity: toC3}}>
            <CaptureView capture={C3} startFrame={Math.round((e + 2.4) * fps)} width={rect.w} height={rect.h} />
          </div>
        ) : null}
        {t >= saved.start ? (
          <div style={{position: 'absolute', inset: 0, opacity: toC4}}>
            <CaptureView capture={C4} startFrame={Math.round(saved.start * fps)} width={rect.w} height={rect.h} />
          </div>
        ) : null}
      </WindowFrame>
      {zipIn > 0 && absorb < 1 ? (
        <div
          style={{
            position: 'absolute',
            left: zx - zipSize / 2,
            top: zy - zipSize * 0.62,
            opacity: zipIn * (1 - absorb),
            transform: `scale(${zipScale})`,
          }}
        >
          <ZipFile size={zipSize} name="Workshop.zip" />
        </div>
      ) : null}
    </>
  );
};

// The end card, on paper. `t` is seconds since the card began.
const EndCard: React.FC<{L: Layout; t: number}> = ({L, t}) => {
  const k = L.landscape ? 1 : shapesScale(L);
  const s = L.safe;
  const item = (delay: number): React.CSSProperties => {
    const p = progress(t, 0.25 + delay, 0.4);
    return {opacity: p, transform: `translateY(${(1 - p) * 18 * k}px)`};
  };
  const underline = progress(t, 0.8, 0.5);
  const nameSize = 112 * k;
  const underlineW = nameSize * 7.4;

  return (
    <div
      style={{
        position: 'absolute',
        left: s.x,
        top: s.y,
        width: s.w,
        height: s.h,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        color: color.navy,
      }}
    >
      <div style={item(0)}>
        <AppIcon size={196 * k} />
      </div>
      <div style={{...item(0.1), marginTop: 30 * k, position: 'relative'}}>
        <div style={{fontWeight: 700, fontSize: nameSize, lineHeight: 1.1, letterSpacing: '-0.02em', whiteSpace: 'nowrap'}}>
          SQLBI Whiteboard
        </div>
        <svg
          width={underlineW}
          height={(underlineW * 26) / 360}
          viewBox="0 0 360 26"
          style={{position: 'absolute', left: '50%', marginLeft: -underlineW / 2, top: nameSize * 1.08, overflow: 'visible'}}
        >
          <path
            d="M6 14c88-7 178-9 252-6 32 1 62 4 90 8"
            stroke={color.pen}
            strokeWidth="3.6"
            fill="none"
            strokeLinecap="round"
            pathLength={1}
            strokeDasharray="1 1"
            strokeDashoffset={1 - underline}
            opacity={underline > 0 ? 0.92 : 0}
          />
        </svg>
      </div>
      <div style={{...item(0.2), marginTop: 44 * k, fontWeight: 600, fontSize: 60 * k, lineHeight: 1.2}}>Free and open source</div>
      <div style={{...item(0.3), marginTop: 30 * k, fontWeight: 600, fontSize: 56 * k, lineHeight: 1.22, color: color.navy}}>
        {L.narrow ? (
          <>
            whiteboard.sqlbi.com/
            <br />
            export-microsoft-whiteboard.html
          </>
        ) : (
          'whiteboard.sqlbi.com/export-microsoft-whiteboard.html'
        )}
      </div>
      <div
        style={{
          ...item(0.4),
          marginTop: 34 * k,
          padding: `${14 * k}px ${30 * k}px`,
          borderRadius: 14 * k,
          background: navyGradient,
          color: color.paper,
          fontWeight: 600,
          fontSize: 48 * k,
          lineHeight: 1.2,
          whiteSpace: 'nowrap',
        }}
      >
        winget install SQLBI.Whiteboard
      </div>
      <div style={{...item(0.5), marginTop: 46 * k}}>
        <SqlbiWordmark height={52 * k} />
      </div>
    </div>
  );
};

const shapesScale = (L: Layout) => (L.width === L.height ? 0.7 : 0.78);
