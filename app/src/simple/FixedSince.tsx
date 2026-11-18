// What to fix's Fixed group (docs/v2-fix-loop.md, section 4, "What to fix"): the checks that found something on the
// site's last check and nothing now, folded below the severities, each with a tick and its count then: "Pages that don't
// work · 9 addresses on 3 Nov". A site's first check has no last check: its group is what its rechecks cleared, each
// with its count before the recheck. Hidden checks stay out, as everywhere.
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import clsx from 'clsx';
import { useId, type Ref } from 'react';
import { formatCount } from '../lib/format';
import { Card } from '../ui/Card';
import { Icon } from '../ui/Icon';
import { PlainCheckList, PlainCheckRow } from '../ui/PlainCheckRow';
import { FIXED_SECTION, type FixedEntry } from './fixedSinceEntries';
import { plainTitle } from './plainWords';

const ROUTE = '/crawls/$crawl/issues/{-$check}';

export function FixedSince({
  crawlId,
  title,
  entries,
  open,
  onOpenChange,
  headingRef,
}: {
  crawlId: string;
  /** "Fixed since your last check on 3 Nov", or "Fixed by a recheck". */
  title: string;
  entries: readonly FixedEntry[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  headingRef?: Ref<HTMLHeadingElement> | undefined;
}) {
  const listId = useId();
  if (entries.length === 0) return null;
  return (
    <section aria-labelledby={FIXED_SECTION} className="mb-6">
      <h2 id={FIXED_SECTION} ref={headingRef} tabIndex={-1} className="si-simple-h2 scroll-mt-4">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={open ? listId : undefined}
          onClick={() => onOpenChange(!open)}
          className="inline-flex items-center gap-2.5 rounded-sm text-left hover:text-text-secondary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          <span className={clsx('inline-flex text-text-muted transition-transform', open && 'rotate-90')}>
            <Icon icon={ChevronRight} size="md" />
          </span>
          {title} <span className="si-simple-h2__count">({formatCount(entries.length)})</span>
        </button>
      </h2>
      {open ? (
        <Card className="mt-3 overflow-hidden">
          <div id={listId}>
            <PlainCheckList label={title}>
              {entries.map(({ check, then }) => (
                <li key={check.id}>
                  <PlainCheckRow asChild outcome="passed" quiet title={plainTitle(check)} count={then}>
                    <Link to={ROUTE} params={{ crawl: crawlId, check: check.id }} />
                  </PlainCheckRow>
                </li>
              ))}
            </PlainCheckList>
          </div>
        </Card>
      ) : null}
    </section>
  );
}
