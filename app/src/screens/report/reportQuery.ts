// The report's document (docs/v2-sidecar-api.md, "Report (E5)"), as text for the frame's `srcdoc`. Under the crawl's
// prefix, so whatever changes the crawl (an analysis ending, a crawl continued) makes it read again.
import { queryOptions } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { ReportCut } from '../../api/exports';

/** `hiddenChecks`: the checks the user hid in this crawl, left out of the report as of the one saved. */
export const reportDocumentQuery = (id: string, cut: ReportCut, hiddenChecks: readonly string[] = []) =>
  queryOptions({
    queryKey: ['crawl', id, 'report', cut, hiddenChecks.join(',')] as const,
    queryFn: ({ signal }) =>
      api.report(id, { cut, ...(hiddenChecks.length > 0 ? { hiddenChecks: [...hiddenChecks] } : {}) }, signal),
    staleTime: Infinity,
  });
