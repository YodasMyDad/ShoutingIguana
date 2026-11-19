import clsx from 'clsx';
import { Check, Copy } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { announce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { Button } from './Button';
import { diffCode, type DiffPart } from './codeDiff';
import { languageName, tokenizeCode, type CodeToken } from './codeTokens';

/** How long "Copied" stays on the button after a click: as long as a copied URL's tooltip says it. */
const COPIED_MS = 1600;

export interface CodeExampleProps {
  /** What the code is written in (`html`, `httpHeader`, `robotsTxt`, `xml`, `json`): any other word shows it plain. */
  language: string;
  /** The corrected code, or the code to add. */
  after: string;
  /** The code as it is now. Left out or null, the example is something to add, and shows one block. */
  before?: string | null | undefined;
  /** Where the code goes, said after the first label: "in the page's <head>". */
  where?: string | null | undefined;
  className?: string | undefined;
}

/** A side's parts, each with the coloured tokens it covers: the two cuts of the same text laid over each other. */
function coloured(
  parts: readonly DiffPart[],
  tokens: readonly CodeToken[],
): { changed: boolean; tokens: CodeToken[] }[] {
  const rest = tokens.map((token) => ({ ...token }));
  return parts.map((part) => {
    const covered: CodeToken[] = [];
    let wanted = part.text.length;
    for (let next = rest[0]; wanted > 0 && next; next = rest[0]) {
      const taken = next.text.slice(0, wanted);
      covered.push({ text: taken, kind: next.kind });
      wanted -= taken.length;
      if (taken.length === next.text.length) rest.shift();
      else next.text = next.text.slice(taken.length);
    }
    return { changed: part.changed, tokens: covered };
  });
}

function Tokens({ tokens }: { tokens: readonly CodeToken[] }) {
  return tokens.map((token, index) =>
    token.kind === 'plain' ? (
      token.text
    ) : (
      // The tokens of one fixed string, never reordered: their place is their key.
      <span key={index} className={`si-code-example__${token.kind}`}>
        {token.text}
      </span>
    ),
  );
}

/** A block's code: coloured, with what changed in an `<ins>` or a `<del>`, so it's marked for a screen reader too. */
function Code({
  code,
  language,
  parts,
  mark,
}: {
  code: string;
  language: string;
  parts: readonly DiffPart[] | null;
  mark: 'ins' | 'del';
}) {
  const runs = useMemo(
    () => coloured(parts ?? [{ text: code, changed: false }], tokenizeCode(code, language)),
    [code, language, parts],
  );
  const Mark = mark;
  return (
    <pre className="si-code-example__code">
      <code>
        {runs.map((run, index) =>
          run.changed ? (
            <Mark key={index} className={mark === 'ins' ? 'si-code-example__added' : 'si-code-example__removed'}>
              <Tokens tokens={run.tokens} />
            </Mark>
          ) : (
            <span key={index}>
              <Tokens tokens={run.tokens} />
            </span>
          ),
        )}
      </code>
    </pre>
  );
}

function Head({ label, where, children }: { label: string; where?: string | null | undefined; children?: ReactNode }) {
  return (
    <div className="si-code-example__head">
      {/* One run of words, so a long "where" wraps under the label like a sentence. */}
      <p className="si-code-example__words">
        <span className="si-code-example__label">{label}</span>
        {where ? (
          <span className="si-code-example__where">
            <span aria-hidden="true"> · </span>
            {where}
          </span>
        ) : null}
      </p>
      <span className="si-code-example__end">{children}</span>
    </div>
  );
}

const listed = (parts: readonly DiffPart[]) =>
  parts
    .filter((part) => part.changed)
    .map((part) => part.text)
    .join(' and ');

/**
 * A fix as code, to read in a glance and copy: the code as it is ("Now") over the code as it should be ("Fixed"), the
 * part that goes struck through and the part that's new marked, or one block to "Add" with where it goes. The labels
 * are words; the marks are an underline and a strike as well as a tint, so they survive forced colours; what changed is
 * said in a sentence for screen readers. Long lines wrap: the example never scrolls sideways, and never grows past
 * about 100 characters.
 */
export function CodeExample({ language, after, before = null, where, className }: CodeExampleProps) {
  const diff = useMemo(() => (before === null ? null : diffCode(before, after)), [before, after]);
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  const fixes = before !== null;
  const copy = async () => {
    const done = await copyText(after);
    setCopied(done);
    announce(done ? (fixes ? 'Copied the fixed code.' : 'Copied the code.') : 'The code couldn’t be copied.');
  };
  const copyName = fixes ? 'the fixed code' : 'the code to add';
  const name = languageName(language);
  const added = diff ? listed(diff.after) : '';
  const removed = diff ? listed(diff.before) : '';
  const languageTag = name ? <span className="si-code-example__language">{name}</span> : null;
  const button = (
    <Button
      size="sm"
      variant="ghost"
      icon={copied ? Check : Copy}
      aria-label={copied ? `Copied ${copyName}` : `Copy ${copyName}`}
      onClick={() => void copy()}
    >
      {copied ? 'Copied' : 'Copy'}
    </Button>
  );

  return (
    <figure className={clsx('si-code-example', className)} aria-label={fixes ? 'The fix as code' : 'The code to add'}>
      {before !== null ? (
        <div className="si-code-example__block si-code-example__block--now">
          <Head label="Now" where={where}>
            {languageTag}
          </Head>
          <Code code={before} language={language} parts={diff?.before ?? null} mark="del" />
        </div>
      ) : null}
      <div className="si-code-example__block si-code-example__block--fixed">
        <Head label={fixes ? 'Fixed' : 'Add'} where={fixes ? null : where}>
          {fixes ? null : languageTag}
          {button}
        </Head>
        <Code code={after} language={language} parts={diff?.after ?? null} mark="ins" />
      </div>
      {added !== '' || removed !== '' ? (
        <figcaption className="sr-only">
          {[removed === '' ? null : `Taken out: ${removed}.`, added === '' ? null : `Added: ${added}.`]
            .filter((sentence) => sentence !== null)
            .join(' ')}
        </figcaption>
      ) : null}
    </figure>
  );
}
