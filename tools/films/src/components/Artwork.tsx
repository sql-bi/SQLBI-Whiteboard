import React from 'react';
import {color, fontFamily, navyAlpha} from '../brand';

// The application icon, from src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg.
export const AppIcon: React.FC<{size: number}> = ({size}) => (
  <svg width={size} height={size} viewBox="0 0 256 256" style={{display: 'block'}}>
    <defs>
      <linearGradient id="app-icon-bg" x1="32" y1="24" x2="224" y2="232" gradientUnits="userSpaceOnUse">
        <stop stopColor={color.iconTop} />
        <stop offset="1" stopColor={color.iconBottom} />
      </linearGradient>
    </defs>
    <rect x="8" y="8" width="240" height="240" rx="52" fill="url(#app-icon-bg)" />
    <rect x="9" y="9" width="238" height="238" rx="51" fill="none" stroke="#FFFFFF" strokeOpacity="0.18" strokeWidth="2" />
    <g transform="translate(32 32) scale(8)" fill="#FFFFFF">
      <path d="m15.99 4-3.07 3.06c-.34.34-.6.74-.78 1.18l-.08.23-.74 2.3a2.25 2.25 0 0 0 2.64 2.88l.15-.04 2.33-.7a3.5 3.5 0 0 0 1.29-.7l.18-.18L22 7.95v8.8c0 1.8-1.45 3.25-3.25 3.25H5.25A3.25 3.25 0 0 1 2 16.75v-4.2l.08-.03.07-.04 3.76-2.36.1-.05A.75.75 0 0 1 7 11l-.04.1-1.2 2.3-.08.14a2.25 2.25 0 0 0 3 2.95l.17-.09 1.76-1 .09-.05a.75.75 0 0 0-.74-1.3l-.1.05-1.75 1-.1.04a.75.75 0 0 1-.97-.96l.04-.09 1.2-2.28.09-.17a2.25 2.25 0 0 0-3.12-2.87l-.15.08L2 10.81V7.24a3.25 3.25 0 0 1 3.07-3.24L5.25 4h10.74Zm5.19-.46.13.13.12.13c.76.89.72 2.23-.13 3.07l-4.28 4.28c-.26.26-.58.45-.94.56l-2.33.7a1 1 0 0 1-1.24-1.27l.74-2.29c.11-.34.3-.65.56-.9l4.29-4.29a2.27 2.27 0 0 1 3.08-.12Z" />
    </g>
  </svg>
);

// The SQLBI wordmark, from site/sqlbi-nor.svg.
export const SqlbiWordmark: React.FC<{height: number}> = ({height}) => (
  <svg height={height} width={(height * 1414.5) / 500} viewBox="0 0 1414.5 500" style={{display: 'block'}}>
    <path
      d="M349.1,111.4c0-18.8-77.3-33.1-173.3-33.1S2.4,92.7,2.4,110.3v265c0,18.8,79.5,33.1,175.5,33.1s173.3-15.5,173.3-33.1V109.2ZM107.3,356.5c-23.2-2.2-46.4-4.4-69.6-7.7V310.2c23.2,2.2,46.4,4.4,69.6,5.5Zm103.8,2.3q-34.8,1.65-69.6,0V204.2h69.6Zm102.6-10c-23.2,3.3-46.4,6.6-69.6,7.7V243.9c23.2,0,46.4-1.1,69.6-2.2Z"
      fill={color.sqlbiRed}
    />
    <path
      d="M177.9,78.3c96.1,0,173.3,14.4,173.3,30.9s-77.3,30.9-173.3,30.9S2.4,125.8,2.4,109.2,81.9,78.3,177.9,78.3Z"
      fill={color.sqlbiRedDark}
    />
    <path
      d="M481.6,293.6a197.51,197.51,0,0,0,88.3,21c25.4,0,54.1-4.4,54.1-21,0-17.7-26.5-26.5-55.2-30.9-42-6.6-100.5-18.8-100.5-80.6,0-62.9,56.3-79.5,110.4-79.5,49.7,0,82.8,14.4,101.6,25.4l-18.8,51.9c-15.5-9.9-46.4-21-82.8-21-25.4,0-48.6,5.5-48.6,21,0,19.9,32,21,49.7,23.2,74,12.1,106,43.1,106,91.6,0,62.9-66.2,75.1-115.9,75.1-45.3,0-81.7-11-107.1-25.4C462.8,345.5,481.6,293.6,481.6,293.6ZM939.7,493.4H877.9v-127c-12.1,5.5-27.6,6.6-43.1,6.6-72.9,0-128.1-40.9-128.1-131.4V234c0-87.2,51.9-131.4,128.1-131.4,21,0,70.7,3.3,104.9,23.2V493.4Zm-170-251.7c0,61.8,28.7,75.1,69.6,75.1,18.8,0,32-3.3,38.6-7.7V166.6c-9.9-4.4-22.1-7.7-40.9-7.7-42,0-67.3,14.4-67.3,75.1v7.7ZM977.3,6.6h64V374.3h-64V6.6Z"
      fill={color.sqlbiGrey}
    />
    <path
      d="M1077.8,6.6h61.8V109.3c12.1-5.5,27.6-6.6,43.1-6.6,72.9,0,128.1,40.9,128.1,131.4v7.7c0,88.3-51.9,131.4-128.1,131.4-21,0-70.7-3.3-104.9-23.2Zm171,227.3c0-61.8-29.7-76.1-70.6-76.1-17.7,0-32,4.4-37.5,7.7V308c8.8,4.4,22.1,7.7,40.9,7.7,42,0,67.3-14.4,67.3-75.1ZM1377,6.6c21,0,37.5,15.5,37.5,35.3s-16.6,36.4-37.5,36.4c-19.9,0-37.5-16.6-37.5-36.4S1356,6.6,1377,6.6Zm-31,109.3h61.8V373.1H1346V115.9Z"
      fill={color.sqlbiRed}
    />
  </svg>
);

