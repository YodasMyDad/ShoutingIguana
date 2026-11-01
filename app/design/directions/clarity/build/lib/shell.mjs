// The shell every screen shares: the document head, the sidebar and the top bar.
import { sprite, icon } from './icons.mjs';
import { esc, num, dateTime } from './format.mjs';
import { gardenCrawl, gardenChecks, gardenStats, urls, ILLUSTRATIVE } from './data.mjs';

const FONTS = 'https://fonts.googleapis.com/css2?family=Inter:opsz,wght@14..32,400..700&family=JetBrains+Mono:wght@400..600&display=swap';

export const favicon = (site, size = 'md') => {
  const letter = site.replace(/^www\./, '').match(/[a-z0-9]/i)?.[0].toUpperCase() ?? '·';
  return `<span class="favicon favicon--${size}" aria-hidden="true">${letter}</span>`;
};

export const kbd = (...keys) => `<span class="kbd-group">${keys.map(k => `<kbd class="kbd">${k}</kbd>`).join('')}</span>`;

function sidebar({ section, crawlState }) {
  const g = gardenChecks();
  const running = crawlState === 'running';
  const nav = [
    ['overview', 'Overview', 'overview', null],
    ['issues', 'Issues', 'issues', running ? null : g.withIssues],
    ['urls', 'URLs', 'urls', running ? 2713 : urls.total],
    ['links', 'Links', 'links', running ? null : gardenStats.linkCount],
    ['sitemaps', 'Sitemaps', 'sitemaps', gardenStats.sitemapCount],
    ['robots', 'robots.txt', 'robots', null],
    ['settings', 'Crawl settings', 'sliders', null],
  ];
  const item = ([id, label, ic, count], i) => {
    const current = id === section;
    return `<a class="nav__item${current ? ' is-current' : ''}" href="#"${current ? ' aria-current="page"' : ''} title="Ctrl+${i + 1}">
      ${icon(ic, { size: 'md', cls: 'nav__icon' })}<span class="nav__label">${label}</span>${count != null ? `<span class="nav__count">${num(count)}</span>` : ''}</a>`;
  };
  const bg = running
    ? [{ site: 'gardenfurnituresets.co.uk', fetched: 2713, ratio: 2713 / 5722, current: true }]
    : [{ site: ILLUSTRATIVE.running.site, fetched: ILLUSTRATIVE.running.fetched, ratio: ILLUSTRATIVE.running.fetched / ILLUSTRATIVE.running.discovered }];
  return `
  <aside class="sidebar" aria-label="Navigation">
    <div class="sidebar__brand">
      <img class="brand__logo" src="../../assets/logo.png" alt="" width="28" height="28">
      <span class="brand__name">Shouting Iguana</span>
      <button class="icon-btn icon-btn--sm brand__collapse" type="button" aria-label="Collapse the sidebar" title="Collapse the sidebar">${icon('panel-left', { size: 'sm' })}</button>
    </div>
    <nav class="nav" aria-label="App">
      <a class="nav__item${section === 'home' ? ' is-current' : ''}" href="home.html"${section === 'home' ? ' aria-current="page"' : ''}>${icon('home', { cls: 'nav__icon' })}<span class="nav__label">Home</span></a>
    </nav>
    <section class="nav-crawl" aria-label="Open crawl">
      <a class="nav-crawl__site" href="live.html" title="${esc(gardenCrawl.startUrl)}">
        ${favicon('gardenfurnituresets.co.uk', 'md')}
        <span class="nav-crawl__text"><span class="nav-crawl__name">gardenfurnituresets.co.uk</span><span class="nav-crawl__meta">${dateTime(gardenCrawl.startedAt)}${running ? ', crawling' : ''}</span></span>
      </a>
      <nav class="nav nav--crawl" aria-label="This crawl">
        ${nav.map(item).join('')}
      </nav>
    </section>
    <div class="sidebar__spacer"></div>
    <section class="nav-running" aria-label="Crawls running">
      <h2 class="nav-heading">Crawling now</h2>
      ${bg.map(b => `
      <a class="run-item${b.current ? ' is-current' : ''}" href="${b.current ? 'live.html' : 'home.html'}">
        <span class="run-item__top">${favicon(b.site, 'sm')}<span class="run-item__name">${b.site}</span><span class="live-dot" aria-hidden="true"></span></span>
        <span class="run-item__bar" aria-hidden="true"><span style="--value:${(b.ratio * 100).toFixed(1)}%"></span></span>
        <span class="run-item__meta">${num(b.fetched)} URLs crawled</span>
      </a>`).join('')}
    </section>
    <div class="sidebar__footer">
      <a class="nav__item" href="#" title="Ctrl+,">${icon('settings', { cls: 'nav__icon' })}<span class="nav__label">Settings</span></a>
      <div class="theme-switch" role="radiogroup" aria-label="Theme">
        <button type="button" role="radio" aria-checked="true" data-theme-choice="system" title="Follow the system">${icon('monitor', { size: 'sm' })}<span class="sr-only">System</span></button>
        <button type="button" role="radio" aria-checked="false" data-theme-choice="light" title="Light">${icon('sun', { size: 'sm' })}<span class="sr-only">Light</span></button>
        <button type="button" role="radio" aria-checked="false" data-theme-choice="dark" title="Dark">${icon('moon', { size: 'sm' })}<span class="sr-only">Dark</span></button>
      </div>
    </div>
  </aside>`;
}

