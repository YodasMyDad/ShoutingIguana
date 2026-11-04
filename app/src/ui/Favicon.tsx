import clsx from 'clsx';

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
 * A site's favicon: the letter tile the design shows until the engine brings real favicons (E6), then the icon itself
 * (`src`). Decorative: the site's name is always beside it.
 */
export function Favicon({
  site,
  src,
  size = 'md',
  className,
}: {
  /** The site's host or URL. */
  site: string;
  src?: string | null | undefined;
  /** 16, 20 (the default) or 28 px. */
  size?: 'sm' | 'md' | 'lg' | undefined;
  className?: string | undefined;
}) {
  return (
    <span aria-hidden="true" className={clsx('si-favicon', `si-favicon--${size}`, className)}>
      {src ? <img src={src} alt="" className="si-favicon__image" /> : monogram(site)}
    </span>
  );
}
