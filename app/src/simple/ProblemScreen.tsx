import { useQuery } from '@tanstack/react-query';
import { Link, useRouter } from '@tanstack/react-router';
import { ArrowLeft, ArrowUpRight, FileText, Gauge, UserRound } from 'lucide-react';
import { useMemo, useRef, type ReactNode } from 'react';
import { analysisQuery, checksQuery, crawlQuery } from '../api/queries';
import type { IssueGroup } from '../api/grids';
import type { IssueTarget, LinkPosition } from '../api/inspector';
import type { ShownAt } from '../crawl/evidence';
import { isCrawling, type Analysis, type Check, type CrawlListItem, type SkipReason } from '../api/types';
import { crawlName, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { useOpensAtTop } from './scrollPlace';
import { useCommands } from '../keyboard/registry';
import { formatCount } from '../lib/format';
import { useLive } from '../live/liveStore';
import { switchMode } from '../mode/switchMode';
import { checkEntries, relatedChecks, type CheckEntry, type RelatedCheck } from '../screens/issues/checkList';
import { useAnalysisAnswer } from '../screens/issues/useAnalysisAnswer';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { Card } from '../ui/Card';
import { Icon } from '../ui/Icon';
import { SeverityWord } from '../ui/SeverityWord';
import { copyProblemList, saveCheckSheet } from './handOver';
import {
  fixerWords,
  looksForWords,
  passedWords,
  plainTitle,
  pointsWords,
  shareWords,
  wholesOf,
  type ShareWords,
} from './plainWords';
import { flatOneFixSentences, oneFixSentences } from './oneFixWords';
import { plainImageInsight, plainRelated } from './plainEvidence';
import { saysShownAt, useGroupShownAt } from '../screens/issues/useShownAt';
import { WhereItIs } from './WhereItIs';
import { RecheckCard } from './RecheckCard';
import { ChangeFact, FixedSinceNote } from './ProblemSince';
import { useSimpleChanges, type SimpleChanges } from './useSimpleChanges';
import { isRecheckRunning, useRecheckRun } from '../crawl/recheck';
import { useSharedSource } from './useSharedSource';
import { pathOrigin } from './whereWords';
import { useTemplateLink } from '../screens/issues/useWhereFound';
import { firstGroupQuery } from './useOneFix';

const FIX = '/crawls/$crawl/issues/{-$check}';

/**
 * One problem (docs/v2-ui-simple.md, "One problem"): what's wrong and why, how to fix it, who usually fixes it, the
 * One fix callout for a likely template, its pages by title then address, Copy the list, Save as a spreadsheet and
 * See it in Advanced. A skipped check's page says why it couldn't run; a passed check's says its good news.
 */
export function ProblemScreen({ crawlId, checkId }: { crawlId: string; checkId: string }) {
  const item = useQuery(crawlQuery(crawlId)).data;
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  // A recheck's analyses (before and after it) are the recheck's: the page stays, its card following it.
  const rechecking = isRecheckRunning(useRecheckRun(crawlId));
  const analysing = status === 'analyzing' && !rechecking;
  const readable = item !== undefined && status !== 'unreadable';
  const checks = useQuery(checksQuery());
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: readable && !crawling });
  const { neverAnalysed, rereading } = useAnalysisAnswer(crawlId, analysis, { crawling, analysing });
  const check = checks.data?.checks.find((candidate) => candidate.id === checkId);
  const entry = useMemo(
    () =>
      checks.data && analysis.data && check
        ? (checkEntries({ ...checks.data, checks: [check] }, analysis.data)[0] ?? null)
        : null,
    [checks.data, analysis.data, check],
  );
  // The problems related to this one by a fix (related checks), with the points the fix gives back.
  const related = useMemo(() => {
    if (!checks.data || !analysis.data || !entry) return null;
    const points = new Map((analysis.data.score?.lines ?? []).map((line) => [line.checkId, line.points]));
    return relatedChecks(checkEntries(checks.data, analysis.data), entry, points);
  }, [checks.data, analysis.data, entry]);
  // The fix loop: what changed since the site's last check, for the head's count and a fixed problem's good news.
  const changes = useSimpleChanges(crawlId, item, status);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => titleRef.current);
  // At its head, its back link in sight, whatever the list it was opened from was scrolled to (the closing audit's G3).
  useOpensAtTop(screenRef, checkId);

  const loading =
    item === undefined ||
    (readable &&
      !crawling &&
      (checks.isPending || (analysis.isPending && !rereading)) &&
      !checks.error &&
      !analysis.error);
  const skeleton = useDelayedLoading(loading);

  let body: ReactNode;
  if (loading || skeleton) {
    body = skeleton ? <SkeletonRows rows={8} label="Loading the problem" /> : null;
  } else if (checks.error || (analysis.error && !neverAnalysed)) {
    body = (
      <Card>
        <ErrorState
          error={checks.error ?? analysis.error}
          title="This problem couldn’t be read."
          onRetry={() => void (checks.error ? checks.refetch() : analysis.refetch())}
        />
      </Card>
    );
  } else if (!check) {
    body = (
      <Message titleRef={titleRef} title="This isn’t a check Shouting Iguana knows">
        It may be from a newer version.
      </Message>
    );
  } else if (crawling || analysing || neverAnalysed || !analysis.data || !entry) {
    body = (
      <Message titleRef={titleRef} title={plainTitle(check)}>
        {crawling || analysing
          ? 'The check is still running: this shows what it found once it’s done.'
          : 'The results haven’t been worked out for this check, so there’s nothing to show yet.'}{' '}
        <Link to="/crawls/$crawl/overview" params={{ crawl: crawlId }} className="si-link si-link--inline">
          See the summary
        </Link>
      </Message>
    );
  } else if (entry.outcome === 'skipped') {
    body = (
      <SkippedProblem
        titleRef={titleRef}
        check={check}
        reason={analysis.data.skipped.find((skip) => skip.checkId === check.id)?.reason ?? null}
      />
    );
  } else if (entry.outcome === 'passed') {
    body = <PassedProblem titleRef={titleRef} crawlId={crawlId} item={item} check={check} changes={changes} />;
  } else {
    body = (
      <FoundProblem
        titleRef={titleRef}
        crawlId={crawlId}
        item={item}
        check={check}
        entry={entry}
        analysis={analysis.data}
        related={related}
        changes={changes}
      />
    );
  }

  return (
    <div ref={screenRef} className="@container">
      <div className="si-simple-page flex flex-col">
        <p className="mb-4">
          <Link
            to={FIX}
            params={{ crawl: crawlId }}
            className="inline-flex items-center gap-1.5 text-14 font-medium text-text-secondary hover:text-text"
          >
            <Icon icon={ArrowLeft} size="sm" />
            What to fix
          </Link>
        </p>
        {body}
      </div>
    </div>
  );
}

