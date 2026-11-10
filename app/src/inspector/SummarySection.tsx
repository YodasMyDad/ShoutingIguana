import { CircleCheck, CircleDashed, CircleSlash, Check, Minus, X } from 'lucide-react';
import type { ReactNode } from 'react';
import type { MeasuredText, UrlDetail } from '../api/inspector';
import { formatBytes, formatCount, formatCountOf, formatExactTime, formatMilliseconds } from '../lib/format';
import { Badge } from '../ui/Badge';
import { Callout } from '../ui/Callout';
import { Fact, Facts } from '../ui/Facts';
import { Icon } from '../ui/Icon';
import { Meter } from '../ui/Meter';
import { FigureParts, Num } from '../ui/Num';
import { SeverityMark } from '../ui/SeverityMark';
import { MiniStat, MiniStats } from '../ui/Stat';
import { StatusCode } from '../ui/StatusCode';
import { severityCount, SEVERITIES } from '../ui/severity';
import { fetchState, type FetchState } from './fetchState';
import { canonicalFact, indexabilityVerdict, type IndexabilityRow, type IndexabilityVerdict } from './indexability';
import { checkTitle, useChecks, useInspectorContext } from './inspectorContext';
import { issueLink } from './issueLinks';
import { Block, IssueCheckLink, IssueList, PageText, SectionBody, WalkUrl } from './parts';
import { measuresOf, scaleEnd, type Measure } from './scale';
import type { SectionId } from './sections';
import { KIND_WORDS, SOURCE_WORDS, TYPE_WORDS, depthWords, protocolWords, reasonPhrase, wordFor } from './words';

/** How many of the URL's issues the summary lists before sending the rest to the Issues section. */
const SUMMARY_ISSUES = 5;

const SUMMARY_WORDS = {
  crawling: 'Known once the crawl has finished and been analysed.',
  notAnalyzed: 'Known once the crawl is analysed.',
  engineChanged: 'Known once the crawl is analysed again: another version of Shouting Iguana analysed it.',
  crawlChanged: 'Known once the crawl is analysed again: it was continued after its last analysis.',
} as const;

export interface SummarySectionProps {
  detail: UrlDetail;
  /** Shows another section: "All 12 in Issues". */
  onShowSection: (section: SectionId) => void;
}

/**
 * The summary: how it answered, whether it can be indexed and why, its title, description and H1 with each length on
 * the scale the analysis used, its content, timings and size, its issues, and how the crawl found it.
 */
export function SummarySection({ detail, onShowSection }: SummarySectionProps) {
  const { crawling } = useInspectorContext();
  const state = fetchState(detail, crawling);
  const measures = measuresOf(detail);
  const verdict = indexabilityVerdict(detail);
  return (
    <SectionBody>
      <TopFigures detail={detail} state={state} response={measures.response} imageSize={measures.imageSize} />
      {state.title ? (
        <Block>
          <Callout tone="info" title={state.title}>
            {state.why}
            {state.pending ? ' It’ll show here once the crawl has fetched it.' : null}
            {detail.response?.error ? (
              <span className="mt-1 block font-mono text-12">{detail.response.error}</span>
            ) : null}
          </Callout>
        </Block>
      ) : null}
      <Verdict verdict={verdict} />
      {detail.content ? (
        <>
          <Block>
            <div className="flex flex-col gap-5">
              {detail.limits.fromAnalysis ? null : (
                <p className="text-12 text-text-muted">
                  Drawn against the default limits: this crawl hasn’t been analysed, so no limits of its own apply yet.
                </p>
              )}
              <MeasuredTexts
                name="Title"
                none="The page has no title."
                texts={detail.content.titles}
                measures={measures.titles}
                detail={detail}
              />
              <MeasuredTexts
                name="Meta description"
                none="The page has no meta description."
                texts={detail.content.descriptions}
                measures={measures.descriptions}
                detail={detail}
              />
              <PlainTexts name="H1" none="The page has no H1." texts={detail.content.h1s} />
            </div>
          </Block>
          <ContentFacts detail={detail} wordsMeasure={measures.words} />
        </>
      ) : null}
      {state.answered && detail.response ? (
        <Block title="Timings and size">
          <div className="flex flex-col gap-4">
            {measures.response ? <ScaleMeter measure={measures.response} detail={detail} /> : null}
            {measures.htmlSize ? <ScaleMeter measure={measures.htmlSize} detail={detail} /> : null}
            {measures.imageSize ? <ScaleMeter measure={measures.imageSize} detail={detail} /> : null}
            <Facts>
              <Fact label="First byte">
                <Num value={detail.response.ttfbMs} format="ms" />
              </Fact>
              <Fact label="Transferred">
                <Num value={detail.response.sizeBytes} format="bytes" />
              </Fact>
              <Fact label="Content type">
                {detail.response.contentType ? (
                  <span className="font-mono text-12">{detail.response.contentType}</span>
                ) : (
                  '–'
                )}
              </Fact>
              <Fact label="Protocol">{protocolWords(detail.response.httpVersion) ?? '–'}</Fact>
              <Fact label="Crawled">
                <span className="figures">{formatExactTime(detail.response.crawledAt)}</span>
              </Fact>
            </Facts>
          </div>
        </Block>
      ) : null}
      <IssuesSummary detail={detail} onShowSection={onShowSection} />
      <Block title="Links and discovery">
        <Discovery detail={detail} />
      </Block>
    </SectionBody>
  );
}

