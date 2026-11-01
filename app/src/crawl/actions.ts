// The crawl actions, once: the top bar's buttons, the palette, F7 and F8 all call these, so they behave the same.
// Failures become toasts with the plain cause; results arrive as events, which update the screens.
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { CrawlListItem, CrawlStatus } from '../api/types';
import { notifyError, notifyResult } from '../feedback/notify';

export async function pauseOrResume(id: string, status: CrawlStatus | null): Promise<void> {
  try {
    if (status === 'paused') await api.resumeCrawl(id);
    else if (status === 'running') await api.pauseCrawl(id);
  } catch (error) {
    notifyError(error, { retry: () => void pauseOrResume(id, status) });
  }
}

export async function stopCrawl(id: string): Promise<void> {
  try {
    await api.stopCrawl(id);
  } catch (error) {
    notifyError(error, { retry: () => void stopCrawl(id) });
  }
}

/** Continues a stopped crawl. When its headers or proxy password must be given again, says so plainly. */
export async function continueCrawl(id: string, options: { skipMissingHeaders?: boolean } = {}): Promise<boolean> {
  try {
    await api.continueCrawl(id, options.skipMissingHeaders ? { skipMissingHeaders: true } : {});
    return true;
  } catch (error) {
    if (isProblem(error, 'needs-secrets')) {
      const headersOnly = !error.proxyCredentials && error.missingHeaders.length > 0;
      notifyResult(error.title, {
        description: headersOnly
          ? `Its headers (${error.missingHeaders.join(', ')}) aren’t kept in the crawl file. Enter them again in its settings, or continue without them.`
          : 'Its proxy password isn’t kept in the crawl file. Enter it again in the crawl’s settings to continue.',
        ...(headersOnly
          ? {
              action: {
                label: 'Continue without',
                onClick: () => void continueCrawl(id, { skipMissingHeaders: true }),
              },
            }
          : {}),
      });
      return false;
    }
    notifyError(error, { retry: () => void continueCrawl(id, options) });
    return false;
  }
}

export async function analyseCrawl(id: string): Promise<void> {
  try {
    await api.analyze(id);
  } catch (error) {
    notifyError(error, { retry: () => void analyseCrawl(id) });
  }
}

/** Starts a crawl of `startUrl` with the user's defaults. Field problems come back for the form to show inline. */
export async function startCrawl(
  startUrl: string,
): Promise<{ ok: true; item: CrawlListItem } | { ok: false; fieldErrors: Record<string, string[]> }> {
  try {
    return { ok: true, item: await api.startCrawl({ options: { startUrl } }) };
  } catch (error) {
    if (isProblem(error, 'invalid-options')) return { ok: false, fieldErrors: error.errors };
    notifyError(error);
    return { ok: false, fieldErrors: {} };
  }
}
