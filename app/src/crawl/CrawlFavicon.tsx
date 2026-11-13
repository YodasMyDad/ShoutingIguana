import type { CrawlListItem } from '../api/types';
import { Favicon } from '../ui/Favicon';
import { crawlName } from './status';
import { faviconBroken, useFavicon } from './favicons';

/**
 * A crawl's site icon, where the design shows a site (Home's rows, the top bar, the sidebar's crawl): the engine's
 * icon once it's read, the site's letter until then and wherever there's none.
 */
export function CrawlFavicon({
  item,
  site,
  size,
  className,
}: {
  item: CrawlListItem | undefined;
  /** The site's name where the item's isn't known yet. */
  site?: string | undefined;
  size?: 'sm' | 'md' | 'lg' | undefined;
  className?: string | undefined;
}) {
  const src = useFavicon(item?.id, item?.hasFavicon);
  return (
    <Favicon
      site={item?.site ?? site ?? crawlName(item)}
      src={src}
      size={size}
      className={className}
      onError={() => {
        if (item) faviconBroken(item.id);
      }}
    />
  );
}
