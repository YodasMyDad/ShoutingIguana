import { Link } from '@tanstack/react-router';
import { memo } from 'react';
import { formatCount, formatCountOf, formatPercent } from '../../lib/format';
import { ListRow } from '../../ui/ListRow';
import { StatusSplit as StatusSplitBar, type SplitTone, type StatusSplitPart } from '../../ui/StatusSplit';
import { TextLink } from '../../ui/TextLink';
import { linkProps } from './overviewLinks';
import { partsText, type SplitClass, type SplitSegment, type StatusSplit } from './statusSplit';

const TONES: Record<SplitClass, SplitTone> = {
  '2xx': '2xx',
  '3xx': '3xx',
  '4xx': '4xx',
  '5xx': '5xx',
  other: 'other',
  noResponse: 'none',
  blocked: 'blocked',
};

const nameOf = (segment: Pick<SplitSegment, 'label' | 'count'>) =>
  `${segment.label}: ${formatCountOf(segment.count, 'URL')}`;

/** The breakdown in words, for the figure's name: what the two bars draw. */
function splitWords(split: StatusSplit): string {
  if (split.total === 0) return 'Status codes: no URLs crawled yet.';
  const start = `Status codes of ${formatCountOf(split.total, 'URL')}: ${formatCount(split.ok)} answered 2xx.`;
  if (split.rest === 0) return start;
  const others = split.segments
    .filter((segment) => segment.key !== '2xx' && segment.count > 0)
    .map((segment) => `${formatCount(segment.count)} ${segment.label}`);
  return `${start} The other ${formatCount(split.rest)}, magnified: ${others.join(', ')}.`;
}

/**
 * The status codes of what the crawl fetched (docs/v2-ui-design.md, "The magnified status bar"): the full bar, the
 * share that didn't answer 2xx enlarged beneath it, and every class listed with what it's made of, its number and its
 * share. Every class with URLs opens them in URLs. The same figure while the crawl runs and once it's done: only where
 * its counts come from differs.
 */
export const StatusCodes = memo(function StatusCodes({
  crawlId,
  split,
  live = false,
}: {
  crawlId: string;
  split: StatusSplit;
  /** The crawl is running: "so far". */
  live?: boolean;
}) {
  const bySegment = new Map(split.segments.map((segment) => [segment.key, segment]));
  const ok = bySegment.get('2xx');
  const whole: StatusSplitPart[] =
    split.total > 0
      ? [
          {
            key: '2xx',
            tone: '2xx',
            percent: split.whole.ok,
            name: nameOf({ label: '2xx', count: split.ok }),
            link: ok && split.ok > 0 ? linkProps(crawlId, ok.link) : null,
          },
          {
            key: 'rest',
            tone: 'rest',
            percent: split.whole.rest,
            name: nameOf({ label: 'Not 2xx', count: split.rest }),
          },
        ]
      : [];
  const magnified: StatusSplitPart[] = split.magnified.flatMap((part) => {
    const segment = bySegment.get(part.key);
    return segment
      ? [
          {
            key: part.key,
            tone: TONES[part.key],
            percent: part.percent,
            name: nameOf(segment),
            link: linkProps(crawlId, segment.link),
          },
        ]
      : [];
  });
  return (
    <div className="flex flex-col gap-4">
      <StatusSplitBar
        label={splitWords(split)}
        whole={whole}
        magnified={magnified}
        caption={
          split.rest > 0 ? (
            <>
              The <b>{formatCount(split.rest)}</b> not 2xx, magnified
            </>
          ) : split.total > 0 ? (
            live ? (
              'Every URL so far answered 2xx'
            ) : (
              'Every URL answered 2xx'
            )
          ) : (
            'No URLs crawled yet'
          )
        }
        share={
          split.rest > 0 ? (
            <>
              <b>{formatPercent(split.rest, split.total)}</b> of all
            </>
          ) : null
        }
      />
      <ul aria-label="Status codes by class" className="-mx-2.5 flex flex-col">
        {split.segments.map((segment) => {
          const parts = partsText(segment.parts, formatCount);
          const row = {
            icon: (
              <span className="si-legend__swatch" aria-hidden="true">
                <span className={`si-split__part si-split__part--${TONES[segment.key]}`} />
              </span>
            ),
            title: (
              <span className="flex min-w-0 items-baseline gap-2.5">
                <span className="shrink-0 font-medium">{segment.label}</span>
                <span
                  className="truncate text-text-muted"
                  title={segment.count > 0 ? parts || segment.meaning : undefined}
                >
                  {segment.count === 0 ? (live ? 'None so far' : 'None') : parts || segment.meaning}
                </span>
              </span>
            ),
            meta: (
              <span className="figures flex items-baseline gap-3">
                <span className="font-medium">{formatCount(segment.count)}</span>
                <span className="w-(--percent-width) text-right text-12 text-text-muted">
                  {segment.count > 0 ? formatPercent(segment.count, split.total) : ''}
                </span>
              </span>
            ),
          };
          return (
            <li key={segment.key}>
              {segment.count > 0 ? (
                <ListRow asChild {...row}>
                  <Link {...linkProps(crawlId, segment.link)} />
                </ListRow>
              ) : (
                <ListRow muted {...row} />
              )}
            </li>
          );
        })}
      </ul>
      {split.notFetched ? (
        <p className="text-13 text-text-secondary">
          <TextLink {...linkProps(crawlId, split.notFetched.link)} variant="inline">
            {formatCountOf(split.notFetched.count, 'more URL')}
          </TextLink>{' '}
          {split.notFetched.count === 1 ? 'was' : 'were'} found but not crawled: not followed, or past a limit.
        </p>
      ) : null}
    </div>
  );
});
