// The shell every screen shares, in both modes: the document head, the sidebar, and the top bar with the switch.
import { sprite, icon } from './icons.mjs';
import { esc, num } from './ui.mjs';

const FONTS = 'https://fonts.googleapis.com/css2?family=Inter:opsz,wght@14..32,400..700&family=JetBrains+Mono:wght@400..600&display=swap';

export const favicon = (site, size = 'md') => {
  const letter = site.replace(/^www\./, '').match(/[a-z0-9]/i)?.[0].toUpperCase() ?? '·';
  return `<span class="favicon favicon--${size}" aria-hidden="true">${letter}</span>`;
};
export const kbd = (...keys) => `<span class="kbd-group">${keys.map(k => `<kbd class="kbd">${k}</kbd>`).join('')}</span>`;

// The one switch. The same control, in the same place, in both modes and on every screen.
export const modeSwitch = mode => `
    <div class="segmented mode-switch" role="radiogroup" aria-label="Mode" title="Switch between Simple and Advanced (Ctrl+Shift+A)">
      <button class="segmented__item" type="button" role="radio" aria-checked="${mode === 'simple'}">Simple</button>
      <button class="segmented__item" type="button" role="radio" aria-checked="${mode === 'advanced'}">Advanced</button>
    </div>`;

// The open crawl's sections. Simple has three; Advanced keeps its seven and gains Report as the eighth.
const SECTIONS = {
  simple: [['summary', 'Summary', 'gauge'], ['fix', 'What to fix', 'list-checks'], ['report', 'Report', 'file-text']],
  advanced: [['overview', 'Overview', 'overview'], ['issues', 'Issues', 'issues'], ['urls', 'URLs', 'urls'], ['links', 'Links', 'links'],
    ['sitemaps', 'Sitemaps', 'sitemaps'], ['robots', 'robots.txt', 'robots'], ['crawl-settings', 'Settings', 'sliders'], ['report', 'Report', 'file-text']],
};

function sidebar({ mode, section, crawl, running }) {
  const simple = mode === 'simple';
  const item = ([id, label, ic], i) => {
    const current = id === section;
    const count = crawl?.counts?.[id];
    const sub = !simple && id === 'issues' && crawl?.sevCounts ? `
        <div class="nav nav__sub">${crawl.sevCounts.map(([sev, word, n]) => `<a class="nav__item" href="#"><svg class="icon icon--sm sev-icon" aria-hidden="true"><use href="#i-sev-${sev}"/></svg><span class="nav__label">${word}</span><span class="nav__count">${n}</span></a>`).join('')}</div>` : '';
    const divider = !simple && id === 'report' ? '<div class="nav__divider" role="separator"></div>' : '';
    return `${divider}<a class="nav__item${current ? ' is-current' : ''}" href="#"${current ? ' aria-current="page"' : ''} title="${label} (Ctrl+${i + 1})" aria-label="${label}">
        ${icon(ic, { size: 'md', cls: 'nav__icon' })}<span class="nav__label">${label}</span>${count != null ? `<span class="nav__count">${num(count)}</span>` : ''}</a>${sub}`;
  };
  const crawlBlock = crawl ? `
    <section class="nav-crawl" aria-label="${simple ? 'Open site' : 'Open crawl'}">
      <a class="nav-crawl__site" href="#" title="${esc(crawl.site)}">
        ${favicon(crawl.site, 'md')}
        <span class="nav-crawl__text"><span class="nav-crawl__name">${esc(crawl.site)}</span><span class="nav-crawl__meta">${esc(crawl.meta)}</span></span>
      </a>
      <nav class="nav nav--crawl" aria-label="${simple ? 'This site' : 'This crawl'}">
        ${SECTIONS[mode].map(item).join('')}
      </nav>
    </section>` : '';
  const runningBlock = running ? `
    <section class="nav-running" aria-label="${simple ? 'Checking now' : 'Crawling now'}">
      <h2 class="nav-heading">${simple ? 'Checking now' : 'Crawling now'}</h2>
      <a class="run-item${running.current ? ' is-current' : ''}" href="#" title="${esc(running.site)}">
        <span class="run-item__top">${favicon(running.site, 'sm')}<span class="run-item__name">${esc(running.site)}</span><span class="live-dot" aria-hidden="true"></span></span>
        <span class="run-item__bar" aria-hidden="true"><span style="--value:${(running.ratio * 100).toFixed(1)}%"></span></span>
        <span class="run-item__meta">${num(running.checked)} ${simple ? 'addresses checked' : 'URLs crawled'}</span>
      </a>
    </section>` : '';
  return `
  <aside class="sidebar" aria-label="Navigation">
    <div class="sidebar__brand">
      <img class="brand__logo" src="../../src/assets/iguana-64.png" alt="" width="28" height="28">
      <span class="brand__name">Shouting Iguana</span>
      <button class="icon-btn icon-btn--sm brand__collapse" type="button" aria-label="Collapse the sidebar" title="Collapse the sidebar">${icon('panel-left', { size: 'sm' })}</button>
    </div>
    <nav class="nav" aria-label="App">
      <a class="nav__item${section === 'home' ? ' is-current' : ''}" href="home.html"${section === 'home' ? ' aria-current="page"' : ''} title="Home" aria-label="Home">${icon('home', { cls: 'nav__icon' })}<span class="nav__label">Home</span></a>
    </nav>${crawlBlock}
    <div class="sidebar__spacer"></div>${runningBlock}
    <div class="sidebar__footer">
      <a class="nav__item${section === 'settings' ? ' is-current' : ''}" href="#" title="Settings (Ctrl+,)" aria-label="Settings"${section === 'settings' ? ' aria-current="page"' : ''}>${icon('settings', { cls: 'nav__icon' })}<span class="nav__label">Settings</span></a>
    </div>
  </aside>`;
}

