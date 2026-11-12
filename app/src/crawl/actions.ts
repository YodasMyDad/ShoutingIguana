// The crawl actions, once: the top bar's buttons, the palette, F7 and F8 all call these, so they behave the same.
// Failures become toasts with the plain cause, one per crawl and action however often it's pressed; results arrive as
// events, which update the screens.
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { CrawlListItem, CrawlStatus } from '../api/types';
import { crawlToast, notifyError, notifyResult } from '../feedback/notify';

/**
 * Pause, Resume and Stop act on a crawl this sidecar is running: `crawl-busy` says it isn't (it ended, or another
 * program runs it), and trying again can't change that. A lost connection can come back, so that keeps its Retry.
 */
function retryWhileLive(error: unknown, retry: () => void): { retry?: () => void } {
  return isProblem(error, 'crawl-busy') ? {} : { retry };
}

export async function pauseOrResume(id: string, status: CrawlStatus | null): Promise<void> {
  try {
    if (status === 'paused') await api.resumeCrawl(id);
    else if (status === 'running') await api.pauseCrawl(id);
  } catch (error) {
    notifyError(error, {
      id: crawlToast(id, 'pause'),
      ...retryWhileLive(error, () => void pauseOrResume(id, status)),
    });
  }
}

export async function stopCrawl(id: string): Promise<void> {
  try {
    await api.stopCrawl(id);
  } catch (error) {
    notifyError(error, { id: crawlToast(id, 'stop'), ...retryWhileLive(error, () => void stopCrawl(id)) });
  }
}

/** Opens a crawl's settings, where what Continue asks for again is given: the shell sets it, with its router. */
let openCrawlSettings: ((id: string) => void) | null = null;

export function setCrawlSettingsOpener(open: ((id: string) => void) | null): void {
  openCrawlSettings = open;
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
        id: crawlToast(id, 'continue'),
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
          : openCrawlSettings
            ? { action: { label: 'Open its settings', onClick: () => openCrawlSettings?.(id) } }
            : {}),
      });
      return false;
    }
    // A crawl busy elsewhere (another program has it) can be continued once that's done: Retry stays.
    notifyError(error, { id: crawlToast(id, 'continue'), retry: () => void continueCrawl(id, options) });
    return false;
  }
}

export async function analyseCrawl(id: string): Promise<void> {
  try {
    await api.analyze(id);
  } catch (error) {
    // Busy means an analysis already runs, or the file is being read: either ends, so Retry can work.
    notifyError(error, { id: crawlToast(id, 'analyse'), retry: () => void analyseCrawl(id) });
  }
}

/** Cancels a running analysis: nothing it did is kept, and the last analysis stands. */
export async function cancelAnalysis(id: string): Promise<void> {
  try {
    await api.cancelAnalysis(id);
  } catch (error) {
    notifyError(error, { id: crawlToast(id, 'cancel-analysis'), retry: () => void cancelAnalysis(id) });
  }
}

/**
 * Starts a crawl of `startUrl` with the user's defaults: the saved ones, read now, since a setting left out of the
 * request takes the engine's default, not the user's. A saved header has its name only (its value isn't kept), so it
 * isn't sent. Field problems come back for the form to show inline.
 */
export async function startCrawl(
  startUrl: string,
): Promise<{ ok: true; item: CrawlListItem } | { ok: false; fieldErrors: Record<string, string[]> }> {
  try {
    const saved = { ...(await api.crawlOptionDefaults()).options };
    // A response's own fact, not an option to send.
    delete saved.hasRedactedProxyCredentials;
    const extraHeaders = Object.fromEntries(Object.entries(saved.extraHeaders ?? {}).filter(([, value]) => value));
    return { ok: true, item: await api.startCrawl({ options: { ...saved, extraHeaders, startUrl } }) };
  } catch (error) {
    if (isProblem(error, 'invalid-options')) return { ok: false, fieldErrors: error.errors };
    notifyError(error);
    return { ok: false, fieldErrors: {} };
  }
}
