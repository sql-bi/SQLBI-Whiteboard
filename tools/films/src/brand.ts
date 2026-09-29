import {Easing} from 'remotion';

// The campaign colors, as used by site/hero-launch-card.svg and the app icon.
export const color = {
  paper: '#F7F6F2',
  navy: '#0A1B33',
  navyDeep: '#071426',
  pen: '#E5442C',
  sqlbiRed: '#EB5E5E',
  sqlbiRedDark: '#D35455',
  sqlbiGrey: '#4B4A4B',
  iconTop: '#F42727',
  iconBottom: '#B71D1D',
};

export const navyGradient = `linear-gradient(160deg, ${color.navy} 0%, ${color.navyDeep} 100%)`;

// Shadows and tints are the navy and the icon red at reduced opacity, as in the cards.
export const navyAlpha = (a: number) => `rgba(10, 27, 51, ${a})`;
export const brandRedAlpha = (a: number) => `rgba(244, 39, 39, ${a})`;
export const paperAlpha = (a: number) => `rgba(247, 246, 242, ${a})`;

export const fontFamily = '"Segoe UI Variable Display", "Segoe UI", sans-serif';

// Every transition eases in and out, with no overshoot.
export const ease = Easing.bezier(0.45, 0, 0.55, 1);
