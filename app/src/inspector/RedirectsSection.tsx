import { ArrowDown, CornerDownRight, Repeat } from 'lucide-react';
import type { UrlDetail } from '../api/inspector';
import { Badge } from '../ui/Badge';
import { Callout } from '../ui/Callout';
import { Icon } from '../ui/Icon';
import { StatusCode } from '../ui/StatusCode';
import { useInspectorContext, useIssuesIn } from './inspectorContext';
import { Block, IssueList, SectionBody, WalkUrl } from './parts';
import { redirectChainLayout, type ChainStep } from './redirectChain';
import { reasonPhrase } from './words';

const VIA_WORDS = { http: 'Redirects to', metaRefresh: 'Meta refresh to' } as const;
const REDIRECT_CATEGORIES = ['redirects'] as const;

/**
 * The redirect chain, hop by hop: the URL itself with its own status, then where each step went, the last being
 * where it ends. Loops and chains longer than the crawl follows are called out.
 */
export function RedirectsSection({ detail }: { detail: UrlDetail }) {
  const { crawling } = useInspectorContext();
  const layout = redirectChainLayout(detail, crawling);
  const issues = useIssuesIn(detail.issues, REDIRECT_CATEGORIES);
  return (
    <SectionBody>
      <Block title={layout.title}>
        <p className="mb-4 text-13 text-text-secondary">{layout.summary}</p>
        {layout.loop ? (
          <Callout tone="warning" title="A redirect loop" icon={Repeat} className="mb-4">
            Browsers and search engines give up on a loop: point the first redirect at a page that answers.
          </Callout>
        ) : null}
        {layout.tooLong ? (
          <Callout tone="warning" title="Too long a chain" className="mb-4">
            The crawl follows 20 hops and stops. Search engines stop sooner: redirect straight to where it ends.
          </Callout>
        ) : null}
        {layout.hops > 0 ? (
          <ol aria-label="Redirect chain" className="flex flex-col">
            {layout.steps.map((step, index) => (
              <Step key={step.key} step={step} index={index} last={index === layout.steps.length - 1} />
            ))}
          </ol>
        ) : null}
      </Block>
      {issues.length > 0 ? (
        <Block title="What the analysis found">
          <IssueList issues={issues} label="This URL’s redirect issues" detail={detail} />
        </Block>
      ) : null}
    </SectionBody>
  );
}

function Step({ step, index, last }: { step: ChainStep; index: number; last: boolean }) {
  return (
    <li className="grid grid-cols-(--chain-columns) gap-x-3">
      {/* The rail: the step's number, and the line down to the next. */}
      <div className="flex flex-col items-center">
        <span className="figures grid h-(--icon-box) w-(--icon-box) shrink-0 place-items-center rounded-full border border-border bg-surface-sunken text-12 font-medium text-text-secondary">
          {index + 1}
        </span>
        {last ? null : <span aria-hidden="true" className="w-(--hairline) flex-1 bg-border" />}
      </div>
      <div className={last ? 'min-w-0 pb-1' : 'min-w-0 pb-5'}>
        {step.via ? (
          <p className="flex items-center gap-1.5 pt-1 text-12 text-text-muted">
            <Icon icon={step.via === 'http' ? ArrowDown : CornerDownRight} size="xs" />
            {VIA_WORDS[step.via]}
          </p>
        ) : null}
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 pt-1">
          <StatusCode
            code={step.statusCode}
            reason={step.statusCode === null ? (step.noCode ?? undefined) : reasonPhrase(step.statusCode)}
            variant={step.statusCode !== null && step.statusCode >= 400 ? 'flag' : 'plain'}
            className="text-13"
          />
          {step.current && !step.repeat ? <Badge variant="outline">This URL</Badge> : null}
          {step.repeat ? (
            <Badge variant="outline" icon={Repeat}>
              {step.current ? 'Back at this URL' : 'Seen before: the loop closes here'}
            </Badge>
          ) : null}
          {step.role === 'end' && !step.repeat && index > 0 ? <Badge variant="outline">Where it ends</Badge> : null}
        </div>
        <WalkUrl id={step.id} url={step.url} truncate="wrap" className="mt-1" />
      </div>
    </li>
  );
}
