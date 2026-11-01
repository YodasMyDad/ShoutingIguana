import type { ReactNode } from 'react';

/**
 * A screen's place before its slice builds it: the title, what it's for, and anything the foundation already has.
 * Placeholder styling.
 */
export function ScreenStub({ title, purpose, children }: { title: string; purpose: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col gap-6 px-(--page-gutter) py-6">
      <header className="flex flex-col gap-1">
        <h1 className="text-21 font-semibold tracking-snug">{title}</h1>
        <p className="max-w-(--container-content) text-14 text-text-secondary">{purpose}</p>
      </header>
      {children}
    </div>
  );
}
