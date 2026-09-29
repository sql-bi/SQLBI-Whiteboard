import React from 'react';
import {color, fontFamily, navyAlpha} from '../brand';

// A desk calendar. `flip` runs from 0, the page reading Today, to 1, where that page
// has turned up over the hinge and shows 16 October 2026 underneath.
export const Calendar: React.FC<{size: number; flip: number}> = ({size, flip}) => {
  const w = size;
  const h = size * 1.08;
  const band = h * 0.26;
  const r = size * 0.1;

  const page = (children: React.ReactNode, header: string): React.ReactNode => (
    <div
      style={{
        position: 'absolute',
        inset: 0,
        borderRadius: r,
        overflow: 'hidden',
        background: color.paper,
        fontFamily,
        textAlign: 'center',
        color: color.navy,
        backfaceVisibility: 'hidden',
      }}
    >
      <div
        style={{
          height: band,
          background: color.pen,
          color: color.paper,
          fontWeight: 700,
          fontSize: band * 0.46,
          lineHeight: `${band}px`,
          letterSpacing: '0.06em',
        }}
      >
        {header}
      </div>
      {children}
    </div>
  );

  // The first half of the turn lifts the page; past 90 degrees it is out of sight.
  const angle = flip * 180;
  return (
    <div
      style={{
        position: 'relative',
        width: w,
        height: h,
        borderRadius: r,
        boxShadow: `0 ${size * 0.04}px ${size * 0.12}px ${navyAlpha(0.35)}`,
        perspective: size * 4,
      }}
    >
      {page(
        <>
          <div style={{fontWeight: 700, fontSize: h * 0.42, lineHeight: 1, marginTop: h * 0.08, letterSpacing: '-0.02em'}}>16</div>
          <div style={{fontWeight: 600, fontSize: h * 0.13, marginTop: h * 0.04, color: color.sqlbiGrey}}>2026</div>
        </>,
        'OCTOBER',
      )}
      <div
        style={{
          position: 'absolute',
          inset: 0,
          transformOrigin: 'top center',
          transform: `rotateX(${angle}deg)`,
          transformStyle: 'preserve-3d',
          visibility: angle >= 90 ? 'hidden' : 'visible',
        }}
      >
        {page(
          <div style={{fontWeight: 700, fontSize: h * 0.2, lineHeight: `${h - band}px`}}>Today</div>,
          '',
        )}
      </div>
    </div>
  );
};
