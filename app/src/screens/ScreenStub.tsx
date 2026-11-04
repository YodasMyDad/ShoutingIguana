import type { ReactNode } from 'react';
import { PageHeader } from '../ui/PageHeader';

/** A screen's place before its slice builds it: the title, what it's for, and anything the foundation already has. */
export function ScreenStub({ title, purpose, children }: { title: string; purpose: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col gap-6 px-(--page-gutter) pt-7 pb-10">
      <PageHeader title={title} subtitle={purpose} />
      {children}
    </div>
  );
}
