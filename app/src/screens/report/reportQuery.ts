// The report's document (docs/v2-sidecar-api.md, "Report (E5)"), as text for the frame's `srcdoc`. Under the crawl's
// prefix, so whatever changes the crawl (an analysis ending, a crawl continued) makes it read again.
import { queryOptions } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { ReportCut } from '../../api/exports';

export const reportDocumentQuery = (id: string, cut: ReportCut) =>
  queryOptions({
    queryKey: ['crawl', id, 'report', cut] as const,
    queryFn: ({ signal }) => api.report(id, { cut }, signal),
    staleTime: Infinity,
  });
