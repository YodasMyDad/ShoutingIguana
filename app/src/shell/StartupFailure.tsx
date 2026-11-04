import { useState } from 'react';
import { copyText } from '../lib/clipboard';
import { diagnostics, errorText } from './diagnostics';

/**
 * What the window shows when the app couldn't start (the shell's IPC failed, say), instead of staying blank: a plain
 * sentence, and the diagnostics to copy into a report. It needs nothing that may have failed: no router, no queries,
 * no engine.
 */
export function StartupFailure({ error }: { error: unknown }) {
  const [copied, setCopied] = useState<boolean | null>(null);

  return (
    <main className="flex h-full items-center justify-center bg-surface p-8 text-text">
      <div role="alert" className="flex max-w-(--container-content) flex-col items-start gap-3">
        <h1 className="text-16 font-semibold">Shouting Iguana couldn’t start</h1>
        <p className="text-13 text-text-secondary">
          Something went wrong while the app was starting. Close it and open it again. If it keeps happening, copy the
          diagnostics and include them when you report it.
        </p>
        <p className="font-mono text-12 break-all text-text-secondary">{errorText(error)}</p>
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => void diagnostics(error).then(copyText).then(setCopied)}
            className="h-(--control-height-sm) rounded-sm bg-accent-fill px-3 text-13 font-medium text-text-on-accent hover:bg-accent-fill-hover"
          >
            Copy diagnostics
          </button>
          <span role="status" className="text-12 text-text-secondary">
            {copied === null ? '' : copied ? 'Copied' : 'Couldn’t copy: select the text above instead.'}
          </span>
        </div>
      </div>
    </main>
  );
}
