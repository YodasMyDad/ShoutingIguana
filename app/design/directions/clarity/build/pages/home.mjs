// Home: the URL field, the crawl running now, and the library grouped by site.
import { page, kbd, favicon } from '../lib/shell.mjs';
import { icon, sevIcon, severityPlural } from '../lib/icons.mjs';
import { esc, num, duration, relative, dateLong, dateShort } from '../lib/format.mjs';
import { sparkline } from '../lib/charts.mjs';
import { library, ILLUSTRATIVE, NOW } from '../lib/data.mjs';

export function sevCounts(issues, { words = true } = {}) {
  return `<span class="sevcounts" aria-label="Checks with issues: ${issues.error} error, ${issues.warning} warning, ${issues.notice} notice">${['error', 'warning', 'notice'].map(s => {
    const n = issues[s];
    return `<span class="sevcount${n ? '' : ' is-zero'}">${sevIcon(s)}<span class="num">${num(n)}</span>${words ? `<span class="sevcount__word">${severityPlural[s][n === 1 ? 0 : 1]}</span>` : ''}</span>`;
  }).join('')}</span>`;
}

function runningCard() {
  const r = ILLUSTRATIVE.running;
  const fig = (label, value) => `<div><div class="running__figure-label">${label}</div><div class="running__figure-value">${value}</div></div>`;
  return `
    <article class="card running" aria-label="Crawling ${r.site}">
      <div class="running__who">
        ${favicon(r.site, 'lg')}
        <div style="min-width:0">
          <div class="running__site">${r.site}</div>
          <div class="running__sub"><span class="status-pill status-pill--running" style="margin:0"><span class="live-dot" aria-hidden="true"></span>Running</span><span>Started ${relative(r.startedAt, NOW)}, ${duration(r.elapsedMs)} so far</span><span>${r.preset} speed</span></div>
        </div>
      </div>
      <div class="running__actions">
        <button class="btn btn--secondary" type="button" title="Pause (F7)">${icon('pause', { size: 'sm' })}Pause</button>
        <button class="btn btn--secondary" type="button" title="Stop (F8)">${icon('stop', { size: 'sm' })}Stop</button>
        <button class="btn btn--primary" type="button">Open</button>
      </div>
      <div class="running__figures">
        ${fig('Crawled', num(r.fetched))}
        ${fig('Found', num(r.discovered))}
        ${fig('Queued', num(r.queued))}
        ${fig('Pages', num(r.pages))}
        <div class="running__rate">
          <div><div class="running__figure-label">URLs a second</div><div class="running__figure-value">${r.perSecond.toFixed(1)}</div></div>
          ${sparkline(r.rate, { label: `URLs a second over ${duration(r.elapsedMs)}`, height: 'var(--spark-height-lg)' })}
        </div>
      </div>
      <div class="running__stack">
        <div class="stack" role="img" aria-label="Of ${num(r.discovered)} URLs found: ${num(r.fetched)} crawled, ${num(r.queued)} queued, ${num(r.skipped)} not followed">
          <span class="stack__crawled" style="--n:${r.fetched}"></span><span class="stack__queued" style="--n:${r.queued}"></span><span class="stack__skipped" style="--n:${r.skipped}"></span>
        </div>
        <div class="legend">
          <span class="legend__item"><span class="legend__swatch stack__crawled"></span>Crawled <span class="legend__value">${num(r.fetched)}</span></span>
          <span class="legend__item"><span class="legend__swatch stack__queued"></span>Queued <span class="legend__value">${num(r.queued)}</span></span>
          <span class="legend__item"><span class="legend__swatch stack__skipped"></span>Not followed (nofollow) <span class="legend__value">${num(r.skipped)}</span></span>
          <span class="legend__item legend__item--end">No URL limit set, so no estimate of the end</span>
        </div>
      </div>
    </article>`;
}

function statusCell(s) {
  if (s.missing) return `<span class="crawl-status">${icon('file-x', { size: 'sm' })}File not found</span><span class="crawl-status__note"><a class="link" href="#">Locate the file…</a></span>`;
  if (s.status === 'stopped') return `<span class="crawl-status">${icon('stop', { size: 'sm' })}Stopped</span><span class="crawl-status__note">Partial analysis. <a class="link" href="#">Continue</a></span>`;
  if (s.outOfDate) return `<span class="crawl-status">${icon('check', { size: 'sm' })}Done</span><span class="crawl-status__note">Out of date. <a class="link" href="#">Analyse again</a></span>`;
  return `<span class="crawl-status">${icon('check', { size: 'sm' })}Done</span>`;
}

