// The exports this app knows of, by id: from the call that started each, from the live stream's `export` events, and
// from `hello.exports` after a reload or a reconnect. They outlive the screen that started them: the toasts and the
// sidebar's list read them here. Outside React, as the live store is; components subscribe with `useExports`.
import { useSyncExternalStore } from 'react';
import type { ExportJob } from '../api/exports';
import { nextPeak } from './exportWords';
import type { ExportAgain } from './exportOffer';

export interface TrackedExport {
  job: ExportJob;
  /** The share the bar shows, 0 to 1: never less than it showed before. */
  peak: number;
  /** What it is, from the screen that started it ("URLs", "This check"); null for one the engine told of alone. */
  label: string | null;
  /** How to make the same export again (another file, as CSV), for a problem's action; null when not known here. */
  again: ExportAgain | null;
  /** Each column's title, for the columns a finished export left empty. */
  columnTitles: Readonly<Record<string, string>>;
  /** The engine that ran it went before it ended: no file was written, and it can't be asked about any more. */
  lost: boolean;
  /** When this app first heard of it, by `performance.now()`. */
  heardAt: number;
  /** When this app heard it had ended, by `performance.now()`; null while it runs. */
  endedAt: number | null;
}

export interface ExportMeta {
  label?: string | null;
  again?: ExportAgain | null;
  columnTitles?: Readonly<Record<string, string>>;
}

const ORDER: Record<ExportJob['status'], number> = { running: 0, done: 1, cancelled: 1, failed: 1 };

/**
 * Whether `incoming` is news against what's held. One export's events come in order, but the call that started it can
 * answer after its first events have arrived, and a read after a reconnect can cross an event: an ended export never
 * goes back to running, and a running one never goes back in rows.
 */
export function isNewer(held: ExportJob, incoming: ExportJob): boolean {
  if (ORDER[incoming.status] !== ORDER[held.status]) return ORDER[incoming.status] > ORDER[held.status];
  if (incoming.status !== 'running') return false;
  if (incoming.rowsWritten !== held.rowsWritten) return incoming.rowsWritten > held.rowsWritten;
  return held.rowsExpected === null && incoming.rowsExpected !== null;
}

/** How many ended exports are kept for the list and their toasts: the engine keeps its last 64. */
const KEEP_ENDED = 32;

export class ExportStore {
  private exports = new Map<string, TrackedExport>();
  private snapshot: readonly TrackedExport[] = [];
  private readonly listeners = new Set<() => void>();

  constructor(private readonly now: () => number = () => performance.now()) {}

  /** Takes an export as the engine says it is now. Returns what's held for it afterwards. */
  accept(job: ExportJob, meta: ExportMeta = {}): TrackedExport {
    const held = this.exports.get(job.exportId);
    const now = this.now();
    if (held) {
      const newer = isNewer(held.job, job);
      const next: TrackedExport = {
        ...held,
        ...(newer ? { job, peak: nextPeak(held.peak, job) } : {}),
        label: meta.label ?? held.label,
        again: meta.again ?? held.again,
        columnTitles: meta.columnTitles ?? held.columnTitles,
        endedAt: held.endedAt ?? (newer && job.status !== 'running' ? now : null),
      };
      if (newer || meta.label !== undefined || meta.again !== undefined || meta.columnTitles !== undefined) {
        this.set(next);
      }
      return next;
    }
    const tracked: TrackedExport = {
      job,
      peak: nextPeak(0, job),
      label: meta.label ?? null,
      again: meta.again ?? null,
      columnTitles: meta.columnTitles ?? {},
      lost: false,
      heardAt: now,
      endedAt: job.status === 'running' ? null : now,
    };
    this.set(tracked);
    return tracked;
  }

  /** The engine that ran it has gone, and the next one doesn't know it: it ended without a file. */
  markLost(exportId: string): void {
    const held = this.exports.get(exportId);
    if (held?.job.status !== 'running' || held.lost) return;
    this.set({ ...held, lost: true, endedAt: this.now() });
  }

  get(exportId: string): TrackedExport | undefined {
    return this.exports.get(exportId);
  }

  /** Every export known, oldest first. */
  list(): readonly TrackedExport[] {
    return this.snapshot;
  }

  /** The exports still running (and not lost). */
  running(): TrackedExport[] {
    return this.snapshot.filter((tracked) => tracked.job.status === 'running' && !tracked.lost);
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** For tests and stories: forgets every export. */
  clear(): void {
    this.exports.clear();
    this.publish();
  }

  private set(tracked: TrackedExport): void {
    this.exports.set(tracked.job.exportId, tracked);
    const ended = [...this.exports.values()].filter((each) => each.endedAt !== null);
    for (const old of ended.slice(0, Math.max(0, ended.length - KEEP_ENDED))) this.exports.delete(old.job.exportId);
    this.publish();
  }

  private publish(): void {
    this.snapshot = [...this.exports.values()];
    for (const listener of this.listeners) listener();
  }
}

export const exportStore = new ExportStore();

/** Every export known, oldest first; renders again as any of them changes. */
export function useExports(store: ExportStore = exportStore): readonly TrackedExport[] {
  return useSyncExternalStore(
    (listener) => store.subscribe(listener),
    () => store.list(),
  );
}

/** One export, by id. */
export function useExport(exportId: string, store: ExportStore = exportStore): TrackedExport | undefined {
  return useSyncExternalStore(
    (listener) => store.subscribe(listener),
    () => store.get(exportId),
  );
}
