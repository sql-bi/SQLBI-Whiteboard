// One layout for every shape. A landscape frame puts the text in a navy panel on the
// left, as the social card does; a square or portrait frame puts the window at the top
// and the text in a navy panel below it. Everything read or pointed at stays inside
// the safe area of the shape.

export type Rect = {x: number; y: number; w: number; h: number};

export type Shape = {
  id: string;
  width: number;
  height: number;
  // Area the platforms leave uncovered.
  safe: Rect;
};

export type Layout = {
  width: number;
  height: number;
  landscape: boolean;
  safe: Rect;
  // The window: always 16:9, like the captures.
  stage: Rect;
  // Navy background behind the text. It may bleed off the frame.
  panel: Rect;
  // Where the text sits, inside the safe area.
  text: Rect;
  textSize: number;
  // Text too long for one line is broken at a slash on narrow shapes.
  narrow: boolean;
};

const inset = (width: number, height: number, m: number): Rect => ({
  x: m,
  y: m,
  w: width - 2 * m,
  h: height - 2 * m,
});

export const shapes: Shape[] = [
  {id: '16x9', width: 1920, height: 1080, safe: inset(1920, 1080, 96)},
  {id: '1x1', width: 1080, height: 1080, safe: inset(1080, 1080, 90)},
  {id: '4x5', width: 1080, height: 1350, safe: inset(1080, 1350, 90)},
  // Reels and TikTok cover the top, the caption strip at the bottom, and the icon
  // column on the right.
  {id: '9x16', width: 1080, height: 1920, safe: {x: 60, y: 250, w: 870, h: 1300}},
];

export const layoutFor = (shape: Shape): Layout => {
  const {width, height, safe} = shape;
  const landscape = width / height > 1.2;

  if (landscape) {
    const panelW = Math.round(width * 0.36);
    const text: Rect = {x: safe.x, y: safe.y, w: panelW - safe.x - 60, h: safe.h};
    const stageX = panelW + 72;
    const stageW = safe.x + safe.w - stageX;
    const stageH = Math.round((stageW * 9) / 16);
    return {
      width,
      height,
      landscape,
      safe,
      stage: {x: stageX, y: Math.round((height - stageH) / 2), w: stageW, h: stageH},
      panel: {x: 0, y: 0, w: panelW, h: height},
      text,
      textSize: 64,
      narrow: false,
    };
  }

  const stageW = safe.w;
  const stageH = Math.round((stageW * 9) / 16);
  const textSize = shape.id === '1x1' ? 54 : 60;
  // Room for three or four lines of text below the window.
  const gap = 44;
  const textPad = 44;
  const textBlock = Math.round(textSize * 1.18 * 4);
  const block = stageH + gap + textPad + textBlock;
  const top = safe.y + Math.max(0, Math.round((safe.h - block) / 2) - 40);
  const stage: Rect = {x: safe.x, y: shape.id === '1x1' ? safe.y : top, w: stageW, h: stageH};
  const panelY = stage.y + stage.h + gap;
  const textY = panelY + textPad;
  return {
    width,
    height,
    landscape,
    safe,
    stage,
    panel: {x: 0, y: panelY, w: width, h: height - panelY},
    text: {x: safe.x, y: textY, w: safe.w, h: safe.y + safe.h - textY},
    textSize,
    narrow: true,
  };
};