/** Status, response and words (or size, for a file): the three figures the design leads with. */
function TopFigures({
  detail,
  state,
  response: responseTime,
  imageSize,
}: {
  detail: UrlDetail;
  state: FetchState;
  /** The response time on its scale: its words are exact where rounding would put it on its limit. */
  response: Measure | null;
  /** An image's size on its scale, with the limit for images. */
  imageSize: Measure | null;
}) {
  const { response, content, limits } = detail;
  if (!response || !state.answered) return null;
  return (
    <Block>
      <MiniStats>
        <MiniStat
          label="Status"
          value={<StatusCode code={response.statusCode} />}
          context={reasonPhrase(response.statusCode) ?? TYPE_WORDS[detail.type]}
        />
        <MiniStat
          label="Response"
          value={
            responseTime ? (
              <span className="figures">
                <FigureParts text={responseTime.valueText} />
              </span>
            ) : (
              <Num value={response.responseMs} format="ms" />
            )
          }
          context={`Limit ${formatMilliseconds(limits.slowResponseMs)}`}
        />
        {content ? (
          <MiniStat
            label="Words"
            value={<Num value={content.wordCount} />}
            context={`Minimum ${formatCount(limits.minWords)}`}
          />
        ) : (
          <MiniStat
            label="Size"
            value={
              imageSize ? (
                <span className="figures">
                  <FigureParts text={imageSize.valueText} />
                </span>
              ) : (
                <Num value={response.sizeBytes} format="bytes" />
              )
            }
            context={imageSize ? imageSize.limitText : TYPE_WORDS[detail.type]}
          />
        )}
      </MiniStats>
    </Block>
  );
}

const VERDICT_ICONS = { indexable: CircleCheck, notIndexable: CircleSlash, notJudged: CircleDashed } as const;
const TEST_ICONS = { pass: Check, fail: X, notChecked: Minus } as const;
const TEST_WORDS = { pass: 'Passed', fail: 'Failed', notChecked: 'Not checked' } as const;

/** "Indexable, and why": the verdict, then the four tests as a checklist, the failed ones marked. */
function Verdict({ verdict }: { verdict: IndexabilityVerdict }) {
  return (
    <Block>
      <div className="flex items-start gap-3">
        <span className="grid h-(--icon-box) w-(--icon-box) shrink-0 place-items-center rounded-full bg-surface-hover text-text">
          <Icon icon={VERDICT_ICONS[verdict.state]} size="md" />
        </span>
        <div className="min-w-0">
          <h3 className="text-14 font-semibold text-text">{verdict.title}</h3>
          <p className="mt-0.5 text-13 text-text-secondary">{verdict.why}</p>
          {verdict.note ? <p className="mt-1 text-12 text-text-muted">{verdict.note}</p> : null}
        </div>
      </div>
      <ul aria-label="Indexability tests" className="mt-4 flex flex-col gap-2.5">
        {verdict.rows.map((row) => (
          <TestRow key={row.test} row={row} />
        ))}
      </ul>
    </Block>
  );
}