// state: null | { kind: 'running' | 'done' | 'stopped', count }
function topbar({ mode, crumbs, state }) {
  const simple = mode === 'simple';
  let pill = '', controls = '';
  if (state?.kind === 'running') {
    pill = `<span class="status-pill status-pill--running" role="status" aria-live="polite"><span class="live-dot" aria-hidden="true"></span>${simple ? 'Checking' : 'Running'}<span class="status-pill__count">${num(state.count)} ${simple ? 'checked' : 'URLs'}</span></span>`;
    controls = `
      <button class="btn btn--secondary btn--bar" type="button" title="Pause (F7)" aria-label="Pause">${icon('pause', { size: 'sm' })}<span class="btn__label">Pause</span><kbd class="kbd kbd--inline">F7</kbd></button>
      <button class="btn btn--secondary btn--bar" type="button" title="Stop (F8)" aria-label="Stop">${icon('stop', { size: 'sm' })}<span class="btn__label">Stop</span><kbd class="kbd kbd--inline">F8</kbd></button>`;
  } else if (state?.kind === 'done') {
    pill = `<span class="status-pill status-pill--done" role="status">${icon('check', { size: 'sm' })}Done</span>`;
  } else if (state?.kind === 'stopped') {
    pill = `<span class="status-pill status-pill--stopped" role="status">${icon('stop', { size: 'sm' })}${simple ? 'Stopped early' : 'Stopped'}</span>`;
    controls = `<button class="btn btn--secondary btn--bar" type="button" aria-label="Continue" title="Continue">${icon('play', { size: 'sm' })}<span class="btn__label">Continue</span></button>`;
  }
  const search = simple
    ? `<button class="search-trigger search-trigger--icon" type="button" aria-keyshortcuts="Control+K" aria-label="Search or run a command" title="Search or run a command (Ctrl+K)">${icon('search', { size: 'sm' })}</button>`
    : `<button class="search-trigger" type="button" aria-keyshortcuts="Control+K" aria-label="Search or run a command" title="Search or run a command (Ctrl+K)">${icon('search', { size: 'sm' })}<span class="search-trigger__label">Search or run …</span>${kbd('Ctrl', 'K')}</button>`;
  const crumb = (c, i) => {
    const last = i === crumbs.length - 1;
    const sep = i ? icon('chevron-right', { size: 'xs', cls: 'crumbs__sep' }) : '';
    const inner = `${c.favicon ? favicon(c.favicon, 'sm') : ''}<span>${esc(c.label)}</span>`;
    const kind = last ? 'current' : c.kind ?? (i === 0 ? 'home' : 'site');
    const link = last
      ? `<span class="crumb is-current" aria-current="page">${inner}</span>`
      : `<a class="crumb${c.favicon ? ' crumb--site' : ''}" href="#">${inner}</a>`;
    // The separator after a crumb belongs to it, so a crumb that gives way takes its separator with it.
    return `<span class="crumbs__part crumbs__part--${kind}">${link}${last ? '' : icon('chevron-right', { size: 'xs', cls: 'crumbs__sep' })}</span>`;
  };
  return `
  <header class="topbar${state?.kind === 'running' || state?.kind === 'stopped' ? ' topbar--live' : ''}">
    <div class="topbar__history">
      <button class="icon-btn" type="button" aria-label="Back" title="Back (Alt+Left)">${icon('chevron-left')}</button>
      <button class="icon-btn" type="button" aria-label="Forward" title="Forward (Alt+Right)" disabled>${icon('chevron-right')}</button>
    </div>
    <nav class="crumbs" aria-label="You are here">
      ${crumbs.map(crumb).join('')}
    </nav>
    ${pill}
    <div class="topbar__spacer"></div>
    <div class="topbar__controls">${controls}</div>
    ${search}
    ${modeSwitch(mode)}
  </header>`;
}

export function page({ mode = 'simple', title, file, section, crawl = null, running = null, crumbs, state = null, main, overlay = '', mainClass = '' }) {
  return `<!doctype html>
<html lang="en-GB" data-density="comfortable" data-mode="${mode}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${esc(title)}</title>
  <meta name="description" content="Shouting Iguana v2, ${mode === 'simple' ? 'Simple' : 'Advanced'}: ${esc(file)}">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="${FONTS}">
  <link rel="stylesheet" href="../directions/clarity/tokens.css">
  <link rel="stylesheet" href="../directions/clarity/app.css">
  <link rel="stylesheet" href="simple.css">
  <script src="../directions/clarity/ui.js" defer></script>
</head>
<body>
${sprite()}
<a class="skip-link" href="#main">Skip to the content</a>
<div class="app">
  ${sidebar({ mode, section, crawl, running })}
  ${topbar({ mode, crumbs, state })}
  <main class="main${mainClass ? ' ' + mainClass : ''}" id="main" tabindex="-1">
${main}
  </main>
</div>
${overlay}
</body>
</html>
`;
}
