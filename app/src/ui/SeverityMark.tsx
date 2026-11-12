import clsx from 'clsx';
import type { IconSize } from './Icon';
import { outcomeWord, type CheckOutcome } from './severity';

export interface SeverityMarkProps {
  severity: CheckOutcome;
  size?: IconSize | undefined;
  /**
   * Beside its word (a badge, "2 errors"), the mark is decorative. Alone, in a dense row or cell, the word is its
   * accessible name.
   */
  decorative?: boolean | undefined;
  /** Its accessible name when it isn't decorative, where the severity's own word isn't the screen's: "Serious". */
  label?: string | undefined;
  className?: string | undefined;
}

/**
 * Severity as a shape and a colour (docs/v2-ui-design.md, Rules this changes): a red octagon for errors, an amber
 * triangle for warnings and a quiet slate circle for notices, so it reads without colour too. Skipped is a dashed
 * circle and passed a ticked one, both in the muted text colour. The word is its accessible name.
 */
export function SeverityMark({ severity, size = 'sm', decorative = false, label, className }: SeverityMarkProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={clsx('si-sev-mark', `si-sev-mark--${severity}`, `si-icon--${size}`, className)}
      focusable="false"
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': label ?? outcomeWord(severity) })}
    >
      <Shape severity={severity} />
    </svg>
  );
}

function Shape({ severity }: { severity: CheckOutcome }) {
  switch (severity) {
    case 'error':
      return (
        <>
          <path className="si-sev-mark__shape--error" d="M8.1 1.5h7.8l5.6 5.6v7.8l-5.6 5.6H8.1l-5.6-5.6V7.1z" />
          <path className="si-sev-mark__glyph" d="M12 7v6" strokeWidth={2.4} strokeLinecap="round" />
          <circle className="si-sev-mark__dot" cx={12} cy={16.6} r={1.35} />
        </>
      );
    case 'warning':
      return (
        <>
          <path
            className="si-sev-mark__shape--warning"
            d="M12 2.4 22.4 20.4H1.6z"
            strokeWidth={1.4}
            strokeLinejoin="round"
          />
          <path className="si-sev-mark__glyph" d="M12 9v5.2" strokeWidth={2.3} strokeLinecap="round" />
          <circle className="si-sev-mark__dot" cx={12} cy={17.2} r={1.25} />
        </>
      );
    case 'notice':
      return (
        <>
          <circle className="si-sev-mark__shape--notice" cx={12} cy={12} r={10.4} />
          <path className="si-sev-mark__glyph" d="M12 10.6v6.2" strokeWidth={2.4} strokeLinecap="round" />
          <circle className="si-sev-mark__dot" cx={12} cy={7.3} r={1.4} />
        </>
      );
    case 'skipped':
      return (
        <g strokeWidth={1.8} strokeLinecap="round">
          <circle cx={12} cy={12} r={9.5} strokeDasharray="3.2 2.6" />
          <path d="M8.5 12h7" />
        </g>
      );
    case 'passed':
      return (
        <g strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
          <circle cx={12} cy={12} r={9.5} />
          <path d="m8.5 12.2 2.4 2.4 4.6-4.8" />
        </g>
      );
  }
}