function TestRow({ row }: { row: IndexabilityRow }) {
  const failed = row.state === 'fail';
  return (
    <li className="grid grid-cols-(--check-row-columns) gap-x-2.5 text-13">
      <span
        className={
          failed
            ? 'grid h-(--leading-13) w-(--leading-13) place-items-center rounded-xs bg-text text-surface-raised'
            : 'grid h-(--leading-13) w-(--leading-13) place-items-center text-text-muted'
        }
      >
        <Icon icon={TEST_ICONS[row.state]} size="xs" />
      </span>
      <div className="min-w-0">
        <p className={failed ? 'font-semibold text-text' : row.state === 'pass' ? 'text-text' : 'text-text-muted'}>
          {row.label}
          <span className="sr-only">: {TEST_WORDS[row.state]}.</span>
          {failed ? (
            <Badge variant="outline" className="ml-2 align-middle">
              Failed
            </Badge>
          ) : null}
        </p>
        {row.plain ? null : <p className="text-12 text-text-muted">{row.detail}</p>}
        {row.target ? (
          <WalkUrl
            id={row.target.id}
            url={row.target.url}
            statusCode={row.target.id === null ? undefined : row.target.statusCode}
            showStatus
            className="mt-0.5"
          />
        ) : null}
      </div>
    </li>
  );
}

/**
 * The severity and title of the check a value out of range trips, when the analysis flagged this URL with it. An
 * image's size is judged on the pages that use the image: the check is theirs, and its issues name this URL.
 */
function useFlagged(detail: UrlDetail, measure: Measure) {
  const { byId, ready } = useChecks();
  const checkId = measure.checkId;
  if (checkId === null) return null;
  const check = byId.get(checkId);
  const issue = detail.issues?.find((candidate) => candidate.checkId === checkId);
  if (issue) {
    return {
      severity: issue.severity ?? check?.severity ?? 'notice',
      title: checkTitle(check, ready),
      link: issueLink(issue, check?.grouping, detail),
    };
  }
  const named = measure.id === 'imageSize' && detail.namedBy?.checks.some((entry) => entry.checkId === checkId);
  if (!named) return null;
  return {
    severity: check?.severity ?? 'notice',
    title: checkTitle(check, ready),
    // The pages whose issue names this image: its group on a check grouped by the other URL.
    link: {
      checkId,
      group: check?.grouping === 'relatedUrl' ? { detail: null, relatedUrlId: detail.id } : undefined,
    },
  };
}

/** A value on its scale, and, when the analysis flagged it, the check with how far out it is. */
function ScaleMeter({ measure, detail }: { measure: Measure; detail: UrlDetail }) {
  const flagged = useFlagged(detail, measure);
  return (
    <div>
      <Meter
        label={measure.label}
        value={measure.value}
        min={measure.min}
        max={measure.max}
        scaleMax={scaleEnd(measure)}
        valueText={measure.valueText}
        limitText={measure.limitText}
        severity={flagged?.severity ?? 'notice'}
        formatTick={measure.formatTick}
      />
      {measure.note ? (
        <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-12 text-text-muted">
          {flagged ? (
            <span className="inline-flex items-center gap-1.5 text-text">
              <SeverityMark severity={flagged.severity} />
              <IssueCheckLink checkId={flagged.link.checkId} group={flagged.link.group}>
                {flagged.title}
              </IssueCheckLink>
            </span>
          ) : null}
          <span className="figures">{measure.note}</span>
        </p>
      ) : null}
    </div>
  );
}

function TextHeading({ name, count, index }: { name: string; count: number; index: number }) {
  return (
    <h4 className="text-12 text-text-muted">
      {name}
      {count > 1 ? ` ${index + 1} of ${count}` : null}
    </h4>
  );
}

