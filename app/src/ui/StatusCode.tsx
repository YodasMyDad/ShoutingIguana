import clsx from 'clsx';
import { statusCodeClass } from './statusClass';

export interface StatusCodeProps {
  /** The HTTP status, or null when there was no response. */
  code: number | null | undefined;
  /** The words for no response, or the reason after a code ("Not Found"). */
  reason?: string | undefined;
  /**
   * `flag` sets it on its class's tint, for a code that's the point of the row (a 404 in a list of broken links).
   * Grids keep the plain mark, so a normal 200 stays quiet.
   */
  variant?: 'plain' | 'flag' | undefined;
  className?: string | undefined;
}

/**
 * A status code, marked by its class: a square in the 2xx, 3xx, 4xx or 5xx colour, or grey for no response. Only for
 * status codes: the class colours mean nothing else. The code itself is always the text, so colour never carries it.
 */
export function StatusCode({ code, reason, variant = 'plain', className }: StatusCodeProps) {
  const statusClass = statusCodeClass(code);
  const hasCode = code !== null && code !== undefined;
  return (
    <span
      className={clsx(
        'si-code',
        statusClass !== 'none' && `si-code--${statusClass}`,
        variant === 'flag' && 'si-code--flag',
        className,
      )}
    >
      <span aria-hidden="true" className="si-code__mark" />
      {hasCode ? code : (reason ?? 'No response')}
      {hasCode && reason ? (
        <>
          {' '}
          <span className="si-code__reason">{reason}</span>
        </>
      ) : null}
    </span>
  );
}
