import type { FC } from 'react';

const iconProps = {
  viewBox: '0 0 24 24',
  width: 24,
  height: 24,
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

export const SearchIcon: FC = () => (
  <svg {...iconProps}>
    <circle cx='11' cy='11' r='7' />
    <path d='m21 21-4.3-4.3' />
  </svg>
);

export const SparkleIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M12 3c.9 4.6 3.4 7.1 8 8-4.6.9-7.1 3.4-8 8-.9-4.6-3.4-7.1-8-8 4.6-.9 7.1-3.4 8-8Z' />
    <path d='M19 3v4M17 5h4' />
  </svg>
);

export const FileTextIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z' />
    <path d='M14 2v6h6M16 13H8M16 17H8M10 9H8' />
  </svg>
);

export const ImageIcon: FC = () => (
  <svg {...iconProps}>
    <rect x='3' y='3' width='18' height='18' rx='2' />
    <circle cx='8.5' cy='8.5' r='1.5' />
    <path d='m21 15-5-5L5 21' />
  </svg>
);

export const MonitorIcon: FC = () => (
  <svg {...iconProps}>
    <rect x='2' y='3' width='20' height='14' rx='2' />
    <path d='M8 21h8M12 17v4' />
  </svg>
);

export const UsersIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2' />
    <circle cx='9' cy='7' r='4' />
    <path d='M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75' />
  </svg>
);

export const SlidersIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6' />
  </svg>
);

export const VideoIcon: FC = () => (
  <svg {...iconProps}>
    <path d='m23 7-7 5 7 5V7z' />
    <rect x='1' y='5' width='15' height='14' rx='2' />
  </svg>
);

export const MusicIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M9 18V5l12-2v13' />
    <circle cx='6' cy='18' r='3' />
    <circle cx='18' cy='16' r='3' />
  </svg>
);

export const ArrowIcon: FC = () => (
  <svg {...iconProps}>
    <path d='M5 12h14M13 6l6 6-6 6' />
  </svg>
);
