import { Link } from '@tanstack/react-router';
import { RotateCcw } from 'lucide-react';
import { Button } from '../../ui/Button';

/**
 * "Crawl again…", wherever the words recommend it (the audit of 2026-11-19, H-04: it was plain text in the card, the
 * notes banner and each skipped check's page, and the control was two screens away). Opens New crawl with this
 * crawl's settings, to look over and start.
 */
export function CrawlAgainButton({ crawlId }: { crawlId: string }) {
  return (
    <Button asChild size="sm" icon={RotateCcw}>
      <Link to="/new" search={{ from: crawlId }}>
        Crawl again…
      </Link>
    </Button>
  );
}