function libraryRows(sites) {
  return sites.map(s => {
    const older = s.older.length ? `<span class="crawl-status__note"><a class="link" href="#">${s.older.length} older ${s.older.length === 1 ? 'crawl' : 'crawls'}</a></span>` : '';
    const tag = s.runningNow ? `<span class="badge badge--accent"><span class="live-dot" aria-hidden="true"></span>Crawling now</span>` : '';
    const partial = '';
    return `<tr${s.missing ? ' class="row-missing"' : ''}>
      <td>
        <div class="site-cell">${favicon(s.site, 'md')}
          <div class="site-cell__text">
            <span class="site-cell__name">${esc(s.site)}${tag}</span>
            <span class="site-cell__url">${esc(s.startUrl)}</span>
          </div>
        </div>
      </td>
      <td>${statusCell(s)}</td>
      <td class="num"><span class="cell-2l"><span>${num(s.pageCount)}</span><span class="cell-2l__sub">${num(s.urlCount)} URLs</span></span></td>
      <td>${sevCounts(s.issues)}${partial}</td>
      <td title="${dateLong(s.startedAt)}"><span class="text-2">${relative(s.startedAt, NOW)}</span>${older}</td>
      <td><div class="row-actions"><button class="icon-btn icon-btn--sm" type="button" aria-label="Actions for ${esc(s.site)}">${icon('more', { size: 'sm' })}</button></div></td>
    </tr>`;
  }).join('');
}

export function homePage() {
  const lib = library();
  const main = `
    <div class="page page--narrow">
      <section class="launch" aria-labelledby="launch-title">
        <h1 class="launch__title" id="launch-title">Crawl a site</h1>
        <div class="launch__row">
          <div class="launch-field">
            ${icon('globe', { size: 'lg', cls: 'launch-field__icon' })}
            <input class="launch-field__input" type="url" aria-labelledby="launch-title" placeholder="Paste a URL, then press Enter" autofocus>
            <button class="btn btn--primary btn--lg" type="button">Crawl${kbd('Enter')}</button>
          </div>
          <button class="btn btn--secondary btn--lg" type="button">${icon('sliders', { size: 'sm' })}Settings…</button>
        </div>
        <div class="launch__meta">
          <span class="launch__meta-item">${icon('activity', { size: 'sm' })}Your defaults: Standard speed, 5 requests at a time</span>
          <span class="launch__meta-item">${icon('robots', { size: 'sm' })}Follows robots.txt and reads sitemaps</span>
          <span class="launch__meta-item">${icon('folder-open', { size: 'sm' })}Open a crawl file with ${kbd('Ctrl', 'O')} or drop it here</span>
        </div>
      </section>

      <section class="home-section" aria-labelledby="running-title">
        <h2 class="section-title" id="running-title">Crawling now</h2>
        ${runningCard()}
      </section>

      <section class="home-section" aria-labelledby="library-title">
        <div class="card library">
          <div class="library__toolbar">
            <h2 class="card__title" id="library-title">Library</h2>
            <span class="card__meta">${lib.siteCount} sites, ${lib.crawlCount} crawls</span>
            <label class="field library__search">${icon('search', { size: 'sm' })}<input class="field__input" type="search" placeholder="Search the library" aria-label="Search the library"></label>
            <button class="btn btn--ghost btn--sm" type="button">${icon('sort', { size: 'sm' })}Newest first${icon('chevron-down', { size: 'sm' })}</button>
          </div>
          <table class="grid" aria-label="Crawls, grouped by site">
            <colgroup><col style="width:27%"><col style="width:17%"><col style="width:10%"><col style="width:32%"><col style="width:14%"><col style="width:48px"></colgroup>
            <thead><tr>
              <th scope="col">Site</th><th scope="col">Latest crawl</th><th scope="col" class="num">Pages</th><th scope="col">Checks with issues</th><th scope="col">Crawled</th><th scope="col"><span class="sr-only">Actions</span></th>
            </tr></thead>
            <tbody>${libraryRows(lib.sites)}</tbody>
          </table>
        </div>
      </section>
    </div>`;

  return page({
    title: 'Home, Shouting Iguana', file: 'home.html', section: 'home', crawlState: null,
    crumbs: [{ label: 'Home' }],
    main,
  });
}