function topbar({ crumbs, crawlState, liveCount }) {
  let pill = '', controls = '';
  if (crawlState === 'running') {
    pill = `<span class="status-pill status-pill--running" role="status" aria-live="polite"><span class="live-dot" aria-hidden="true"></span>Running<span class="status-pill__count">${num(liveCount)} URLs</span></span>`;
    controls = `
      <button class="btn btn--secondary" type="button" title="Pause (F7)">${icon('pause', { size: 'sm' })}Pause<kbd class="kbd kbd--inline">F7</kbd></button>
      <button class="btn btn--secondary" type="button" title="Stop (F8)">${icon('stop', { size: 'sm' })}Stop<kbd class="kbd kbd--inline">F8</kbd></button>`;
  } else if (crawlState === 'done') {
    pill = `<span class="status-pill status-pill--done" role="status">${icon('check', { size: 'sm' })}Done</span>`;
    controls = `
      <button class="btn btn--secondary" type="button">${icon('refresh', { size: 'sm' })}Crawl again</button>
      <button class="icon-btn" type="button" aria-label="More crawl actions" title="More crawl actions">${icon('more')}</button>`;
  }
  return `
  <header class="topbar">
    <div class="topbar__history">
      <button class="icon-btn" type="button" aria-label="Back" title="Back (Alt+Left)">${icon('chevron-left')}</button>
      <button class="icon-btn" type="button" aria-label="Forward" title="Forward (Alt+Right)" disabled>${icon('chevron-right')}</button>
    </div>
    <nav class="crumbs" aria-label="You are here">
      ${crumbs.map((c, i) => {
        const last = i === crumbs.length - 1;
        const sep = i ? `${icon('chevron-right', { size: 'xs', cls: 'crumbs__sep' })}` : '';
        const inner = `${c.favicon ? favicon(c.favicon, 'sm') : ''}<span>${esc(c.label)}</span>`;
        return `${sep}${last ? `<span class="crumb is-current" aria-current="page">${inner}</span>` : `<a class="crumb${c.strong ? ' crumb--site' : ''}" href="${c.href ?? '#'}">${inner}</a>`}`;
      }).join('')}
    </nav>
    ${pill}
    <div class="topbar__spacer"></div>
    <div class="topbar__controls">${controls}</div>
    <button class="search-trigger" type="button" aria-keyshortcuts="Control+K">
      ${icon('search', { size: 'sm' })}<span class="search-trigger__label">Search or jump to…</span>${kbd('Ctrl', 'K')}
    </button>
  </header>`;
}

export function page({ title, file, section, crawlState = null, crumbs, liveCount, main, inspector = '', appClass = '', density = 'comfortable', extraHead = '' }) {
  return `<!doctype html>
<html lang="en-GB" data-density="${density}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${esc(title)}</title>
  <meta name="description" content="Shouting Iguana v2, direction B (clarity): ${esc(file)}">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="${FONTS}">
  <link rel="stylesheet" href="tokens.css">
  <link rel="stylesheet" href="app.css">
  <script src="ui.js" defer></script>${extraHead}
</head>
<body>
${sprite()}
<a class="skip-link" href="#main">Skip to the content</a>
<div class="app${inspector ? ' app--inspector' : ''}${appClass ? ' ' + appClass : ''}">
  ${sidebar({ section, crawlState })}
  ${topbar({ crumbs, crawlState, liveCount })}
  <main class="main" id="main" tabindex="-1">
${main}
  </main>
  ${inspector}
</div>
</body>
</html>
`;
}