/** Titles or descriptions as the page gives them, each with its length on the analysis's range. */
function MeasuredTexts({
  name,
  none,
  texts,
  measures,
  detail,
}: {
  name: string;
  none: string;
  texts: readonly MeasuredText[];
  measures: readonly Measure[];
  detail: UrlDetail;
}) {
  if (texts.length === 0) {
    return (
      <div>
        <h4 className="text-12 text-text-muted">{name}</h4>
        <p className="mt-1 text-13 text-text-secondary">{none}</p>
      </div>
    );
  }
  return (
    <>
      {texts.map((text, index) => {
        const measure = measures[index];
        return (
          <div key={index} className="flex flex-col gap-1.5">
            <TextHeading name={name} count={texts.length} index={index} />
            <PageText text={text.text} what={name.toLowerCase()} />
            {measure ? <ScaleMeter measure={measure} detail={detail} /> : null}
          </div>
        );
      })}
    </>
  );
}

function PlainTexts({ name, none, texts }: { name: string; none: string; texts: readonly MeasuredText[] }) {
  return (
    <div className="flex flex-col gap-1.5">
      <h4 className="flex justify-between gap-2 text-12 text-text-muted">
        <span>{name}</span>
        {texts.length > 0 ? <span className="figures">{formatCount(texts.length)} on the page</span> : null}
      </h4>
      {texts.length === 0 ? (
        <p className="text-13 text-text-secondary">{none}</p>
      ) : (
        texts.map((text, index) => <PageText key={index} text={text.text} what={name} />)
      )}
    </div>
  );
}

const HEADING_NAMES = ['H1', 'H2', 'H3', 'H4', 'H5', 'H6'] as const;

/** "1 H1, 4 H2, 9 H3": the headings the page has, by level. */
function headingWords(headings: readonly number[]): string {
  const parts = HEADING_NAMES.flatMap((name, index) => {
    const count = headings[index] ?? 0;
    return count > 0 ? [`${formatCount(count)} ${name}`] : [];
  });
  return parts.length > 0 ? parts.join(', ') : 'None';
}

function imageWords(images: number, missingAlt: number): string {
  if (images === 0) return 'None';
  if (missingAlt === 0) return `${formatCount(images)}, all with alt text`;
  return `${formatCount(images)}, ${formatCount(missingAlt)} without alt text`;
}

/** A canonical there's nothing to show for: the page has none, or the crawl didn't get as far as judging it. */
const CANONICAL_WORDS = { none: 'None', notChecked: 'Not checked', notRead: 'Not read' } as const;

function ContentFacts({ detail, wordsMeasure }: { detail: UrlDetail; wordsMeasure: Measure | null }) {
  const content = detail.content;
  if (!content) return null;
  const canonical = canonicalFact(detail);
  const types = [...new Set(detail.structuredData.flatMap((block) => block.types))];
  const canonicalValue: ReactNode =
    canonical.kind === 'other' ? (
      <WalkUrl {...canonical.target} showStatus={canonical.target.id !== null} />
    ) : canonical.kind === 'self' ? (
      <Badge>Self-referencing</Badge>
    ) : (
      <span className="text-text-secondary">{CANONICAL_WORDS[canonical.kind]}</span>
    );
  return (
    <Block title="Content">
      <div className="flex flex-col gap-4">
        {wordsMeasure ? <ScaleMeter measure={wordsMeasure} detail={detail} /> : null}
        <Facts>
          <Fact label="Canonical">{canonicalValue}</Fact>
          <Fact label="Headings">{headingWords(content.headings)}</Fact>
          <Fact label="Images">{imageWords(content.images, content.imagesMissingAlt)}</Fact>
          <Fact label="Structured data">{types.length > 0 ? types.join(', ') : 'None'}</Fact>
          <Fact label="Language">
            {content.language ? <span className="font-mono text-12">{content.language}</span> : 'Not declared'}
          </Fact>
          {content.charset ? (
            <Fact label="Character set">
              <span className="font-mono text-12">{content.charset}</span>
            </Fact>
          ) : null}
          {content.robots.length > 0 ? (
            <Fact label="Robots">
              <span className="font-mono text-12">{content.robots.join(', ')}</span>
            </Fact>
          ) : null}
          {content.metaRefresh ? (
            <Fact label="Meta refresh">
              {content.metaRefresh.delaySeconds === null
                ? 'With no delay given'
                : content.metaRefresh.delaySeconds === 0
                  ? 'At once'
                  : `After ${formatCountOf(content.metaRefresh.delaySeconds, 'second')}`}
              {content.metaRefresh.url ? (
                <span className="mt-0.5 block font-mono text-12 wrap-anywhere">{content.metaRefresh.url}</span>
              ) : null}
            </Fact>
          ) : null}
        </Facts>
      </div>
    </Block>
  );
}

