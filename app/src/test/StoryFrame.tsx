// Frames for the design system's stories: the canvas and a sheet to set components on, rows that name each state,
// and a router for the components that link (TextLink, NavItem, StatLink).
import { RouterContextProvider, createMemoryHistory, createRootRoute, createRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { useState, type ReactNode } from 'react';

/** The page behind a story: the app's canvas, padded, in its theme. */
export function StoryFrame({ children, sheet = false }: { children: ReactNode; sheet?: boolean }) {
  return (
    <main className="min-h-screen bg-canvas p-8 text-text">
      <div className={clsx('flex flex-col gap-6', sheet && 'si-card p-6')}>{children}</div>
    </main>
  );
}

/** A group of states under a heading. */
export function StorySection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-3">
      <h2 className="text-12 font-semibold text-text-secondary">{title}</h2>
      {children}
    </section>
  );
}

/** One state: its name on the left, the component on the right. */
export function StateRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-h-(--control-height) items-center gap-4">
      <span className="w-(--status-label-width) shrink-0 text-12 text-text-muted">{label}</span>
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-3">{children}</div>
    </div>
  );
}

/** A surface to check a component against: the canvas, a sheet, a selected row, the sunken band. */
export function OnSurfaces({ children }: { children: ReactNode }) {
  const surfaces = [
    ['Canvas', 'bg-canvas'],
    ['Surface', 'bg-surface'],
    ['Selected', 'bg-surface-selected'],
    ['Sunken', 'bg-surface-sunken'],
  ] as const;
  return (
    <div className="grid grid-cols-4 gap-3">
      {surfaces.map(([name, background]) => (
        <div key={name} className={clsx('flex flex-col gap-2 rounded-md border border-border p-4', background)}>
          <span className="text-12 text-text-muted">{name}</span>
          {children}
        </div>
      ))}
    </div>
  );
}

/** A router for stories of links: the page shown is `path`, so the link to it is current. */
export function StoryRouter({ path = '/', children }: { path?: string; children: ReactNode }) {
  const [router] = useState(() =>
    createRouter({
      routeTree: createRootRoute(),
      history: createMemoryHistory({ initialEntries: [path] }),
    }),
  );
  return <RouterContextProvider router={router}>{children}</RouterContextProvider>;
}
