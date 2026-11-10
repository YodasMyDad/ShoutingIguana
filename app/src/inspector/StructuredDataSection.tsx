import { Braces, ChevronDown, ChevronRight } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import type { UrlDetail } from '../api/inspector';
import { formatCount } from '../lib/format';
import { Badge } from '../ui/Badge';
import { Callout } from '../ui/Callout';
import { EmptyState } from '../ui/EmptyState';
import { Icon } from '../ui/Icon';
import { fetchState } from './fetchState';
import { useIssuesIn } from './inspectorContext';
import { Block, IssueList, SectionBody } from './parts';
import {
  CHILD_PAGE,
  parseStructuredData,
  structuredDataSummary,
  visibleChildren,
  type DataNode,
  type ParsedBlock,
} from './structuredData';

const STRUCTURED_DATA_CATEGORIES = ['structuredData'] as const;
/** Levels open at first: the block and what's directly in it. */
const OPEN_DEPTH = 1;

/**
 * The page's structured data: each JSON-LD block with its types and whether it parses, and its parsed tree to open
 * and close (nested lists with a disclosure button each). A block that doesn't parse shows its text and the parser's
 * message. Values are the site's: text only.
 */
export function StructuredDataSection({ detail }: { detail: UrlDetail }) {
  const blocks = useMemo(() => parseStructuredData(detail.structuredData), [detail.structuredData]);
  const issues = useIssuesIn(detail.issues, STRUCTURED_DATA_CATEGORIES);

  if (blocks.length === 0) {
    const state = fetchState(detail);
    return (
      <SectionBody>
        <EmptyState title="No structured data" icon={Braces} layout="row" level={3}>
          {state.hasContent
            ? 'The page has no JSON-LD blocks.'
            : (state.why ?? 'The crawl didn’t read this URL’s content, so it has none to show.')}
        </EmptyState>
      </SectionBody>
    );
  }

  return (
    <SectionBody>
      <p className="pb-4 text-13 text-text-secondary">{structuredDataSummary(blocks)}.</p>
      {issues.length > 0 ? (
        <Block title="What the analysis found" className="border-t">
          <IssueList issues={issues} label="This URL’s structured data issues" />
        </Block>
      ) : null}
      {blocks.map((block) => (
        <BlockView key={block.index} block={block} count={blocks.length} />
      ))}
    </SectionBody>
  );
}

function BlockView({ block, count }: { block: ParsedBlock; count: number }) {
  return (
    <Block
      title={count > 1 ? `Block ${block.index}: ${block.title}` : block.title}
      meta={block.valid ? 'JSON-LD' : null}
      actions={block.valid ? null : <Badge variant="dashed">Doesn’t parse</Badge>}
      className="border-t"
    >
      {block.truncated ? (
        <Callout tone="info" className="mb-3">
          This block is longer than the crawl reads: it was cut, so some of it isn’t here.
        </Callout>
      ) : null}
      {block.error ? (
        <Callout tone="warning" title="It isn’t valid JSON" className="mb-3">
          <span className="font-mono text-12 wrap-anywhere">{block.error}</span>
        </Callout>
      ) : null}
      {block.root ? (
        <ul aria-label={`${block.title} properties`} className="font-mono text-12">
          <Node node={block.root} depth={0} root />
        </ul>
      ) : (
        <pre
          // Scrolls by itself, so the keyboard can reach it.
          // eslint-disable-next-line jsx-a11y-x/no-noninteractive-tabindex
          tabIndex={0}
          aria-label={`${block.title}, as text`}
          className="max-h-(--detail-max-height) overflow-auto rounded-md border border-border-subtle bg-surface-sunken p-3 font-mono text-12 wrap-anywhere whitespace-pre-wrap text-text-secondary"
        >
          {block.raw}
        </pre>
      )}
    </Block>
  );
}

const VALUE_CLASSES: Record<DataNode['kind'], string> = {
  string: 'text-code-string',
  number: 'text-code-number',
  boolean: 'text-code-keyword',
  null: 'text-code-comment',
  object: 'text-text-secondary',
  array: 'text-text-secondary',
};

/** A node of the tree: a value on its line, or an object or list that opens to its children. */
function Node({ node, depth, root = false }: { node: DataNode; depth: number; root?: boolean }) {
  const [open, setOpen] = useState(depth <= OPEN_DEPTH);
  const [shown, setShown] = useState(CHILD_PAGE);
  const groupId = useId();
  const branch = node.kind === 'object' || node.kind === 'array';
  const key = node.key === null ? null : <span className="text-code-attribute">{node.key}</span>;

  if (!branch) {
    return (
      <li className="flex gap-2 py-0.5 pl-5">
        {key ? <span className="shrink-0">{key}</span> : null}
        <span className={`min-w-0 wrap-anywhere ${VALUE_CLASSES[node.kind]}`}>
          {node.kind === 'string' ? `"${node.label}"` : node.label}
        </span>
      </li>
    );
  }

  const { children, more } = visibleChildren(node, shown);
  return (
    <li>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={groupId}
        onClick={() => setOpen((value) => !value)}
        className="flex min-h-(--target-min) max-w-full items-center gap-1 rounded-xs text-left hover:bg-surface-hover"
      >
        <Icon icon={open ? ChevronDown : ChevronRight} size="xs" className="shrink-0 text-text-muted" />
        {key ?? (root ? null : <span className="text-text-muted">item</span>)}
        <span className={`min-w-0 font-sans wrap-anywhere ${VALUE_CLASSES[node.kind]}`}>{node.label}</span>
      </button>
      {open ? (
        <ul id={groupId} className="ml-2 border-l border-border-subtle pl-2">
          {children.map((child, index) => (
            <Node key={`${child.key ?? ''}-${index}`} node={child} depth={depth + 1} />
          ))}
          {more > 0 ? (
            <li className="py-1 pl-5 font-sans">
              <button
                type="button"
                className="si-link min-h-(--target-min) text-12"
                onClick={() => setShown((value) => value + CHILD_PAGE)}
              >
                Show {formatCount(Math.min(more, CHILD_PAGE))} more
                <span className="text-text-muted"> of {formatCount(more)}</span>
              </button>
            </li>
          ) : null}
          {node.children.length === 0 ? <li className="py-0.5 pl-5 font-sans text-text-muted">Empty</li> : null}
        </ul>
      ) : null}
    </li>
  );
}
