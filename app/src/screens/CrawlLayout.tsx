import { useQuery } from '@tanstack/react-query';
import { Outlet, useLocation, useParams } from '@tanstack/react-router';
import { useEffect } from 'react';
import { isProblem } from '../api/errors';
import { crawlQuery } from '../api/queries';
import { sectionOf } from '../app/sections';
import { ErrorState } from '../feedback/ErrorState';
import { updateSettings } from '../settings/settingsStore';
import { ScreenStub } from './ScreenStub';

/** Everything under /crawls/:crawl: the crawl must exist, and the view it's on is remembered for next time. */
export function CrawlLayout() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl' });
  const section = useLocation({ select: (location) => sectionOf(location.pathname) });
  const crawl = useQuery(crawlQuery(crawlId));

  useEffect(() => {
    if (section)
      updateSettings((settings) => ({ lastViewByCrawl: { ...settings.lastViewByCrawl, [crawlId]: section } }));
  }, [crawlId, section]);

  if (crawl.error && isProblem(crawl.error, 'crawl-not-found')) {
    return (
      <ScreenStub
        title="This crawl isn’t in the library"
        purpose="It may have been removed, or its file moved. Open it again from Home."
      >
        <ErrorState error={crawl.error} compact />
      </ScreenStub>
    );
  }
  if (crawl.error && !crawl.data) return <ErrorState error={crawl.error} onRetry={() => void crawl.refetch()} />;
  return <Outlet />;
}
