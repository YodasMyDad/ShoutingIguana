import { useQuery } from '@tanstack/react-query';
import { useParams } from '@tanstack/react-router';
import { crawlOptionsQuery } from '../api/queries';
import { ErrorState } from '../feedback/ErrorState';
import { ScreenStub } from './ScreenStub';

/** The settings this crawl used: Continue with them, or Crawl again. The form is New crawl's, in its slice. */
export function CrawlSettingsScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/settings' });
  const options = useQuery(crawlOptionsQuery(crawlId));
  return (
    <ScreenStub
      title="Crawl settings"
      purpose="The settings this crawl used. Continue it with them, or crawl the site again with them."
    >
      {options.error ? <ErrorState error={options.error} compact onRetry={() => void options.refetch()} /> : null}
      {options.data?.startUrl ? (
        <p className="text-13 text-text-secondary">
          Start URL: <span className="font-mono">{options.data.startUrl}</span>
        </p>
      ) : null}
    </ScreenStub>
  );
}
