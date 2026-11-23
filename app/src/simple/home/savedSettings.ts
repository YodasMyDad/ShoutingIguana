// The line under Home's field (docs/v2-ui-simple.md, Home): what a bare Enter does. The engine's defaults read "Checks
// the whole site, at a speed that's safe for most hosting"; saved defaults that differ say so instead, in Simple's
// words for what Simple asks, and a count of what's set in Advanced. Pure.
import type { CrawlOptionDefaults } from '../../api/types';
import { ENGINE_DEFAULTS } from '../../crawl/options/engineDefaults';
import { changedFields, formFromOptions, renderingOf, SIMPLE_FIELDS, speedOf } from '../../crawl/options/form';
import { speedTitle } from '../../crawl/options/words';
import { formatCount } from '../../lib/format';

export const ENGINE_LINE = 'Checks the whole site, at a speed that’s safe for most hosting.';

/**
 * "Uses your saved settings: Polite speed, up to 5,000 addresses, and 3 more set in Advanced.", or the engine's line
 * when nothing saved differs from the engine's defaults.
 */
export function savedSettingsLine(defaults: CrawlOptionDefaults): string {
  const form = formFromOptions(defaults.options);
  const changed = changedFields(form, formFromOptions(ENGINE_DEFAULTS));
  if (changed.length === 0) return ENGINE_LINE;

  const parts: string[] = [];
  if (changed.includes('concurrency') || changed.includes('maxRequestsPerSecond')) {
    const speed = speedOf(form, defaults.speedPresets);
    parts.push(speed ? `${speedTitle(speed)} speed` : 'your own speed');
  }
  if (changed.includes('maxUrls')) {
    const limit = Number(form.maxUrls);
    parts.push(limit > 0 ? `up to ${formatCount(limit)} ${limit === 1 ? 'address' : 'addresses'}` : 'the whole site');
  }
  if (changed.includes('checkExternalLinks')) {
    parts.push(form.checkExternalLinks ? 'links to other websites checked' : 'links to other websites not checked');
  }
  if (changed.includes('checkResources')) {
    parts.push(form.checkResources ? 'images and other files checked' : 'images and other files not checked');
  }
  if (changed.includes('javaScriptRendering')) {
    const rendering = renderingOf(form.javaScriptRendering, 'automatic');
    parts.push(
      rendering === 'off'
        ? 'JavaScript off'
        : rendering === 'javaScript'
          ? 'JavaScript on every page'
          : rendering === 'matching'
            ? 'JavaScript on listed URLs'
            : 'JavaScript on shells only',
    );
  }
  // The speed is two fields, but one setting as Simple asks it; the rest are counted, as Advanced's.
  const advanced = changed.filter((field) => !SIMPLE_FIELDS.has(field)).length;
  // A simple setting this line doesn't name (a list of addresses, a sign-in) still mustn't leave "settings: ."
  if (parts.length === 0 && advanced === 0) return 'Uses your saved settings.';
  if (advanced === 0) return `Uses your saved settings: ${listOf(parts)}.`;
  if (parts.length === 0) {
    return `Uses your saved settings: ${formatCount(advanced)} ${advanced === 1 ? 'setting' : 'settings'} set in Advanced.`;
  }
  return `Uses your saved settings: ${parts.join(', ')}, and ${formatCount(advanced)} more set in Advanced.`;
}

function listOf(parts: readonly string[]): string {
  return parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}` : (parts[0] ?? '');
}
