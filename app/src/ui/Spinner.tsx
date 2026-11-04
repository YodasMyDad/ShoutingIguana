import clsx from 'clsx';
import type { IconSize } from './Icon';

/**
 * A spinner, in the text colour around it. Only for work under way that has no progress to show, and only inside the
 * thing that's working (a button, a row), never over the screen. With reduced motion it stands still. With a label it
 * is a status a screen reader hears; without one it's decorative (the button around it says `aria-busy`).
 */
export function Spinner({ size = 'sm', label, className }: { size?: IconSize; label?: string; className?: string }) {
  const svg = (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2.5}
      strokeLinecap="round"
      className={clsx('si-spinner', `si-icon--${size}`, className)}
      focusable="false"
      aria-hidden="true"
    >
      <circle className="si-spinner__track" cx={12} cy={12} r={9} />
      <path d="M21 12a9 9 0 0 0-9-9" />
    </svg>
  );
  if (!label) return svg;
  return (
    <span role="status" className="inline-flex items-center">
      {svg}
      <span className="sr-only">{label}</span>
    </span>
  );
}
