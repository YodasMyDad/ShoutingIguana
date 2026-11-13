import clsx from 'clsx';
import { useState } from 'react';

/** The letter a site's tile shows: its host's first, without "www." ("gardenfurnituresets.co.uk" is G). */
function monogram(site: string): string {
  const host = site
    .replace(/^[a-z][a-z\d+.-]*:\/\//i, '')
    .replace(/^www\./i, '')
    .trim();
  const first = /[\p{L}\p{N}]/u.exec(host)?.[0];
  return (first ?? '?').toLocaleUpperCase('en-GB');
}

/**
 * A site's favicon: the icon itself (`src`, from the engine's E6 favicon), or the letter tile the design shows where
 * there's none, while it's on its way, or when the image can't be drawn. The tile is the same size either way, so
 * nothing moves when the icon arrives or fails. Decorative: the site's name is always beside it.
 */
export function Favicon({
  site,
  src,
  size = 'md',
  onError,
  className,
}: {
  /** The site's host or URL. */
  site: string;
  src?: string | null | undefined;
  /** 16, 20 (the default) or 28 px. */
  size?: 'sm' | 'md' | 'lg' | undefined;
  /** The image couldn't be drawn (its bytes are damaged): the letter shows in its place. */
  onError?: (() => void) | undefined;
  className?: string | undefined;
}) {
  // The source that failed: another source is tried afresh.
  const [failed, setFailed] = useState<string | null>(null);
  const showImage = Boolean(src) && failed !== src;
  return (
    <span aria-hidden="true" className={clsx('si-favicon', `si-favicon--${size}`, className)}>
      {showImage && src ? (
        <img
          src={src}
          alt=""
          className="si-favicon__image"
          onError={() => {
            setFailed(src);
            onError?.();
          }}
        />
      ) : (
        monogram(site)
      )}
    </span>
  );
}