// The saved export, drawn as the file in the guide's figure.
export const ZipFile: React.FC<{size: number; name: string}> = ({size, name}) => {
  const s = size / 120;
  return (
    <div style={{width: size, display: 'flex', flexDirection: 'column', alignItems: 'center'}}>
      <svg
        width={size * 0.78}
        height={size}
        viewBox="48 36 116 150"
        style={{display: 'block', filter: `drop-shadow(0 ${6 * s}px ${10 * s}px ${navyAlpha(0.35)})`}}
      >
        <path d="M60 40h72l28 28v106a8 8 0 0 1-8 8H60a8 8 0 0 1-8-8V48a8 8 0 0 1 8-8Z" fill={color.paper} stroke={color.navy} strokeWidth="3" />
        <path d="M132 40v20a8 8 0 0 0 8 8h20" fill="none" stroke={color.navy} strokeWidth="3" />
        <g fill={color.navy}>
          <rect x="98" y="48" width="8" height="6" rx="1" />
          <rect x="98" y="60" width="8" height="6" rx="1" />
          <rect x="98" y="72" width="8" height="6" rx="1" />
          <rect x="95" y="84" width="14" height="14" rx="3" />
        </g>
        <text x="106" y="150" fontFamily={fontFamily} fontSize="24" fontWeight="700" fill={color.pen} textAnchor="middle">
          ZIP
        </text>
      </svg>
      <div
        style={{
          marginTop: 10 * s,
          padding: `${4 * s}px ${12 * s}px`,
          borderRadius: 8 * s,
          background: color.paper,
          color: color.navy,
          fontFamily,
          fontWeight: 700,
          fontSize: 17 * s,
          whiteSpace: 'nowrap',
          boxShadow: `0 ${3 * s}px ${8 * s}px ${navyAlpha(0.3)}`,
        }}
      >
        {name}
      </div>
    </div>
  );
};

// The SQLBI Whiteboard window before a board is open, with the chrome of
// site/hero-launch.svg: title bar, menu bar, and an empty board. Drawn 16:9 so it
// matches the captures it hands over to.
export const SqlbiWindow: React.FC = () => (
  <svg viewBox="0 0 1600 900" width="100%" height="100%" style={{display: 'block'}}>
    <rect x="0" y="0" width="1600" height="900" fill="#FFFFFF" />
    <rect x="0" y="0" width="1600" height="56" fill="#2B7CD3" />
    <rect x="16" y="14" width="28" height="28" rx="7" fill="#D82121" />
    <path d="M23 32c4-6 10-8 14 -3" stroke="#FFFFFF" strokeWidth="2.4" fill="none" strokeLinecap="round" />
    <text x="58" y="37" fontFamily="Segoe UI, sans-serif" fontSize="24" fill="#FFFFFF">
      SQLBI Whiteboard
    </text>
    <g stroke="#FFFFFF" strokeWidth="2.4" fill="none">
      <path d="M1472 28h20" />
      <rect x="1522" y="19" width="18" height="18" rx="2" />
      <path d="M1574 19l18 18M1592 19l-18 18" />
    </g>
    <rect x="0" y="56" width="1600" height="52" fill="#FFFFFF" />
    <g fontFamily="Segoe UI, sans-serif" fontSize="24" fill="#1A1A1A">
      <text x="30" y="91">File</text>
      <text x="112" y="91">Edit</text>
      <text x="198" y="91">View</text>
      <text x="292" y="91">Help</text>
    </g>
  </svg>
);
