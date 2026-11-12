import { Check, Copy } from 'lucide-react';
import { useEffect, useId, useState, type ReactNode } from 'react';
import { announce } from '../../feedback/announce';
import { copyText } from '../../lib/clipboard';
import { Button } from '../../ui/Button';
import { PageHeader } from '../../ui/PageHeader';

/** How long a copy button says "Copied". */
const COPIED_MS = 2_000;

/** A section of Settings: its title, what it's for, and its settings. */
export function SectionPage({ title, purpose, children }: { title: string; purpose: ReactNode; children: ReactNode }) {
  return (
    <div className="flex max-w-(--form-max) flex-col gap-7">
      <PageHeader title={title} subtitle={purpose} />
      {children}
    </div>
  );
}

/**
 * One setting: its name, what it does in a line, and its control. The name labels a group around the control, so a
 * control that is a group itself (radios, segments) is named by it.
 */
export function Setting({ label, help, children }: { label: string; help: ReactNode; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={`${id}-label`} className="flex flex-col items-start gap-2">
      <div className="flex flex-col gap-0.5">
        <h2 id={`${id}-label`} className="text-14 font-semibold">
          {label}
        </h2>
        <p className="text-13 text-text-secondary">{help}</p>
      </div>
      {children}
    </section>
  );
}

/**
 * Copies text, and says so: "Copied" on the button for a moment, and once to a screen reader. Its name stays its
 * words, so the announcement is what tells.
 */
export function CopyButton({ label, text }: { label: string; text: () => string | Promise<string> }) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);
  return (
    <Button
      size="sm"
      icon={copied ? Check : Copy}
      onClick={() =>
        void Promise.resolve(text())
          .then(copyText)
          .then((done) => {
            setCopied(done);
            announce(done ? 'Copied.' : 'It couldn’t be copied.');
          })
      }
    >
      {label}
    </Button>
  );
}

/** A folder's path, with Copy beside it. */
export function PathRow({ label, path }: { label: string; path: string | null }) {
  return (
    <div className="flex w-full flex-wrap items-center gap-3">
      <code className="min-w-0 flex-1 rounded-sm border border-border-subtle bg-surface-sunken px-2.5 py-1.5 font-mono text-12 break-all text-text-secondary">
        {path ?? '–'}
      </code>
      {path ? <CopyButton label={`Copy the ${label}`} text={() => path} /> : null}
    </div>
  );
}
