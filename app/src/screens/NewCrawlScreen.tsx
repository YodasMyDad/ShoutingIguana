import { useSearch } from '@tanstack/react-router';
import { ScreenStub } from './ScreenStub';

/** New crawl: the start URL, speed presets with the rate in words, and the folded sections. Its own slice. */
export function NewCrawlScreen() {
  const { url } = useSearch({ from: '/new' });
  return (
    <ScreenStub
      title="New crawl"
      purpose="The start URL and three speed presets, with the rate in words. Folded sections for scope, limits, robots and sitemaps, headers and network, and advanced settings, each validated inline. Start with Ctrl+Enter."
    >
      {url ? <p className="font-mono text-13 text-text-secondary">{url}</p> : null}
    </ScreenStub>
  );
}
