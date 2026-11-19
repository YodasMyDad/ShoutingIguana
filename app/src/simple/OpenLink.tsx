import clsx from 'clsx';
import type { ReactNode } from 'react';
import { openInBrowser } from '../shell/os';

/**
 * Words that open an address in the person's browser (the UX fix round's rule 1: in Simple, which has no inspector, a
 * URL's text is a link that opens it). A real link, so it reads as one, takes Tab and Enter, and can be copied; the
 * click goes through the app's opener, which is the shell's in the built app. Plain words for an address that isn't
 * http(s).
 */
export function OpenLink({
  url,
  hint = 'Open in your browser',
  className,
  short = false,
  children,
}: {
  url: string;
  /** Its tooltip: what opening it shows ("Open the page at this link"). */
  hint?: string | undefined;
  className?: string | undefined;
  /** Its words are a letter or two (the home page's "/"): it's given the least size a pointer or finger needs. */
  short?: boolean | undefined;
  children: ReactNode;
}) {
  if (!/^https?:\/\//i.test(url)) return <span className={className}>{children}</span>;
  return (
    <a
      href={url}
      target="_blank"
      rel="noreferrer"
      title={hint}
      // A short one (the home page's "/") is still a target a finger can hit.
      className={clsx('si-open-link', short && 'si-open-link--short', className)}
      onClick={(event) => {
        event.preventDefault();
        void openInBrowser(url);
      }}
    >
      {children}
    </a>
  );
}
