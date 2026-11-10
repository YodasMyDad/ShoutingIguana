import { useParams } from '@tanstack/react-router';
import { useEffect } from 'react';
import { useCrawlState } from '../crawl/useCrawlState';
import { preloadSourceViewer } from '../inspector/sourceViewerLoader';
import { RobotsFileView } from './robots/RobotsFileView';
import { originFromParam } from './robots/robotsFile';
import { RobotsOrigins } from './robots/RobotsOrigins';

const ROUTE = '/crawls/$crawl/robots/{-$origin}';

/**
 * robots.txt (docs/v2-ui-ia.md, robots.txt): every origin the crawl asked for one, with what its answer meant and how
 * many URLs it blocked, and one origin's file (`:origin` in the path) beside the URLs it blocked, each with the line
 * that decided it.
 */
export function RobotsScreen() {
  const { crawl: crawlId, origin: originParam } = useParams({ from: ROUTE });
  const crawl = useCrawlState(crawlId);
  const origin = originFromParam(originParam);
  // A file is shown in the source viewer, a chunk of its own: loaded while the app is idle, so it opens at once.
  useEffect(() => preloadSourceViewer(), []);

  if (origin === null) return <RobotsOrigins crawlId={crawlId} crawl={crawl} />;
  // Another origin is another view: its own search, its own cursor.
  return <RobotsFileView key={origin} crawlId={crawlId} origin={origin} crawl={crawl} />;
}
