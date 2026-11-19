// The report's document (docs/v2-sidecar-api.md, "Report (E5)"), as text for the frame's `srcdoc`. Under the crawl's
// prefix, so whatever changes the crawl (an analysis ending, a crawl continued) makes it read again.
import { queryOptions } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { ReportCut, ReportWording } from '../../api/exports';
import type { Mode } from '../../mode/mode';

/** Whose words a mode's report is in: Simple's says what Simple's screens say, Advanced's speaks as the checks do. */
export const reportWording = (mode: Mode): ReportWording => (mode === 'simple' ? 'plain' : 'technical');

/**
 * `words`: whose words the rows are in, the mode's. `hiddenChecks`: the checks the user hid in this crawl, left out of
 * the report as of the one saved.
 */
export const reportDocumentQuery = (
  id: string,
  cut: ReportCut,
  words: ReportWording,
  hiddenChecks: readonly string[] = [],
) =>
  queryOptions({
    queryKey: ['crawl', id, 'report', cut, words, hiddenChecks.join(',')] as const,
    queryFn: ({ signal }) =>
      api.report(id, { cut, words, ...(hiddenChecks.length > 0 ? { hiddenChecks: [...hiddenChecks] } : {}) }, signal),
    staleTime: Infinity,
  });
