import { ScreenStub } from './ScreenStub';

/** Sitemaps: the sitemap tree with each file's status and URL count, and one sitemap's URLs. */
export function SitemapsScreen() {
  return (
    <ScreenStub
      title="Sitemaps"
      purpose="The sitemap tree, with each file’s status and URL count. Open a sitemap for its URLs, each marked crawled, not crawled or non-indexable. It needs E3’s /sitemaps."
    />
  );
}
