import { RefreshCw } from 'lucide-react';
import type { Analysis } from '../api/types';
import { formatExactTime, formatRelativeTime } from '../lib/format';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { LiveDot } from '../ui/LiveBadge';
import { outOfDateWords } from './analysis';

export interface LastAnalysisBannerProps {
  analysis: Analysis;
  /** The crawl is running: its issues are the last analysis's until it finishes. */
  crawling: boolean;
  /** An analysis is running, and something else says so: the banner gives way to it. */
  analysing: boolean;
  canAnalyse: boolean;
  onAnalyse: () => void;
}

/**
 * Whether the last analysis still stands, wherever its issues are shown (the Issues screen, the inspector): the
 * issues of a crawl running now are its last analysis's, and an analysis that's out of date says why, with Analyse
 * again. Nothing when it stands.
 */
export function LastAnalysisBanner({ analysis, crawling, analysing, canAnalyse, onAnalyse }: LastAnalysisBannerProps) {
  const when = formatRelativeTime(analysis.analyzedAt);
  if (crawling) {
    return (
      <Banner inset icon={<LiveDot />}>
        The crawl is running, so these are the issues its last analysis found,{' '}
        <span title={formatExactTime(analysis.analyzedAt)}>{when}</span>. It’s analysed again when it finishes.
      </Banner>
    );
  }
  if (!analysis.outOfDate || analysing) return null;
  return (
    <Banner
      inset
      detail={outOfDateWords(analysis.outOfDateReasons)}
      actions={
        canAnalyse ? (
          <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
            Analyse again
          </Button>
        ) : null
      }
    >
      These issues are out of date: they’re from the analysis {when}.
    </Banner>
  );
}
