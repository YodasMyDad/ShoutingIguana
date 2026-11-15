import clsx from 'clsx';
import { ExternalLink } from 'lucide-react';
import { openInBrowser } from '../shell/os';
import { IconButton } from './IconButton';
import { openLabel } from './urlText';

/**
 * A small button that opens a URL in the person's browser, beside an address shown as text (Simple's lists): named
 * "Open https://… in your browser", through the same opener as the inspector's. Nothing for a URL that isn't http(s).
 */
export function OpenUrlButton({
  url,
  inGrid = false,
  className,
}: {
  url: string;
  /** In a grid's cell: no Tab stop, a press leaves focus on the grid, and it shows as a cell's URL button does. */
  inGrid?: boolean | undefined;
  className?: string | undefined;
}) {
  if (!/^https?:\/\//i.test(url)) return null;
  return (
    <IconButton
      label={openLabel(url)}
      tooltip="Open in your browser"
      icon={ExternalLink}
      size="xs"
      className={clsx(inGrid ? 'si-url__open si-url__open--inline' : 'si-open-url', className)}
      tabIndex={inGrid ? -1 : undefined}
      onMouseDown={inGrid ? (event) => event.preventDefault() : undefined}
      onDoubleClick={(event) => event.stopPropagation()}
      onClick={() => void openInBrowser(url)}
    />
  );
}
