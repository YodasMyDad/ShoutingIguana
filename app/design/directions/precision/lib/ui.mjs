// Shared pieces of the five screens: the page frame, the shell (sidebar and top bar) and the small components.
// Each is written once and used everywhere, the way the app's components will be.

import { esc, int, urlParts } from './format.mjs';
import { icon, sprite } from './icons.mjs';

export const LOGO = '../../../../Assets/logo.png';

const themeScript = `<script>
(() => {
  // Theme: follows the system unless the toggle chose light or dark. Storage can be unavailable; the page works without it.
  const root = document.documentElement;
  const modes = ['system', 'light', 'dark'];
  let mode = 'system';
  try { mode = localStorage.getItem('si-precision-theme') || 'system'; } catch (e) {}
  const apply = () => (mode === 'system' ? root.removeAttribute('data-theme') : root.setAttribute('data-theme', mode));
  apply();
  addEventListener('DOMContentLoaded', () => {
    const button = document.querySelector('[data-theme-toggle]');
    if (!button) return;
    const label = () => button.setAttribute('aria-label', 'Theme: ' + mode + '. Change theme');
    label();
    button.addEventListener('click', () => {
      mode = modes[(modes.indexOf(mode) + 1) % modes.length];
      apply();
      label();
      try { localStorage.setItem('si-precision-theme', mode); } catch (e) {}
    });
  });
})();
</script>`;

export function page({ title, body, script = '' }) {
  return `<!doctype html>
<html lang="en-GB">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:opsz,wght@14..32,400..700&family=JetBrains+Mono:wght@400;500&display=swap">
<link rel="stylesheet" href="tokens.css">
<link rel="stylesheet" href="app.css">
${themeScript}
</head>
<body>
${sprite()}
${body}
${script}
</body>
</html>
`;
}

// Keys, from one registry in the app; here, just their names.
export const kbd = (...keys) =>
  `<span class="kbd-group">${keys.map((k) => `<kbd class="kbd">${k === 'Enter' ? 'Enter' : esc(k)}</kbd>`).join('')}</span>`;

export const sev = (severity, label = true) =>
  `<svg class="sev sev--${severity}"${label ? ` role="img" aria-label="${severityWord[severity]}"` : ' aria-hidden="true"'}><use href="#i-sev-${severity}"/></svg>`;

export const severityWord = { error: 'Error', warning: 'Warning', notice: 'Notice', skipped: 'Skipped' };

export function http(code) {
  if (code == null) return '<span class="http http--none">None</span>';
  const cls = `${String(code)[0]}xx`;
  return `<span class="http http--${cls}">${code}</span>`;
}

const statusGlyph = {
  running: '<span class="live-dot" aria-hidden="true"></span>',
  paused: icon('pause', 'icon--xs'),
  stopped: icon('stop', 'icon--xs'),
  completed: icon('check', 'icon--xs'),
  analysing: icon('refresh', 'icon--xs'),
  interrupted: icon('power', 'icon--xs'),
};
const statusWord = { running: 'Running', paused: 'Paused', stopped: 'Stopped', completed: 'Done', analysing: 'Analysing', interrupted: 'Interrupted' };

export const crawlStatus = (status, figure = '', live = false) =>
  `<span class="crawl-status crawl-status--${status}"${live ? ' role="status"' : ''}>${statusGlyph[status]}${statusWord[status]}${figure ? `<span class="num">${figure}</span>` : ''}</span>`;

// The favicon slot. Favicons aren't fetched yet (engine work), so the fallback is the site's initial on a quiet tile.
export function fav(site, large = false) {
  const name = site.replace(/^www\./, '');
  const letter = /^\d/.test(name) ? '#' : name[0];
  return `<span class="fav${large ? ' fav--l' : ''}" aria-hidden="true">${esc(letter)}</span>`;
}

export function url(u, budget = Infinity) {
  const { host, rest, full } = urlParts(u, budget);
  return `<span class="url" title="${esc(full)}"><span class="url-host">${esc(host)}</span><span class="url-rest">${esc(rest)}</span></span>`;
}

// A value on its scale: the acceptable band, ticks, and the needle. Positions are data, so they're inline.
export function gauge({ min, max, lo = null, hi = null, value, ticks = [], tone = null, labels = [], aria }) {
  const pct = (v) => `${(((Math.min(Math.max(v, min), max) - min) / (max - min)) * 100).toFixed(2)}%`;
  const bandLo = lo ?? min;
  const bandHi = hi ?? max;
  const band = lo != null || hi != null ? `<span class="gauge-band" style="left:${pct(bandLo)};width:calc(${pct(bandHi)} - ${pct(bandLo)})"></span>` : '';
  const tickMarks = ticks.map((t) => `<span class="gauge-tick" style="left:${pct(t)}"></span>`).join('');
  const labelMarks = labels
    .map(([v, text, edge]) => `<span class="gauge-label${edge ? ` gauge-label--${edge}` : ''}" style="left:${pct(v)}">${text}</span>`)
    .join('');
  return `<div class="gauge" role="img" aria-label="${esc(aria)}"><span class="gauge-rail"></span>${band}${tickMarks}<span class="gauge-needle${tone ? ` gauge-needle--${tone}` : ''}" style="left:${pct(value)}"></span>${labelMarks}</div>`;
}

export const segbar = (parts, cls = '') =>
  `<div class="segbar ${cls}" role="img" aria-label="${esc(parts.map((p) => `${p.label} ${int(p.value)}`).join(', '))}">${parts
    .filter((p) => p.value > 0)
    .map((p) => `<span class="seg-${p.key}" style="flex:${p.value} 1 0"></span>`)
    .join('')}</div>`;