/** The URL's issues, in short: how many by severity, the first few, and the way to them all. */
function IssuesSummary({ detail, onShowSection }: SummarySectionProps) {
  const issues = detail.issues;
  if (issues === null) {
    return (
      <Block title="Issues">
        <p className="text-13 text-text-secondary">
          This crawl hasn’t been analysed yet, so no check has looked at this URL.
        </p>
      </Block>
    );
  }
  if (issues.length === 0) {
    return (
      <Block title="No issues">
        <p className="text-13 text-text-secondary">No check flagged this URL in the last analysis.</p>
      </Block>
    );
  }
  const bySeverity = SEVERITIES.flatMap((severity) => {
    const count = issues.filter((issue) => issue.severity === severity).length;
    return count > 0 ? [severityCount(severity, count)] : [];
  });
  return (
    <Block
      title={formatCountOf(issues.length, 'issue')}
      meta={bySeverity.join(', ')}
      actions={
        <button type="button" className="si-link" onClick={() => onShowSection('issues')}>
          {issues.length > SUMMARY_ISSUES ? `All ${formatCount(issues.length)} in Issues` : 'Open Issues'}
        </button>
      }
    >
      <IssueList issues={issues.slice(0, SUMMARY_ISSUES)} label="This URL’s issues" detail={detail} />
    </Block>
  );
}

function Discovery({ detail }: { detail: UrlDetail }) {
  const { crawling } = useInspectorContext();
  const { discovery, links, summary } = detail;
  const unknown = (
    <span className="text-text-secondary">
      {SUMMARY_WORDS[summary.reason ?? (crawling ? 'crawling' : 'notAnalyzed')]}
    </span>
  );
  const foundVia = discovery.foundVia ? KIND_WORDS[discovery.foundVia].toLowerCase() : null;
  return (
    <Facts>
      <Fact label="Inlinks">
        {links ? (
          <>
            <Num value={links.inlinks} noun="hyperlink" />{' '}
            <span className="text-text-muted">
              from {formatCountOf(links.linkingPages, 'page')}
              {links.nofollowInlinks > 0 ? `, ${formatCount(links.nofollowInlinks)} nofollow` : ''}
            </span>
          </>
        ) : (
          unknown
        )}
      </Fact>
      {links ? (
        <Fact label="Outlinks">
          {links.outlinks === null ? (
            <span className="text-text-secondary">Not read</span>
          ) : (
            <Num value={links.outlinks} noun="hyperlink" />
          )}
        </Fact>
      ) : null}
      {links && links.inSitemap !== null ? <Fact label="In a sitemap">{links.inSitemap ? 'Yes' : 'No'}</Fact> : null}
      <Fact label="Depth">{depthWords(discovery)}</Fact>
      <Fact label="Found by">
        {wordFor(SOURCE_WORDS, discovery.source)}
        {discovery.source === 'link' && foundVia ? <span className="text-text-muted"> ({foundVia})</span> : null}
      </Fact>
      {discovery.foundOn ? (
        <Fact label="Found on">
          <WalkUrl id={discovery.foundOn.id} url={discovery.foundOn.url} />
        </Fact>
      ) : null}
      <Fact label="Site">{detail.isInternal ? 'This site' : 'Another site'}</Fact>
      {detail.response?.bodyBytes !== null && detail.response?.bodyBytes !== undefined && !detail.content ? (
        <Fact label="Size unpacked">{formatBytes(detail.response.bodyBytes)}</Fact>
      ) : null}
    </Facts>
  );
}
