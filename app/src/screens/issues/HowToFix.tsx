import * as TabsPrimitive from '@radix-ui/react-tabs';
import clsx from 'clsx';
import { useState } from 'react';
import type { Check, FixExample } from '../../api/types';
import { Badge } from '../../ui/Badge';
import { CodeExample } from '../../ui/CodeExample';

export interface HowToFixProps {
  /** The check: its fix in words, and its fix as code where it has some. */
  check: Pick<Check, 'fix' | 'fixExamples'>;
  /**
   * The case what the crawl found points to (`preferredFixCase`): it shows first, marked "On this site". Left out, or
   * a case the check doesn't have, the check's first case shows.
   */
  preferredCase?: string | undefined;
  className?: string | undefined;
}

function Example({ example }: { example: FixExample }) {
  return (
    <CodeExample
      className="si-how-to-fix__example"
      language={example.language}
      before={example.before}
      after={example.after}
      where={example.where}
    />
  );
}

/**
 * The cases of a check with several fixes, one showing at a time: tabs that look like the segmented control, each
 * named by when it applies. The arrow keys move between them and show each as they go. The choice lasts while the
 * check is open and isn't kept.
 */
function Cases({ examples, preferredCase }: { examples: readonly FixExample[]; preferredCase: string | undefined }) {
  // A case's key among its check's: its own, or its place for one without.
  const keyOf = (index: number) => examples[index]?.case ?? `case-${index}`;
  const preferred = examples.findIndex((example) => example.case !== null && example.case === preferredCase);
  const [chosen, setChosen] = useState<string | null>(null);
  // Nothing chosen yet, or a case the check no longer has (the check changed under the pane): the case the crawl
  // points to, else the first.
  const known = examples.some((_, index) => keyOf(index) === chosen);
  const current = chosen !== null && known ? chosen : keyOf(Math.max(preferred, 0));
  return (
    <TabsPrimitive.Root className="si-fix-cases" value={current} onValueChange={setChosen}>
      <TabsPrimitive.List aria-label="Which case is it" className="si-segmented si-segmented--wrap si-fix-cases__list">
        {examples.map((example, index) => (
          <TabsPrimitive.Trigger key={keyOf(index)} value={keyOf(index)} className="si-segment">
            {example.when}
            {index === preferred ? (
              <Badge variant="outline" className="si-fix-cases__marker">
                On this site
              </Badge>
            ) : null}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
      {examples.map((example, index) => (
        <TabsPrimitive.Content key={keyOf(index)} value={keyOf(index)} className="si-fix-cases__panel">
          <Example example={example} />
        </TabsPrimitive.Content>
      ))}
    </TabsPrimitive.Root>
  );
}

/**
 * A check's "How to fix it": the catalog's words, then the fix as code where the check has it, so what to type is in
 * front of the reader. One example shows as it is (under its condition, when it has one); several show one at a time,
 * the case the crawl points to first. A check without examples is its words alone, as it always was.
 */
export function HowToFix({ check, preferredCase, className }: HowToFixProps) {
  const examples = check.fixExamples ?? [];
  const only = examples.length === 1 ? examples[0] : undefined;
  if (check.fix.trim() === '' && examples.length === 0) return null;
  return (
    <div className={clsx('si-how-to-fix', className)}>
      <h3 className="mb-1.5 text-13 font-semibold">How to fix it</h3>
      {check.fix.trim() !== '' ? (
        <p className="max-w-(--measure-text) text-14 leading-body text-text-secondary">{check.fix}</p>
      ) : null}
      {only ? (
        <>
          {only.when ? <p className="si-how-to-fix__when">{only.when}:</p> : null}
          <Example example={only} />
        </>
      ) : null}
      {examples.length > 1 ? <Cases examples={examples} preferredCase={preferredCase} /> : null}
    </div>
  );
}