function Message({
  titleRef,
  title,
  children,
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  title: string;
  children: ReactNode;
}) {
  return (
    <>
      <h1 ref={titleRef} tabIndex={-1} className="si-simple-title">
        {title}
      </h1>
      <p className="mt-3 text-16 text-text-secondary">{children}</p>
    </>
  );
}

/** "See it in Advanced": the same route, in Advanced, with this check selected. */
function SeeInAdvanced() {
  const router = useRouter();
  return (
    <Button variant="ghost" iconEnd={ArrowUpRight} className="ml-auto" onClick={() => switchMode(router, 'advanced')}>
      See it in Advanced
    </Button>
  );
}

function ProblemHead({
  titleRef,
  outcome,
  title,
  facts,
  after,
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  outcome: CheckEntry['outcome'];
  title: string;
  facts?: ReactNode;
  /** Under the facts: what a fix of it also fixes. */
  after?: ReactNode;
}) {
  return (
    <header className="mb-5">
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <SeverityWord severity={outcome} variant="large" />
        <SeeInAdvanced />
      </div>
      <h1 ref={titleRef} tabIndex={-1} className="si-simple-title">
        {title}
      </h1>
      {facts ? <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5 text-14 text-text-secondary">{facts}</ul> : null}
      {after}
    </header>
  );
}

/**
 * Related problems, in plain words (the UX audit, X5): what fixing this one also fixes ("Fixing this also fixes Links
 * that lead to broken pages: all 5 pages, 4 more points back"), and which problem's fix fixes this one's pages. Each
 * names the other problem as a link to it.
 */
function AlsoFixes({
  crawlId,
  related,
}: {
  crawlId: string;
  related: { clears: readonly RelatedCheck[]; clearedBy: readonly RelatedCheck[] };
}) {
  if (related.clears.length === 0 && related.clearedBy.length === 0) return null;
  const problem = (other: RelatedCheck) => (
    <Link
      to="/crawls/$crawl/issues/{-$check}"
      params={{ crawl: crawlId, check: other.entry.check.id }}
      className="si-link si-link--inline font-semibold"
    >
      {plainTitle(other.entry.check)}
    </Link>
  );
  const pages = (count: number) => (count === 1 ? '1 page' : `${formatCount(count)} pages`);
  return (
    <ul aria-label="Fixed along with it" className="mt-3 flex flex-col gap-1 text-16 text-text-secondary">
      {related.clears.map((other) => (
        <li key={`clears-${other.entry.check.id}`}>
          Fixing this also fixes {problem(other)}
          {other.all
            ? `: ${other.pages === 1 ? 'its 1 page' : `all ${pages(other.pages)}`}`
            : ` on ${formatCount(other.pages)} of its ${pages(other.entry.count ?? other.pages)}`}
          {other.points ? `, ${formatCount(other.points)} more ${other.points === 1 ? 'point' : 'points'} back` : ''}.
        </li>
      ))}
      {related.clearedBy.map((other) => (
        <li key={`by-${other.entry.check.id}`}>
          Fixing {problem(other)} fixes {other.all ? 'this too' : `${pages(other.pages)} of these too`}.
        </li>
      ))}
    </ul>
  );
}

function Fact({ icon, children }: { icon: typeof Gauge; children: ReactNode }) {
  return (
    <li className="inline-flex items-center gap-1.5">
      <Icon icon={icon} size="sm" className="text-text-muted" />
      <span>{children}</span>
    </li>
  );
}

function ShareFact({ share }: { share: ShareWords }) {
  return (
    <Fact icon={FileText}>
      {share.count === null ? (
        share.rest
      ) : (
        <>
          <b className="font-semibold text-text">{share.count}</b>
          {share.rest}
        </>
      )}
    </Fact>
  );
}

function FoundProblem({
  titleRef,
  crawlId,
  item,
  check,
  entry,
  analysis,
  related,
  changes,
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  entry: CheckEntry;
  analysis: Analysis;
  /** The problems related to this one by a fix: what fixing it also fixes, and what fixing another fixes here. */
  related: { clears: readonly RelatedCheck[]; clearedBy: readonly RelatedCheck[] } | null;
  /** The fix loop: what changed since the site's last check. */
  changes: SimpleChanges;
}) {
  const wholes = wholesOf(analysis);
  const count = entry.count ?? 0;
  const share = shareWords(check, count, wholes);
  const line = analysis.score?.lines.find((candidate) => candidate.checkId === check.id);
  const points = analysis.score ? pointsWords(line, check.severity) : null;
  const fixer = fixerWords(check.fixer);
  const first = useQuery({ ...firstGroupQuery(crawlId, check.id), enabled: check.grouping !== 'none' });
  const template = first.data?.groups[0]?.likelyTemplate ? first.data.groups[0] : null;
  // A check that doesn't group, on nearly every page checked: the engine says it's probably the template's.
  const flatTemplate =
    check.grouping === 'none' &&
    analysis.counts.find((candidate) => candidate.checkId === check.id)?.likelyTemplate === true;
  // Where the one fix is: the link every page shares (a check of pages holding it), or the one page linking to most
  // of the broken addresses (a check of the addresses).
  const templateTarget = useTemplateLink(crawlId, check, template);
  // An oversized image: the size its pages show it at, against the file's.
  const templateShownAt = useGroupShownAt(crawlId, check, template);
  const shared = useSharedSource(crawlId, check);
  const context = {
    crawlId,
    check,
    site: crawlName(item),
    checkedAt: item.finishedAt ?? item.startedAt,
    share: share.text,
  };
  const copyList = () => void copyProblemList(context);
  const saveSheet = () => saveCheckSheet(item, check);
  useCommands(
    () => [
      {
        id: 'problem.copyList',
        title: 'Copy the list',
        group: 'Issues',
        keys: ['mod+shift+c'],
        keywords: ['copy', 'addresses', 'pages'],
        run: copyList,
      },
      {
        id: 'problem.saveSheet',
        title: 'Save as a spreadsheet',
        group: 'Issues',
        keys: ['mod+e'],
        keywords: ['export', 'excel', 'spreadsheet'],
        run: saveSheet,
      },
    ],
    [crawlId, check.id, share.text, item],
  );
  const noun = (check.subject ?? 'page') === 'page' ? 'pages' : 'addresses';
  const what = check.plainWhat ?? [check.description];
  const steps = check.plainFix ?? [check.fix];
  return (
    <>
      <ProblemHead
        titleRef={titleRef}
        outcome={entry.outcome}
        title={plainTitle(check)}
        facts={
          <>
            <ShareFact share={share} />
            <ChangeFact changes={changes} checkId={check.id} />
            {points ? (
              <Fact icon={Gauge}>
                {points.points === null ? (
                  points.text
                ) : (
                  <>
                    Takes <b className="font-semibold text-text">{points.points}</b>{' '}
                    {points.points === 1 ? 'point' : 'points'} off the score
                  </>
                )}
              </Fact>
            ) : null}
            {fixer ? <Fact icon={UserRound}>{fixer}</Fact> : null}
          </>
        }
        after={related ? <AlsoFixes crawlId={crawlId} related={related} /> : null}
      />
      <Card className="si-explain2 @max-split:grid-cols-1">
        <section aria-labelledby="problem-what" className="si-explain2__col">
          <h2 id="problem-what" className="mb-2 text-14 font-semibold">
            What’s wrong, and why it matters
          </h2>
          {what.map((sentence, index) => (
            <p key={index} className={index === 0 ? 'text-16 text-text' : 'mt-2.5 text-16 text-text-secondary'}>
              {sentence}
            </p>
          ))}
        </section>
        <section aria-labelledby="problem-fix" className="si-explain2__col">
          <h2 id="problem-fix" className="mb-2 text-14 font-semibold">
            How to fix it
          </h2>
          <ol className="si-steps text-16">
            {steps.map((step, index) => (
              <li key={index}>{step}</li>
            ))}
          </ol>
        </section>
      </Card>
      {template ? (
        <Callout
          className="mt-4"
          title={`One fix clears ${(template.urls ?? template.pages).toLocaleString('en-GB')} of these ${count.toLocaleString('en-GB')} ${noun}`}
        >
          <OneFixWords
            check={check}
            item={item}
            group={template}
            count={count}
            noun={noun}
            link={templateTarget}
            shownAt={templateShownAt}
          />
        </Callout>
      ) : flatTemplate ? (
        <Callout className="mt-4" title={`One fix clears all ${count.toLocaleString('en-GB')} of these ${noun}`}>
          {flatOneFixSentences(count, analysis.pagesAnalyzed).lead}{' '}
          {check.plainOneFix ?? 'That’s almost certainly in the part of the site every page shares.'}{' '}
          {flatOneFixSentences(count, analysis.pagesAnalyzed).fix}
        </Callout>
      ) : shared ? (
        <Callout
          className="mt-4"
          title={`One page links to ${shared.urls.toLocaleString('en-GB')} of ${shared.of >= count ? 'these' : 'the first'} ${shared.of.toLocaleString('en-GB')} ${noun}`}
        >
          {sharedSourceWords(pathOf(shared.url, item), shared.position)}
        </Callout>
      ) : null}
      <WhereItIs
        crawlId={crawlId}
        item={item}
        check={check}
        total={count}
        onCopy={copyList}
        onSave={saveSheet}
        changes={changes}
      />
      {/* The fix loop: Fixed it? is a recheck of these pages (./RecheckCard). */}
      <RecheckCard crawlId={crawlId} item={item} check={check} />
    </>
  );
}

/**
 * The One fix callout's words: what the pages share and on how many ("The same image, /img/hero.jpg, is on 12 of
 * these 14 pages."), where such a thing usually is (the engine's sentence), the one change that fixes them all, then
 * what the cause is ("The image file is 1.8 MB, 4000×3000 pixels, but the pages show it at 400×300, …").
 */
function OneFixWords({
  check,
  item,
  group,
  count,
  noun,
  link,
  shownAt,
}: {
  check: Check;
  item: CrawlListItem;
  group: IssueGroup;
  count: number;
  noun: 'pages' | 'addresses';
  link: Pick<IssueTarget, 'kind' | 'position'> | null;
  shownAt: ShownAt | null | undefined;
}) {
  const origin = group.relatedUrl ? pathOrigin(group.relatedUrl, item) : null;
  // The home page by name: "to your home page" reads, "to /" doesn't.
  const address = group.relatedUrl ? pathOf(group.relatedUrl, item) : null;
  const path = address === '/' ? 'your home page' : address;
  const { lead, fix } = oneFixSentences({
    check,
    on: group.urls ?? group.pages,
    of: count,
    noun,
    path,
    sharedText: group.sharedText ?? null,
    link,
    finalPath: group.related?.final ? pathOf(group.related.final.url, item) : null,
  });
  const about =
    saysShownAt(check) && group.related
      ? plainImageInsight(group.related, shownAt)
      : plainRelated(group.related, check, origin);
  // The path in the address's face, the rest as words.
  const withPath = (sentence: string) => {
    if (!path || path !== address || !sentence.includes(path)) return sentence;
    const at = sentence.indexOf(path);
    return (
      <>
        {sentence.slice(0, at)}
        <span className="font-mono text-14">{path}</span>
        {sentence.slice(at + path.length)}
      </>
    );
  };
  return (
    <>
      {withPath(lead)} {check.plainOneFix ? `${check.plainOneFix} ` : null}
      {withPath(fix)}
      {about ? <span className="mt-1 block">{about}</span> : null}
    </>
  );
}

/** "/garden-furniture/ links to most of them, from its footer. Fix or remove the links there first." */
function sharedSourceWords(path: string, position: LinkPosition | null): ReactNode {
  const where = position && position !== 'content' ? `, from its ${position === 'navigation' ? 'menu' : position}` : '';
  return (
    <>
      <span className="font-mono text-14">{path}</span> links to most of them{where}. Fix or remove the links there
      first.
    </>
  );
}

/** An address as the problem page names it: its path on the site's own origin, else in full. */
function pathOf(url: string, item: Pick<CrawlListItem, 'site' | 'startUrl'>): string {
  const origin = pathOrigin(url, item);
  return origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
}

/** A check that couldn't run: what it looks for, why it couldn't, and what would let it. */
function SkippedProblem({
  titleRef,
  check,
  reason,
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  check: Check;
  reason: SkipReason | null;
}) {
  return (
    <>
      {/* The title says what it looks for: its plain words of what's wrong would state a fault it couldn't look for. */}
      <ProblemHead titleRef={titleRef} outcome="skipped" title={looksForWords(check)} />
      <Card className="flex flex-col gap-4 px-7 py-6">
        <section>
          <h2 className="text-14 font-semibold">Why it couldn’t run</h2>
          <p className="mt-1 text-16 text-text-secondary">
            {reason?.plainText ??
              reason?.text ??
              'The version of Shouting Iguana that worked out the results didn’t say.'}
          </p>
        </section>
        {reason?.plainAction ? (
          <section>
            <h2 className="text-14 font-semibold">What would let it run</h2>
            <p className="mt-1 text-16 text-text-secondary">{reason.plainAction}</p>
          </section>
        ) : null}
      </Card>
    </>
  );
}

/** A check that found nothing: its good news, and what it looks for. */
function PassedProblem({
  titleRef,
  crawlId,
  item,
  check,
  changes,
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  /** The fix loop: a problem fixed since the site's last check says so, with what was fixed. */
  changes: SimpleChanges;
}) {
  return (
    <>
      <ProblemHead titleRef={titleRef} outcome="passed" title={passedWords(check)} />
      <FixedSinceNote crawlId={crawlId} item={item} check={check} changes={changes} />
      <Card className="flex flex-col gap-4 px-7 py-6">
        {/* What it looks for, never its words of what's wrong: it found nothing wrong. */}
        <section aria-labelledby="problem-looks-for">
          <h2 id="problem-looks-for" className="text-14 font-semibold">
            What it looks for
          </h2>
          <p className="mt-1 text-16 text-text">{looksForWords(check)}</p>
        </section>
      </Card>
      {/* A recheck that cleared it: what it found stays in view ("All 5 fixed. This problem is gone from the list."). */}
      <RecheckCard crawlId={crawlId} item={item} check={check} resultOnly />
    </>
  );
}
