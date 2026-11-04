// An analysis in words: its phases while it runs (E4's `analysis` events) and why a finished one is out of date. One
// place, so Issues, Overview and the inspector say it the same way.
import type { AnalysisEvent, OutOfDateReason } from '../api/types';
import { formatCount } from '../lib/format';

/** E4's phases, in the order an analysis runs them (docs/v2-sidecar-api.md, Analysis progress). */
export const ANALYSIS_PHASES = ['reading', 'pageRules', 'siteRules', 'urlSummary', 'writing'] as const;

export type AnalysisPhase = (typeof ANALYSIS_PHASES)[number];

const PHASES: Record<AnalysisPhase, { words: string; unit: readonly [string, string] | null }> = {
  reading: { words: 'Reading the crawl', unit: ['URL', 'URLs'] },
  pageRules: { words: 'Checking each page', unit: ['page', 'pages'] },
  siteRules: { words: 'Checking across pages', unit: ['check', 'checks'] },
  urlSummary: { words: 'Summing up each URL', unit: ['URL', 'URLs'] },
  writing: { words: 'Saving the results', unit: null },
};

function phaseOf(phase: string): AnalysisPhase | null {
  return ANALYSIS_PHASES.find((candidate) => candidate === phase) ?? null;
}

/** "Checking each page", or "Analysing" for a phase this app doesn't know (a newer engine's). */
export function analysisPhaseWords(phase: string): string {
  const known = phaseOf(phase);
  return known ? PHASES[known].words : 'Analysing';
}

/** "Step 2 of 5", for a phase this app knows. */
export function analysisStep(phase: string): string | null {
  const known = phaseOf(phase);
  return known ? `Step ${ANALYSIS_PHASES.indexOf(known) + 1} of ${ANALYSIS_PHASES.length}` : null;
}

/** "1,204 of 4,521 pages", or null where the phase counts nothing worth saying (writing). */
export function analysisProgressText(event: Pick<AnalysisEvent, 'phase' | 'done' | 'total'>): string | null {
  const known = phaseOf(event.phase);
  const unit = known ? PHASES[known].unit : null;
  if (!unit || event.total <= 0) return null;
  return `${formatCount(event.done)} of ${formatCount(event.total)} ${event.total === 1 ? unit[0] : unit[1]}`;
}

const OUT_OF_DATE: Record<OutOfDateReason, string> = {
  crawlChanged: 'The crawl was continued after it was analysed.',
  engineChanged: 'Another version of Shouting Iguana analysed it, so its checks may differ from this version’s.',
  settingsChanged: 'The analysis settings changed after it was analysed.',
};

/** Why the last analysis no longer stands, in plain words, one sentence per reason. */
export function outOfDateWords(reasons: readonly OutOfDateReason[]): string {
  const words = reasons.map((reason) => OUT_OF_DATE[reason]).filter(Boolean);
  return words.length > 0 ? words.join(' ') : 'The crawl changed after it was analysed.';
}
