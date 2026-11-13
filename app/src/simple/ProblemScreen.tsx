import { useQuery } from '@tanstack/react-query';
import { Link, useRouter } from '@tanstack/react-router';
import { ArrowLeft, ArrowUpRight, FileText, Gauge, RefreshCw, UserRound } from 'lucide-react';
import { useMemo, useRef, type ReactNode } from 'react';
import { analysisQuery, checksQuery, crawlQuery } from '../api/queries';
import { isCrawling, type Analysis, type Check, type CrawlListItem, type SkipReason } from '../api/types';
import { crawlName, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { useCommands } from '../keyboard/registry';
import { useLive } from '../live/liveStore';
import { switchMode } from '../mode/switchMode';
import { checkEntries, type CheckEntry } from '../screens/issues/checkList';
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
import { WhereItIs } from './WhereItIs';
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
  const analysing = status === 'analyzing';
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
  const titleRef = useRef<HTMLHeadingElement>(null);
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => titleRef.current);

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
    body = <PassedProblem titleRef={titleRef} check={check} />;
  } else {
    body = (
      <FoundProblem
        titleRef={titleRef}
        crawlId={crawlId}
        item={item}
        check={check}
        entry={entry}
        analysis={analysis.data}
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
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  outcome: CheckEntry['outcome'];
  title: string;
  facts?: ReactNode;
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
    </header>
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
}: {
  titleRef: React.RefObject<HTMLHeadingElement | null>;
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  entry: CheckEntry;
  analysis: Analysis;
}) {
  const wholes = wholesOf(analysis);
  const count = entry.count ?? 0;
  const share = shareWords(check, count, wholes);
  const line = analysis.score?.lines.find((candidate) => candidate.checkId === check.id);
  const points = analysis.score ? pointsWords(line, check.severity) : null;
  const fixer = fixerWords(check.fixer);
  const first = useQuery({ ...firstGroupQuery(crawlId, check.id), enabled: check.grouping !== 'none' });
  const template = first.data?.groups[0]?.likelyTemplate ? first.data.groups[0] : null;
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
          {check.plainOneFix ??
            'They share one cause, in the part of the site every page shares, so one change there clears them.'}
        </Callout>
      ) : null}
      <WhereItIs crawlId={crawlId} item={item} check={check} total={count} onCopy={copyList} onSave={saveSheet} />
      <Card className="mt-4 flex flex-wrap items-center gap-5 px-6 py-5">
        <div className="basis-80 min-w-0 flex-1">
          <h2 className="font-semibold">Fixed it?</h2>
          <p className="mt-0.5 text-14 leading-body text-text-secondary">
            Shouting Iguana can’t see a fix until it reads the site again. Check again, and this drops off the list if
            it’s gone.
          </p>
        </div>
        {item.startUrl ? (
          <Button icon={RefreshCw} asChild>
            <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
              Check the site again
            </Link>
          </Button>
        ) : null}
      </Card>
    </>
  );
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
      <ProblemHead titleRef={titleRef} outcome="skipped" title={looksForWords(check)} />
      <Card className="flex flex-col gap-4 px-7 py-6">
        <section>
          <h2 className="text-14 font-semibold">What it looks for</h2>
          <p className="mt-1 text-16 text-text-secondary">{check.plainWhat?.[0] ?? check.description}</p>
        </section>
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
function PassedProblem({ titleRef, check }: { titleRef: React.RefObject<HTMLHeadingElement | null>; check: Check }) {
  return (
    <>
      <ProblemHead titleRef={titleRef} outcome="passed" title={passedWords(check)} />
      <Card className="flex flex-col gap-4 px-7 py-6">
        <section>
          <h2 className="text-14 font-semibold">What it looks for</h2>
          <p className="mt-1 text-16 text-text">{plainTitle(check)}</p>
          <p className="mt-1 text-16 text-text-secondary">{check.plainWhat?.[0] ?? check.description}</p>
        </section>
      </Card>
    </>
  );
}
