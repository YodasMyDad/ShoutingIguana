import clsx from 'clsx';
import { Image, ImageOff } from 'lucide-react';
import { useState } from 'react';
import { Icon } from './Icon';
import { fileNameOf } from './urlText';

/**
 * A small picture of an image a check is about, from the image's own URL (a site owner knows an image by sight before
 * by its address): read lazily, without a referrer, drawn whole in a fixed box. The image icon stands in while it
 * loads, when it can't be read (a broken image, a site offline, a policy that refuses it), and for an image known to
 * be broken, which isn't asked for. Its alt is the file's name; its address is always beside it.
 */
export function ImageThumb({
  url,
  size = 'sm',
  broken = false,
  className,
}: {
  url: string;
  /** 24 px in a grid's row, 56 px beside a cause in Simple. */
  size?: 'sm' | 'lg' | undefined;
  /** The image is known not to answer (a 404): the icon is drawn, and nothing is asked for. */
  broken?: boolean | undefined;
  className?: string | undefined;
}) {
  // The URL that loaded or failed: another URL starts afresh.
  const [state, setState] = useState<{ url: string; state: 'loaded' | 'failed' } | null>(null);
  const shown = broken ? 'failed' : state?.url === url ? state.state : 'loading';
  return (
    <span className={clsx('si-thumb', `si-thumb--${size}`, className)} data-thumb={shown}>
      {shown === 'failed' ? null : (
        <img
          src={url}
          alt={fileNameOf(url)}
          className="si-thumb__image"
          loading="lazy"
          decoding="async"
          referrerPolicy="no-referrer"
          draggable={false}
          onLoad={() => setState({ url, state: 'loaded' })}
          onError={() => setState({ url, state: 'failed' })}
        />
      )}
      <span className="si-thumb__fallback" aria-hidden="true">
        <Icon icon={shown === 'failed' ? ImageOff : Image} size={size === 'lg' ? 'lg' : 'xs'} />
      </span>
    </span>
  );
}
