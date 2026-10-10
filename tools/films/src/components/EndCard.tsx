import React from 'react';
import {color, navyGradient, progress} from '../brand';
import type {Layout} from '../layout';
import {AppIcon, SqlbiWordmark} from './Artwork';

// The end card, on paper, centred in the safe area: the icon, the name with a pen
// underline, "Free and open source", the address, and optionally an install command and
// the SQLBI wordmark. `t` is seconds since the card began; each line rises into place
// in turn.
export const EndCard: React.FC<{
  L: Layout;
  t: number;
  address: React.ReactNode;
  command?: string;
  wordmark?: boolean;
}> = ({L, t, address, command, wordmark = false}) => {
  const k = L.landscape ? 1 : L.width === L.height ? 0.7 : 0.78;
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
        {address}
      </div>
      {command ? (
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
          {command}
        </div>
      ) : null}
      {wordmark ? (
        <div style={{...item(0.5), marginTop: 46 * k}}>
          <SqlbiWordmark height={52 * k} />
        </div>
      ) : null}
    </div>
  );
};
