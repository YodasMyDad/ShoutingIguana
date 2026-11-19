import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { formatCount } from '../../lib/format';
import { answerParts, type AnalysisTotals } from '../issues/checkList';

/** Where a phrase of the answer leads: Issues, narrowed, or at one check. */
export interface HeadlineTargets {
  crawlId: string;
  /** The first skipped check, in the list's order: "23 were skipped" opens it, with the rest under it. */
  firstSkipped: string | null;
}

interface Phrase {
  /** The words in the sentence, exactly. */
  words: string;
  link: (children: ReactNode) => ReactNode;
}

/** The sentence with each phrase found in it made a link; the rest stays text. Phrases are looked for in order. */
function withLinks(sentence: string, phrases: readonly Phrase[]): ReactNode[] {
  const parts: ReactNode[] = [];
  let rest = sentence;
  for (const phrase of phrases) {
    const at = rest.indexOf(phrase.words);
    if (at < 0) continue;
    if (at > 0) parts.push(rest.slice(0, at));
    parts.push(<span key={phrase.words}>{phrase.link(phrase.words)}</span>);
    rest = rest.slice(at + phrase.words.length);
  }
  if (rest) parts.push(rest);
  return parts;
}

/**
 * Overview's answer (the audit of 2026-11-19, H-07): the sentence Issues heads its list with, each count in it a link
 * to exactly what it counts. The things to fix open the errors and warnings, the things worth a look the notices,
 * "29 checks found 1,499 issues" every check that found something, "118 passed" the list with the passed checks
 * shown, and "23 were skipped" the first skipped check. The words are `answerParts`': one wording, here and in Issues.
 */
export function OverviewHeadline({ totals, targets }: { totals: AnalysisTotals; targets: HeadlineTargets }) {
  const { crawlId, firstSkipped } = targets;
  const { fix, notices, counts } = answerParts(totals);
  const issues = (search: Record<string, unknown>, check?: string) => (children: ReactNode) => (
    <Link
      to="/crawls/$crawl/issues/{-$check}"
      params={{ crawl: crawlId, check }}
      search={search}
      className="si-link si-link--subtle si-link--inline"
    >
      {children}
    </Link>
  );
  const found = /^[\d,]+ checks? found [\d,]+ issues?/.exec(counts)?.[0];
  const were = totals.skipped === 1 ? 'was' : 'were';
  const countsLine = withLinks(counts, [
    ...(found ? [{ words: found, link: issues({}) }] : []),
    ...(totals.passed > 0 ? [{ words: `${formatCount(totals.passed)} passed`, link: issues({ passed: 1 }) }] : []),
    ...(totals.skipped > 0 && firstSkipped
      ? [{ words: `${formatCount(totals.skipped)} ${were} skipped`, link: issues({}, firstSkipped) }]
      : []),
  ]);
  if (!fix) return <>{countsLine}</>;
  return (
    <>
      <span className="font-medium text-text">
        {fix === 'Nothing to fix' ? fix : issues({ severity: ['error', 'warning'] })(fix)}
      </span>
      {notices ? <span className="text-text-muted"> · {issues({ severity: ['notice'] })(notices)}</span> : null}
      <span className="text-text-muted"> · {countsLine}</span>
    </>
  );
}
