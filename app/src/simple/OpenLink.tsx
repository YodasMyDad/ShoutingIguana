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
  children,
}: {
  url: string;
  /** Its tooltip: what opening it shows ("Open the page at this link"). */
  hint?: string | undefined;
  className?: string | undefined;
  children: ReactNode;
}) {
  if (!/^https?:\/\//i.test(url)) return <span className={className}>{children}</span>;
  return (
    <a
      href={url}
      target="_blank"
      rel="noreferrer"
      title={hint}
      className={clsx('si-open-link', className)}
      onClick={(event) => {
        event.preventDefault();
        void openInBrowser(url);
      }}
    >
      {children}
    </a>
  );
}
