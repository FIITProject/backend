// SVG-иконки из дизайна fiit-design (FIITProject/frontend), переведённые в JSX.
import type { SVGProps } from 'react';

type P = SVGProps<SVGSVGElement>;
const line = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

export const IconCollapse = (p: P) => (
  <svg aria-hidden="true" fill="none" height="24" stroke="url(#mg)" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.7" viewBox="0 0 24 24" width="24" {...p}>
    <defs>
      <linearGradient id="mg" x1="0" x2="1" y1="0" y2="1">
        <stop offset="0" stopColor="#CDBBFA" />
        <stop offset=".55" stopColor="#F2B6DE" />
        <stop offset="1" stopColor="#F8CDB0" />
      </linearGradient>
    </defs>
    <path d="M12 6l-6 6 6 6M19 6l-6 6 6 6" />
  </svg>
);

export const IconOverview = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <rect height="7" rx="2.5" width="7" x="3.5" y="3.5" />
    <rect height="7" rx="2.5" width="7" x="13.5" y="3.5" />
    <rect height="7" rx="2.5" width="7" x="3.5" y="13.5" />
    <rect height="7" rx="2.5" width="7" x="13.5" y="13.5" />
  </svg>
);

export const IconCourses = (p: P) => (
  <svg aria-hidden="true" fill="currentColor" height="20" stroke="none" viewBox="0 0 24 24" width="20" {...p}>
    <rect height="14" opacity=".4" rx="3" width="9" x="3" y="6" />
    <path d="M11 4h6a4 4 0 0 1 4 4v8a4 4 0 0 1-4 4h-6a4 4 0 0 1-4-4V8a4 4 0 0 1 4-4zM11 9h6v2h-6zM11 13h4v2h-4z" fillRule="evenodd" />
  </svg>
);

export const IconArchive = (p: P) => (
  <svg aria-hidden="true" fill="currentColor" height="20" stroke="none" viewBox="0 0 24 24" width="20" {...p}>
    <rect height="5" opacity=".4" rx="2" width="18" x="3" y="4" />
    <path d="M4.5 10.5h15V16a4 4 0 0 1-4 4h-7a4 4 0 0 1-4-4v-5.5zM9.5 13h5v2h-5z" fillRule="evenodd" />
  </svg>
);

export const IconBell = (p: P) => (
  <svg aria-hidden="true" fill="currentColor" height="22" stroke="none" viewBox="0 0 24 24" width="22" {...p}>
    <path d="M12 3a6.5 6.5 0 0 0-6.5 6.5v3.2c0 .7-.25 1.4-.7 1.9l-.9 1a1 1 0 0 0 .7 1.7h14.8a1 1 0 0 0 .7-1.7l-.9-1c-.45-.5-.7-1.2-.7-1.9V9.5A6.5 6.5 0 0 0 12 3z" />
    <path d="M9.5 19.2a2.5 2.5 0 0 0 5 0z" opacity=".4" />
  </svg>
);

export const IconCrumbSep = (p: P) => (
  <svg aria-hidden="true" className="crumbs__sep" viewBox="0 0 24 24" strokeWidth="2.4" {...line} {...p}>
    <path d="M9 6l6 6-6 6" />
  </svg>
);

export const IconLock = (p: P) => (
  <svg aria-hidden="true" className="field-icon" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <rect height="10" rx="3" width="14" x="5" y="11" />
    <path d="M8 11V8a4 4 0 0 1 8 0v3" />
  </svg>
);

export const IconUser = (p: P) => (
  <svg aria-hidden="true" className="field-icon" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <circle cx="12" cy="8" r="4" />
    <path d="M4 21c1-4 4-6 8-6s7 2 8 6" />
  </svg>
);

export const IconEye = (p: P) => (
  <svg aria-hidden="true" className="eye-on" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
    <circle cx="12" cy="12" r="3" />
  </svg>
);

export const IconEyeOff = (p: P) => (
  <svg aria-hidden="true" className="eye-off" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
    <circle cx="12" cy="12" r="3" />
    <path d="M4 4l16 16" />
  </svg>
);

export const IconAlert = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 8v5M12 16h.01" />
  </svg>
);

export const IconArrowRight = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M5 12h14M13 6l6 6-6 6" />
  </svg>
);

export const IconCheck = (p: P) => (
  <svg aria-hidden="true" height="12" width="12" viewBox="0 0 24 24" strokeWidth="3.2" {...line} {...p}>
    <path d="M5 12.5l4.5 4.5L19 7" />
  </svg>
);

export const IconClose = (p: P) => (
  <svg aria-hidden="true" fill="none" height="20" stroke="currentColor" strokeLinecap="round" strokeWidth="2.4" viewBox="0 0 24 24" width="20" {...p}>
    <path d="M6 6l12 12M18 6L6 18" />
  </svg>
);

export const IconDoc = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M7 3h7l5 5v13H7zM14 3v5h5M10 13h6M10 17h6" />
  </svg>
);

export const IconPen = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M4 20l4-1 11-11-3-3L5 16z" />
    <path d="M14 6l3 3" />
  </svg>
);

export const IconTable = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <rect height="16" rx="3" width="16" x="4" y="4" />
    <path d="M4 10h16M10 4v16" />
  </svg>
);
