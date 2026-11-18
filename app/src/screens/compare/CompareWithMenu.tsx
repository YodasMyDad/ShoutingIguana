import { ChevronDown } from 'lucide-react';
import { crawledUrls, effectiveStatus, statusLabel } from '../../crawl/status';
import { formatCount, formatExactTime } from '../../lib/format';
import { useLiveCrawl } from '../../live/liveStore';
import { setCompareChoice } from '../../settings/compareWith';
import { Button } from '../../ui/Button';
import { DropdownMenu, MenuItem, MenuLabel, MenuRadioGroup, MenuRadioItem, MenuSeparator } from '../../ui/Menu';
import { comparedWithWords, notComparableReason, type CompareCandidate } from './comparing';
import type { AdvancedComparison } from './useComparison';

/** The radio value of Don't compare: no crawl's id can be it. */
const NONE = 'none';

/**
 * Advanced's Compare with… (docs/v2-fix-loop.md, "Comparing with another crawl"): "Compared with 3 Nov, 09:12 ▾", a
 * menu of the site's other crawls, newest first, each with when it started, its status, its URLs and its score; one
 * that can't be compared is there, disabled, with why; then Don't compare. The choice is the app's preference for this
 * crawl, as its hidden checks are; the previous crawl, being the default, keeps nothing. A crawl later than this one
 * can be picked: the comparison then reads "until".
 */
export function CompareWithMenu({
  crawlId,
  comparison,
  open,
  onOpenChange,
}: {
  crawlId: string;
  comparison: Pick<AdvancedComparison, 'comparing' | 'candidates' | 'item'>;
  open?: boolean | undefined;
  onOpenChange?: ((open: boolean) => void) | undefined;
}) {
  const { comparing, candidates, item } = comparison;
  const value = comparing.kind === 'none' ? (comparing.why === 'off' ? NONE : '') : comparing.id;
  const choose = (next: string) => {
    if (next === NONE) setCompareChoice(crawlId, 'none');
    else setCompareChoice(crawlId, next === item?.previous?.id ? 'previous' : next);
  };

  return (
    <DropdownMenu
      align="end"
      open={open}
      onOpenChange={onOpenChange}
      className="si-compare-menu"
      trigger={
        <Button size="sm" variant="ghost" iconEnd={ChevronDown}>
          {comparedWithWords(comparing)}
        </Button>
      }
    >
      <MenuLabel>Compare with</MenuLabel>
      {candidates.length === 0 ? (
        <MenuItem disabled>No other crawl of this site</MenuItem>
      ) : (
        <MenuRadioGroup value={value} onValueChange={choose}>
          {candidates.map((candidate) => (
            <CandidateItem key={candidate.item.id} candidate={candidate} />
          ))}
          <MenuSeparator />
          <MenuRadioItem value={NONE}>Don’t compare</MenuRadioItem>
        </MenuRadioGroup>
      )}
    </DropdownMenu>
  );
}

/**
 * One crawl in the menu: when it started, and whether it's the previous crawl or a later one; under it, its status,
 * URLs and score, or why it can't be compared. Its status is the live one while this sidecar works on it.
 */
function CandidateItem({ candidate }: { candidate: CompareCandidate }) {
  const { item, previous, later } = candidate;
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? candidate.status;
  const reason = notComparableReason(item, status);
  const tag = previous ? 'Previous' : later ? 'Later' : null;
  const urls = crawledUrls(item);
  const meta = [
    statusLabel(status),
    urls === null ? null : `${formatCount(urls)} ${urls === 1 ? 'URL' : 'URLs'}`,
    item.score ? `Score ${formatCount(item.score.value)}${item.score.partial ? ' so far' : ''}` : 'No score',
  ]
    .filter(Boolean)
    .join(' · ');
  return (
    <MenuRadioItem value={item.id} disabled={reason !== null}>
      <span className="flex min-w-0 flex-col">
        <span className="truncate">
          {formatExactTime(item.startedAt)}
          {tag ? <span className="si-compare-menu__tag"> · {tag}</span> : null}
        </span>
        <span className="si-compare-menu__meta truncate">{reason ?? meta}</span>
      </span>
    </MenuRadioItem>
  );
}