export const button = ({ label = '', iconName, trailing, kind = '', keys, size = '', attrs = '', extraClass = '' }) => {
  const cls = ['btn', kind && `btn--${kind}`, size && `btn--${size}`, !label && 'btn--icon', extraClass].filter(Boolean).join(' ');
  return `<button type="button" class="${cls}"${attrs}>${iconName ? icon(iconName) : ''}${label ? esc(label) : ''}${trailing ? icon(trailing, 'icon icon--trailing') : ''}${keys && keys.length ? kbd(...keys) : ''}</button>`;
};

export const iconButton = (iconName, label, { size = '', keys, extraClass = '', attrs = '' } = {}) =>
  `<button type="button" class="btn btn--ghost btn--icon${size ? ` btn--${size}` : ''}${extraClass ? ` ${extraClass}` : ''}" aria-label="${esc(label)}${keys ? ` (${keys.join('+')})` : ''}"${attrs}>${icon(iconName)}</button>`;

export const tooltip = (text, keys, style) =>
  `<div class="tooltip" role="tooltip" style="${style}">${esc(text)}${keys ? kbd(...keys) : ''}</div>`;

const sections = [
  ['overview', 'Overview', 'overview'],
  ['issues', 'Issues', 'issues'],
  ['urls', 'URLs', 'urls'],
  ['links', 'Links', 'links'],
  ['sitemaps', 'Sitemaps', 'sitemaps'],
  ['robots', 'robots.txt', 'robots'],
  ['settings', 'Settings', 'sliders'],
];

// The sidebar: Home; the open crawl's sections with their counts; crawls running in the background; app settings.
export function sidebar({ current, crawl = null, counts = {}, liveSection = null, background = [], hint = null }) {
  const item = (id, label, iconName, count, i) => {
    const isCurrent = current === id;
    const countHtml = count != null && count !== '' ? `<span class="sb-count">${count}</span>` : '';
    const keys = i != null ? kbd('Ctrl', String(i + 1)) : '';
    const liveMark = liveSection === id ? '<span class="live-dot" aria-label="Live" role="img"></span>' : '';
    return `<a href="#" class="sb-item${isCurrent ? ' is-current' : ''}${hint === id ? ' is-hint' : ''}"${isCurrent ? ' aria-current="page"' : ''}>${icon(iconName)}<span class="sb-label">${label}</span>${liveMark}${countHtml}${keys}</a>`;
  };
  const crawlBlock = crawl
    ? `<div class="sb-rule" role="presentation"></div>
      <div class="sb-crawl">${fav(crawl.site, true)}<div class="sb-crawl-text"><span class="sb-crawl-site" title="${esc(crawl.startUrl)}">${esc(crawl.site)}</span><span class="sb-crawl-when">${esc(crawl.when)}</span></div></div>
      <nav aria-label="This crawl">${sections.map(([id, label, ic], i) => item(id, label, ic, counts[id], i)).join('')}</nav>`
    : '';
  const bg = background.length
    ? `<div class="sb-section">Running in the background</div>
      ${background
        .map((b) => `<a href="#" class="sb-live">
          <span class="sb-live-top"><span class="live-dot" aria-hidden="true"></span><span class="sb-live-site">${esc(b.site)}</span></span>
          ${segbar([{ key: 'crawled', label: 'Crawled', value: b.crawled }, { key: 'queued', label: 'Queued', value: b.queued }], 'segbar--thin')}
          <span class="sb-live-meta"><span>${int(b.crawled)} crawled</span><span>${b.rate} URLs/s</span></span>
        </a>`)
        .join('')}`
    : '';
  return `<aside class="sidebar" aria-label="Sidebar">
    <div class="sb-head">
      <img class="sb-logo" src="${LOGO}" alt="" width="20" height="20">
      <span class="sb-name">Shouting Iguana</span>
      ${iconButton('sidebar', 'Collapse the sidebar', { size: 's' })}
    </div>
    <div class="sb-body">
      <nav aria-label="App">${item('home', 'Home', 'home')}</nav>
      ${crawlBlock}
      <div class="spacer"></div>
      ${bg}
    </div>
    <div class="sb-foot">
      <a href="#" class="sb-item">${icon('gear')}<span class="sb-label">Settings</span>${kbd('Ctrl', ',')}</a>
      <button type="button" class="sb-item" data-theme-toggle aria-label="Change theme">${icon('theme')}<span class="sb-label">Theme</span><span class="sb-count">System</span></button>
    </div>
  </aside>`;
}

export function topbar({ crumbs, status = '', actions = '', extra = '' }) {
  return `<header class="topbar">
    <div class="tb-nav">
      ${iconButton('back', 'Back', { keys: ['Alt', 'Left'] })}
      ${iconButton('forward', 'Forward', { keys: ['Alt', 'Right'] })}
    </div>
    <nav class="crumbs" aria-label="Where you are">${crumbs}</nav>
    ${status}
    <div class="tb-gap"></div>
    <div class="tb-actions">${actions}
      <button type="button" class="cmdk" aria-label="Search or run a command (Ctrl+K)">${icon('search')}<span class="cmdk-label">Search or jump to…</span>${kbd('Ctrl', 'K')}</button>
      ${extra}
    </div>
  </header>`;
}

export const crumbsFor = (site, when, here) =>
  `<a href="#" class="crumb crumb--site" title="${esc(site)}">${fav(site)}<span class="crumb-text">${esc(site)}</span></a><span class="crumb-sep" aria-hidden="true">/</span><a href="#" class="crumb">${esc(when)}</a><span class="crumb-sep" aria-hidden="true">/</span><span class="crumb crumb--here" aria-current="page">${esc(here)}</span>`;
