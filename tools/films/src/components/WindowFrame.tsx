import React from 'react';
import {navyAlpha} from '../brand';
import type {Rect} from '../layout';

// A window floating on the paper, with the shadow and hairline the cards give theirs.
export const WindowFrame: React.FC<{
  rect: Rect;
  opacity?: number;
  children: React.ReactNode;
}> = ({rect, opacity = 1, children}) => {
  const r = Math.max(6, rect.w * 0.012);
  return (
    <div
      style={{
        position: 'absolute',
        left: rect.x,
        top: rect.y,
        width: rect.w,
        height: rect.h,
        borderRadius: r,
        overflow: 'hidden',
        opacity,
        boxShadow: `0 ${rect.w * 0.01}px ${rect.w * 0.03}px ${navyAlpha(0.3)}, 0 0 0 1px ${navyAlpha(0.18)}`,
      }}
    >
      {children}
    </div>
  );
};
