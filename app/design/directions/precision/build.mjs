// Builds direction A's mockups from the real fixtures: tokens.css from lib/tokens.mjs, then the six pages.
//   node app/design/directions/precision/build.mjs
// Needs the fixtures (app/design/data/extract/run-all.mjs). The pages it writes are static: no build to view them.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { tokensCss } from './lib/tokens.mjs';
import { page, sidebar, topbar, crumbsFor, kbd, sev, http, crawlStatus, fav, url, gauge, segbar, button, iconButton, tooltip, severityWord } from './lib/ui.mjs';
import { icon } from './lib/icons.mjs';
import { int, decimal, percent, bytes, ms, duration, clock, crawlName, exact, relative, esc, urlParts, pathOf } from './lib/format.mjs';
import * as data from './lib/data.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const out = (name, html) => writeFileSync(join(here, name), html);

const { garden, illustrative } = data;
const gardenCrawl = garden.crawl;
const gardenWhen = crawlName(gardenCrawl.startedAt);
const checksSummary = data.checksList(garden.analysis);
const gardenCounts = {
  issues: int(checksSummary.withIssues),
  urls: int(gardenCrawl.urlCount),
  links: int(garden.stats.linkCount),
  sitemaps: int(garden.stats.sitemapCount),
};
const running = illustrative.running;
const background = [{ site: running.site, crawled: running.crawled, queued: running.queued, rate: decimal(running.rate) }];
const sevKeys = ['error', 'warning', 'notice'];

const app = ({ side, top, main, after = '' }) => `<div class="app">${side}${top}${main}</div>${after}`;

// ---------------------------------------------------------------------------------------------------- home

function home() {
  const now = illustrative.now;
  const presets = Object.fromEntries(data.speedPresets.map((p) => [p.id, p]));
  const standard = presets.standard;

  const libraryRows = [
    {
      site: data.synthetic.site, startUrl: data.synthetic.startUrl, status: data.synthetic.status, startedAt: data.synthetic.startedAt,
      durationMs: new Date(data.synthetic.finishedAt) - new Date(data.synthetic.startedAt), urlCount: data.synthetic.urlCount,
      pageCount: data.synthetic.pageCount, issues: data.synthetic.issues, older: 0, real: true,
    },
    { ...illustrative.crawls[0] },
    {
      site: gardenCrawl.site, startUrl: gardenCrawl.startUrl, status: gardenCrawl.status, startedAt: gardenCrawl.startedAt,
      durationMs: new Date(gardenCrawl.finishedAt) - new Date(gardenCrawl.startedAt), urlCount: gardenCrawl.urlCount,
      pageCount: gardenCrawl.pageCount, issues: gardenCrawl.issues, older: 0, real: true, hover: true,
    },
    ...illustrative.crawls.slice(1),
  ];
  const crawlCount = libraryRows.reduce((n, r) => n + 1 + r.older, 0);

  // Each row is a site with its latest crawl; older crawls of the site fold under it.
  const action = (r) => {
    if (r.hover) return `${button({ label: 'Open', size: 's', keys: ['Enter'] })}${iconButton('more', 'More actions for this crawl', { size: 's' })}`;
    if (r.status === 'stopped') return button({ label: 'Continue', iconName: 'play', size: 's' });
    if (r.status === 'interrupted') return button({ label: 'Resume', iconName: 'play', size: 's' });
    if (r.outOfDate) return button({ label: 'Analyse again', iconName: 'refresh', size: 's' });
    return '';
  };
  const issuesCells = (r) => {
    if (!r.issues) return `<span class="cell lib-note" role="cell" style="grid-column: span 3">Not analysed yet</span>`;
    return sevKeys
      .map((k) => `<span class="cell" role="cell"><span class="sev-count${r.issues[k] === 0 ? ' is-zero' : ''}">${sev(k)}${int(r.issues[k])}</span></span>`)
      .join('');
  };
  const when = (r) => {
    const today = r.startedAt.slice(0, 10) === now.slice(0, 10);
    return today ? `Today, ${clock(r.startedAt)}` : crawlName(r.startedAt);
  };
  // The status's second line says what the status means for you: how long it took, or why it needs an action.
  const took = (r) => {
    if (r.status === 'stopped') return 'Analysis is partial';
    if (r.status === 'interrupted') return 'The app closed mid-crawl';
    if (r.outOfDate) return 'Analysed with older limits';
    return `In ${duration(r.durationMs)}`;
  };
  const row = (r) => `<div class="lib-row${r.hover ? ' is-hover' : ''}" role="row">
      <span class="cell lib-site" role="cell">${fav(r.site, true)}<span class="lib-two">
        <span class="lib-line"><span class="lib-site-name">${esc(r.site)}</span></span>
        <span class="lib-line"><span class="lib-sub mono">${esc(r.startUrl ?? `https://www.${r.site}/`)}</span>${r.older ? `<a href="#" class="lib-older">${icon('chev-right', 'icon--xs')}${r.older} older</a>` : ''}</span></span></span>
      <span class="cell lib-two" role="cell"><span class="strong" title="${esc(exact(r.startedAt))}">${relative(r.startedAt, now)}</span><span class="lib-sub">${when(r)}</span></span>
      <span class="cell lib-two lib-status" role="cell">${crawlStatus(r.status)}<span class="lib-sub">${took(r)}</span></span>
      <span class="cell cell--num strong" role="cell">${int(r.urlCount)}</span>
      <span class="cell cell--num" role="cell">${int(r.pageCount)}</span>
      ${issuesCells(r)}
      <span class="cell lib-actions" role="cell">${action(r)}</span>
    </div>`;

  const elapsed = duration(running.elapsedMs);
  const found = running.crawled + running.queued + running.notFollowed;
  const main = `<main class="main home" id="main">
    <div class="home-inner">
      <section class="launch" aria-labelledby="launch-title">
        <h1 class="visually-hidden" id="launch-title">New crawl</h1>
        <div class="launch-row">
          <label class="field launch-field is-focused">
            <span class="visually-hidden">URL to crawl</span>
            ${icon('links')}
            <input type="url" placeholder="Paste a URL to crawl" autofocus spellcheck="false">
            ${button({ label: 'Crawl', kind: 'primary', keys: ['Enter'] })}
          </label>
          ${button({ label: 'Settings…', size: 'l', iconName: 'sliders' })}
        </div>
        <p class="launch-hint">
          <span class="hint"><span>Crawls with your defaults: <b>Standard</b> speed, ${standard.concurrency} at a time, no rate cap</span></span>
          <span class="hint">${kbd('Ctrl', 'O')}<span>Open a .sicrawl file, or drop one anywhere</span></span>
        </p>
      </section>

      <section aria-labelledby="running-title">
        <div class="section-head"><h2 class="section-title" id="running-title">Running now</h2><span class="section-meta">1 crawl</span></div>
        <div class="running">
          <div class="running-id">
            ${fav(running.site, true)}
            <span class="running-site">${esc(running.site)}</span>
            ${crawlStatus('running')}
            <span class="muted">Started ${clock(running.startedAt)}, Standard speed</span>
          </div>
          <div class="lib-actions">
            ${button({ label: 'Pause', iconName: 'pause', size: 's' })}
            ${button({ label: 'Stop', iconName: 'stop', size: 's' })}
            ${button({ label: 'Open', size: 's', keys: [] })}
          </div>
          <div class="running-figures">
            <div class="figure"><span class="figure-label"><span class="swatch swatch--crawled"></span>Crawled</span><span class="figure-value">${int(running.crawled)}</span></div>
            <div class="figure"><span class="figure-label"><span class="swatch swatch--queued"></span>Queued</span><span class="figure-value">${int(running.queued)}</span></div>
            <div class="figure"><span class="figure-label"><span class="swatch swatch--skipped"></span>Not followed</span><span class="figure-value">${int(running.notFollowed)}</span></div>
            <div class="figure"><span class="figure-label">Found</span><span class="figure-value">${int(found)}</span></div>
            <div class="spacer"></div>
            <div class="figure"><span class="figure-label">Rate</span><span class="figure-value">${decimal(running.rate)}<small>URLs/s</small></span></div>
            <div class="figure"><span class="figure-label">Elapsed</span><span class="figure-value">${elapsed}</span></div>
          </div>
          <div class="running-bar">${segbar([
            { key: 'crawled', label: 'Crawled', value: running.crawled },
            { key: 'queued', label: 'Queued', value: running.queued },
            { key: 'skipped', label: 'Not followed', value: running.notFollowed },
          ])}</div>
        </div>
      </section>

      <section class="library" aria-labelledby="library-title">
        <div class="section-head">
          <h2 class="section-title" id="library-title">Library</h2>
          <span class="section-meta">${libraryRows.length} sites, ${crawlCount} crawls</span>
          <div class="spacer"></div>
          <label class="field field--quiet" style="width:var(--size-search)">${icon('filter')}<span class="visually-hidden">Filter crawls</span><input placeholder="Filter by site"></label>
        </div>
        <div class="lib-table" role="table" aria-label="Crawls by site, newest first" style="--cols: minmax(0, 1fr) 104px 176px 72px 72px 44px 44px 44px 136px">
          <div class="lib-head" role="row">
            <span class="cell" role="columnheader">Site</span>
            <span class="cell" role="columnheader">Last crawl</span>
            <span class="cell" role="columnheader">Status</span>
            <span class="cell is-num" role="columnheader">URLs</span>
            <span class="cell is-num" role="columnheader">Pages</span>
            <span class="cell lib-checks-head" role="columnheader" style="grid-column: span 3">Checks with issues</span>
            <span class="cell" role="columnheader"><span class="visually-hidden">Actions</span></span>
          </div>
          ${libraryRows.map(row).join('')}
        </div>
        <p class="lib-foot">${icon('folder', 'icon--s')}Crawls are kept as .sicrawl files in your data folder. Removing one from the library can be undone.</p>
      </section>
    </div>
  </main>`;

  return page({
    title: 'Home · Shouting Iguana',
    body: app({
      side: sidebar({ current: 'home', background }),
      top: topbar({ crumbs: `<span class="crumb crumb--here" aria-current="page">${icon('home')}Home</span>` }),
      main,
    }),
  });
}

// ---------------------------------------------------------------------------------------------------- live

function live() {
  const L = data.live();
  const s = L.now;
  const queued = s.queued + s.inFlight;
  const toCrawl = s.fetched + queued;
  const found = s.discovered;
  const cap = L.options.maxRequestsPerSecond;

  // Scale for the segmented bar: 0 to found, ticks every 1,000.
  const ticks = [];
  for (let v = 0; v <= found; v += 1000) ticks.push(v);
  const tickHtml = ticks.map((v) => `<span class="scale-tick" style="left:${((v / found) * 100).toFixed(2)}%"><span>${int(v)}</span></span>`).join('');

  // Rate sparkline: 10 s buckets of URLs per second since the start, with the Polite cap drawn as a threshold.
  const rateMax = 4;
  const W = 1000;
  const Hs = 100;
  const T = s.elapsedMs / 1000;
  const sx = (t) => ((t / T) * W).toFixed(1);
  const sy = (v) => (Hs - (v / rateMax) * Hs).toFixed(1);
  const ratePath = L.rate.map((p, i) => `${i ? 'L' : 'M'}${sx(p.t)} ${sy(p.v)}`).join('');
  const lastRate = L.rate[L.rate.length - 1];

  // Stacked area over time: crawled, then queued, then not followed; the top edge is "found".
  const maxY = 6000;
  const Hc = 196;
  const cy = (v) => (Hc - (v / maxY) * Hc).toFixed(1);
  const series = L.series;
  const band = (lowFn, highFn) => {
    const top = series.map((p, i) => `${i ? 'L' : 'M'}${sx(p.t)} ${cy(highFn(p))}`).join('');
    const bottom = [...series].reverse().map((p) => `L${sx(p.t)} ${cy(lowFn(p))}`).join('');
    return `${top}${bottom}Z`;
  };
  const line = (fn) => series.map((p, i) => `${i ? 'L' : 'M'}${sx(p.t)} ${cy(fn(p))}`).join('');
  const crawledArea = band(() => 0, (p) => p.crawled);
  const queuedArea = band((p) => p.crawled, (p) => p.crawled + p.queued);
  const skippedArea = band((p) => p.crawled + p.queued, (p) => p.found);
  const yTicks = [0, 2000, 4000, 6000];
  const xTicks = [0, 300, 600, 900, 1200];
  const quietFor = duration(s.elapsedMs - L.lastGrowthMs);

  // The hover layer, shown at one moment: a crosshair, a ringed dot on each edge, and the values there.
  const at = series.reduce((best, p) => (Math.abs(p.t - 240) < Math.abs(best.t - 240) ? p : best), series[0]);
  const xPct = ((at.t / T) * 100).toFixed(2);
  const yPct = (v) => ((1 - v / maxY) * 100).toFixed(2);
  const hover = `<span class="chart-cross" style="left:${xPct}%" aria-hidden="true"></span>
    ${[['crawled', at.crawled], ['queued', at.crawled + at.queued], ['found', at.found]].map(([k, v]) => `<span class="chart-dot-h chart-dot-h--${k}" style="left:${xPct}%;top:${yPct(v)}%" aria-hidden="true"></span>`).join('')}
    <div class="datatip" role="tooltip" style="left:calc(${xPct}% + var(--space-3));top:calc(${yPct(at.found)}% + var(--space-3))">
      <span class="datatip-head">At ${duration(at.t * 1000)}</span>
      <span class="datatip-row"><span class="swatch swatch--crawled"></span>Crawled<b>${int(at.crawled)}</b></span>
      <span class="datatip-row"><span class="swatch swatch--queued"></span>Queued<b>${int(at.queued)}</b></span>
      <span class="datatip-row"><span class="swatch swatch--skipped"></span>Not followed<b>${int(at.skipped)}</b></span>
      <span class="datatip-row datatip-row--total">Found<b>${int(at.found)}</b></span>
    </div>`;

  const codes = s.statusCodes;
  const classes = s.statusClasses;
  const fetched = s.fetched;
  const codeDetail = (prefix) =>
    Object.entries(codes).filter(([c]) => c.startsWith(prefix)).map(([c, n]) => `${c} × ${int(n)}`).join(', ');
  const codeRows = [
    { key: '2xx', label: '2xx', n: classes['2xx'], detail: codeDetail('2') },
    { key: '3xx', label: '3xx', n: classes['3xx'], detail: codeDetail('3') ? `${codeDetail('3')}, all external` : '' },
    { key: '4xx', label: '4xx', n: classes['4xx'], detail: codeDetail('4') },
    { key: '5xx', label: '5xx', n: classes['5xx'], detail: '' },
    { key: 'none', label: 'No response', n: classes.noResponse, detail: `robots.txt × ${int(s.robotsDisallowed)}`, title: "Not fetched: the other site’s robots.txt disallows them" },
  ];

  const main = `<main class="main overview" id="main">
    <h1 class="visually-hidden">Overview, live</h1>
    <div class="overview-inner">
      <section class="panel span-7 panel--first" aria-labelledby="progress-title">
        <div class="panel-head"><h2 class="panel-title" id="progress-title">Progress</h2><span class="panel-meta">Updates 4 times a second</span></div>
        <div class="hero">
          <div class="hero-figure">
            <span class="readout-label"><span class="swatch swatch--crawled"></span>Crawled</span>
            <span class="hero-value">${int(s.fetched)}<span class="hero-unit">URLs</span></span>
          </div>
          <div class="spacer"></div>
          <a href="#" class="readout"><span class="readout-label"><span class="swatch swatch--queued"></span>Queued</span><span class="readout-value">${int(queued)}</span></a>
          <a href="#" class="readout"><span class="readout-label"><span class="swatch swatch--skipped"></span>Not followed</span><span class="readout-value">${int(s.skipped)}</span></a>
          <a href="#" class="readout"><span class="readout-label">Found</span><span class="readout-value">${int(found)}</span></a>
        </div>
        <div class="scalebar">
          ${segbar([
            { key: 'crawled', label: 'Crawled', value: s.fetched },
            { key: 'queued', label: 'Queued', value: queued },
            { key: 'skipped', label: 'Not followed', value: s.skipped },
          ])}
          <div class="scale-ticks" aria-hidden="true">${tickHtml}</div>
        </div>
        <p class="note">${icon('clock', 'icon--s')}<span>No ETA: until the queue is empty, new URLs can still turn up. Found hasn't changed for ${quietFor}. Not followed are nofollow links, which this crawl's settings skip.</span></p>
      </section>

      <section class="panel span-5" aria-labelledby="alerts-title">
        <div class="panel-head"><h2 class="panel-title" id="alerts-title">Alerts</h2><span class="panel-meta">0</span></div>
        <div class="empty">
          ${icon('circle-check')}
          <div>
            <p class="empty-title">No alerts</p>
            <p class="empty-text">The site hasn't blocked the crawl, slowed it down or stopped answering. If it does, the alert shows here with what to do about it.</p>
          </div>
        </div>
      </section>

      <section class="panel span-4 panel--first" aria-labelledby="rate-title">
        <div class="panel-head"><h2 class="panel-title" id="rate-title">Rate</h2><span class="panel-meta">Last 5 s</span></div>
        <div class="readout"><span class="readout-value">${decimal(lastRate.v)}<small>URLs/s</small></span></div>
        <div class="spark">
          <svg class="chart" viewBox="0 0 ${W} ${Hs}" preserveAspectRatio="none" style="position:absolute;inset:0;width:100%;height:100%" role="img" aria-label="URLs per second since the start: steady at the cap of ${cap} a second after the first minutes">
            <line class="chart-grid" x1="0" x2="${W}" y1="${Hs}" y2="${Hs}" vector-effect="non-scaling-stroke"/>
            <line class="chart-cap" x1="0" x2="${W}" y1="${sy(cap)}" y2="${sy(cap)}" vector-effect="non-scaling-stroke"/>
            <path class="chart-line" d="${ratePath}" vector-effect="non-scaling-stroke"/>
          </svg>
          <span class="spark-cap" style="top:calc(${((1 - cap / rateMax) * 100).toFixed(2)}% - var(--space-5))">Cap ${cap}/s</span>
          <span class="spark-axis spark-axis--top">${rateMax}</span>
          <span class="spark-axis spark-axis--bottom">0</span>
        </div>
        <p class="readout-note" style="margin-top:var(--space-2)">Polite speed: up to ${cap} requests a second, ${L.options.concurrency} at a time. The crawl has run at its cap since the first minute.</p>
      </section>

      <section class="panel span-3" aria-labelledby="time-title">
        <div class="panel-head"><h2 class="panel-title" id="time-title">Time</h2></div>
        <div class="readout"><span class="readout-value">${duration(s.elapsedMs)}</span><span class="readout-note">Started ${clock(L.meta.startedAt)} on ${crawlName(L.meta.startedAt).split(',')[0]}</span></div>
        <dl class="kv" style="margin-top:var(--space-4)">
          <dt>Pages</dt><dd>${int(s.pages)} <span class="muted">of ${int(s.fetched)} URLs</span></dd>
          <dt>Downloaded</dt><dd>${bytes(s.bytesDownloaded)}</dd>
          <dt>Limit</dt><dd>${int(L.options.maxUrls)} URLs <span class="muted">(${percent(s.fetched, L.options.maxUrls, 0)} used)</span></dd>
        </dl>
      </section>

      <section class="panel span-5" aria-labelledby="codes-title">
        <div class="panel-head"><h2 class="panel-title" id="codes-title">Status codes</h2><span class="panel-meta">${int(fetched)} URLs</span></div>
        ${segbar(codeRows.map((r) => ({ key: r.key, label: r.label, value: r.n })), 'segbar--thin')}
        <div class="codes" style="--cols: var(--size-swatch) 84px minmax(0, 1fr) 48px 44px var(--size-icon-s); margin-top: var(--space-2)">
          ${codeRows
            .map((r) => `<a href="#" class="code-row${r.n === 0 ? ' is-zero' : ''}${r.key === '4xx' ? ' is-hover' : ''}" title="${esc(r.title ?? (r.n ? `Show these ${int(r.n)} URLs` : 'None so far'))}">
              <span class="swatch swatch--${r.key}"></span>
              <span class="code-name">${r.label}</span>
              <span class="code-detail">${esc(r.detail)}</span>
              <span class="code-count">${int(r.n)}</span>
              <span class="code-share">${r.n ? percent(r.n, fetched) : ''}</span>
              ${icon('chev-right', 'icon--s code-go')}
            </a>`)
            .join('')}
        </div>
      </section>

      <section class="panel span-12 panel--first" aria-labelledby="time-chart-title">
        <div class="panel-head">
          <h2 class="panel-title" id="time-chart-title">Found and crawled</h2>
          <span class="panel-meta">Since the start, every 15 s</span>
          <div class="spacer"></div>
          <div class="legend" aria-hidden="true">
            <span class="legend-item"><span class="swatch swatch--crawled"></span>Crawled</span>
            <span class="legend-item"><span class="swatch swatch--queued"></span>Queued</span>
            <span class="legend-item"><span class="swatch swatch--skipped"></span>Not followed</span>
          </div>
        </div>
        <div class="timechart">
          <svg class="chart" viewBox="0 0 ${W} ${Hc}" preserveAspectRatio="none" style="position:absolute;inset:0;width:100%;height:100%" role="img" aria-label="Found rose to ${int(found)} in the first 12 minutes; crawled has risen steadily to ${int(s.fetched)}">
            ${yTicks.map((v) => `<line class="chart-grid" x1="0" x2="${W}" y1="${cy(v)}" y2="${cy(v)}" vector-effect="non-scaling-stroke"/>`).join('')}
            <path class="chart-area-queued" d="${queuedArea}"/>
            <path class="chart-area-crawled" d="${crawledArea}"/>
            <path class="chart-edge-found" d="${line((p) => p.found)}" vector-effect="non-scaling-stroke"/>
            <path class="chart-edge-queued" d="${line((p) => p.crawled + p.queued)}" vector-effect="non-scaling-stroke"/>
            <path class="chart-edge-crawled" d="${line((p) => p.crawled)}" vector-effect="non-scaling-stroke"/>
          </svg>
          ${yTicks.map((v) => `<span class="axis-y" style="position:absolute;right:calc(100% + var(--space-2));top:calc(${((1 - v / maxY) * 100).toFixed(2)}% - var(--space-2))">${int(v)}</span>`).join('')}
          ${xTicks.map((t) => `<span class="axis-x" style="position:absolute;top:calc(100% + var(--space-1));left:${((t / T) * 100).toFixed(2)}%">${t / 60} min</span>`).join('')}
          <span class="axis-x" style="position:absolute;top:calc(100% + var(--space-1));left:100%">now</span>
          ${[
            ['Crawled', s.fetched / 2, s.fetched],
            ['Queued', s.fetched + queued / 2, queued],
            ['Not followed', s.fetched + queued + s.skipped / 2, s.skipped],
          ].map(([label, mid, n]) => `<span class="end-label" style="top:calc(${((1 - mid / maxY) * 100).toFixed(2)}% - var(--space-2))"><b>${int(n)}</b> ${label.toLowerCase()}</span>`).join('')}
          ${hover}
        </div>
      </section>
    </div>
  </main>`;

  return page({
    title: 'Overview · gardenfurnituresets.co.uk · Shouting Iguana',
    body: app({
      side: sidebar({
        current: 'overview',
        crawl: { site: gardenCrawl.site, startUrl: gardenCrawl.startUrl, when: gardenWhen },
        counts: { urls: int(s.fetched), sitemaps: '0' },
        liveSection: 'overview',
      }),
      top: topbar({
        crumbs: crumbsFor(gardenCrawl.site, gardenWhen, 'Overview'),
        status: crawlStatus('running', `${int(s.fetched)} URLs`, true),
        actions: `${button({ label: 'Pause', iconName: 'pause' })}${button({ label: 'Stop', iconName: 'stop' })}`,
      }),
      main,
    }),
  });
}

// ---------------------------------------------------------------------------------------------------- shared crawl chrome

const doneTop = (here, extra = '') =>
  topbar({
    crumbs: crumbsFor(gardenCrawl.site, gardenWhen, here),
    status: crawlStatus('completed', `${int(gardenCrawl.urlCount)} URLs`, true),
    actions: `${button({ label: 'Crawl again', iconName: 'refresh' })}`,
    extra,
  });

const gardenSide = (current, hint) =>
  sidebar({
    current,
    crawl: { site: gardenCrawl.site, startUrl: gardenCrawl.startUrl, when: gardenWhen },
    counts: gardenCounts,
    background,
    hint,
  });

// The catalog's description says what's wrong and, after ", so", why it matters. The API will carry a separate
// "why" (see the README); until then the screen splits the sentence there.
function explain(check) {
  const [what, why] = check.description.split(/, so /);
  const sentence = (t) => t.charAt(0).toUpperCase() + t.slice(1) + (/[.!?]$/.test(t) ? '' : '.');
  return { what: sentence(what), why: why ? sentence(why) : null, fix: check.fix };
}

// ---------------------------------------------------------------------------------------------------- issues

function issues({ hover = true } = {}) {
  const selectedId = 'empty-anchor-text';
  const check = data.checkById[selectedId];
  const { groups, withIssues, bySeverity, passed } = checksSummary;
  const pages = garden.stats.htmlPageCount;
  const rows = data.gardenIssue(selectedId).rows;
  const grouped = data.groupRows(rows);
  const total = rows.length;
  const words = explain(check);

  const checkRow = (c) => {
    if (c.skipped) {
      const reason = data.skipReason(c.id);
      const short = reason.split(/, so |\. /)[0].replace(/\.$/, '');
      return `<div class="check check--skipped" role="option" aria-selected="false" title="${esc(reason)}">
        <svg class="sev sev--skipped" role="img" aria-label="Skipped"><use href="#i-sev-skipped"/></svg>
        <span class="check-title">${esc(c.title)}<span class="check-reason">${esc(short)}</span></span>
        <span class="skip-tag">Skipped</span>
      </div>`;
    }
    const selected = c.id === selectedId;
    return `<a href="#" class="check${selected ? ' is-selected is-focused' : ''}" role="option" aria-selected="${selected}">
      ${sev(c.severity)}<span class="check-title">${esc(c.title)}</span><span class="check-count">${int(c.count)}</span>
    </a>`;
  };

  const list = groups
    .map((g) => {
      const n = g.rows.filter((r) => !r.skipped).length;
      return `<div class="cat" role="presentation"><span>${esc(g.title)}</span><span class="num">${n}</span></div>${g.rows.map(checkRow).join('')}`;
    })
    .join('');

  const cols = '--cols: var(--size-control) minmax(0, 184px) minmax(0, 1fr) 64px 132px';
  const membersShown = 7;
  const groupRow = (g, i) => {
    const open = i === 0;
    const share = g.urls.length / total;
    const related = g.relatedUrl ? url(g.relatedUrl, 44) : '<span class="faint">None</span>';
    let html = `<div class="grid-row group-row" role="row" aria-level="1" aria-expanded="${open}">
      <span class="cell" role="gridcell"><span class="disclose">${icon(open ? 'chev-down' : 'chev-right', 'icon--s')}</span></span>
      <span class="cell cell--detail" role="gridcell">${esc(g.detail ?? 'No detail')}</span>
      <span class="cell" role="gridcell">${related}</span>
      <span class="cell cell--num strong" role="gridcell">${int(g.urls.length)}</span>
      <span class="cell" role="gridcell"><span class="group-share"><span class="meter"><span class="meter-fill" style="width:${(share * 100).toFixed(2)}%"></span></span><span class="num muted">${percent(g.urls.length, total)}</span></span></span>
    </div>`;
    if (open) {
      html += g.urls
        .slice(0, membersShown)
        .map((u) => `<div class="grid-row member-row" role="row" aria-level="2">
          <span class="cell" role="gridcell"></span>
          <span class="cell cell--url" role="gridcell">${url(u, 80)}</span>
          <span class="cell cell--num" role="gridcell"></span>
          <span class="cell" role="gridcell"></span>
        </div>`)
        .join('');
      html += `<div class="member-more" role="row"><span role="gridcell">${int(g.urls.length - membersShown)} more pages in this group</span><a href="#" class="link" role="gridcell">Show all ${int(g.urls.length)} in URLs</a></div>`;
    }
    return html;
  };

  const main = `<main class="main main--split" id="main">
    <section class="pane checks" aria-label="Checks">
      <div class="checks-tools">
        <label class="field field--quiet">${icon('search')}<span class="visually-hidden">Filter checks</span><input placeholder="Filter checks">${kbd('Ctrl', 'F')}</label>
        <div class="segmented segmented--fill" role="group" aria-label="Severity">
          <button type="button" class="toggle is-on" aria-pressed="true">All <span class="num">${withIssues}</span></button>
          ${sevKeys.map((k) => `<button type="button" class="toggle" aria-pressed="false">${sev(k, false)}${severityWord[k]}s <span class="num">${bySeverity[k]}</span></button>`).join('')}
        </div>
      </div>
      <div class="scroll"><div class="checks-list" role="listbox" aria-label="Checks with issues, by category">${list}</div></div>
      <div class="checks-foot">${icon('circle-check', 'icon--s')}<span>${passed} checks found nothing.</span><a href="#" class="link">Show them</a></div>
    </section>

    <section class="pane detail" aria-labelledby="check-title">
      <header class="detail-head">
        <div class="detail-kicker"><span class="sev-word">${sev(check.severity, false)}${severityWord[check.severity]}</span><span>${esc(data.categoryTitle[check.category])}</span></div>
        <div class="detail-actions">
          ${button({ label: 'Open in URLs', iconName: 'urls' })}
          ${button({ label: 'Copy URLs', iconName: 'copy' })}
          <span class="anchor">${button({ label: 'Export', iconName: 'download', extraClass: hover ? 'is-hover' : '' })}${hover ? tooltip('Export this check', ['Ctrl', 'E'], 'top:calc(100% + var(--space-2));right:0') : ''}</span>
          ${iconButton('more', 'More: hide this check')}
        </div>
        <h1 class="detail-title" id="check-title">${esc(check.title)}</h1>
        <div class="detail-figures">
          <div class="readout">
            <span class="readout-label">Pages affected</span>
            <span class="readout-value">${int(total)}<small>of ${int(pages)} pages</small></span>
            <span class="meter" role="img" aria-label="${percent(total, pages, 0)} of pages"><span class="meter-fill" style="width:${((total / pages) * 100).toFixed(2)}%"></span></span>
          </div>
          <div class="readout">
            <span class="readout-label">Groups</span>
            <span class="readout-value">${grouped.length}<small>from ${int(total)} issues</small></span>
            <span class="readout-note">One group covers ${int(grouped[0].urls.length)} pages</span>
          </div>
        </div>
      </header>
      <div class="scroll">
        <dl class="wwf">
          <dt>What's wrong</dt><dd>${esc(words.what)}</dd>
          <dt>Why it matters</dt><dd>${esc(words.why)}</dd>
          <dt>How to fix it</dt><dd>${esc(words.fix)}</dd>
        </dl>
        <div class="groups-head">
          <h2 class="panel-title">Affected pages</h2>
          <span class="panel-meta">Grouped by detail and related URL, largest first</span>
          <div class="spacer"></div>
          ${button({ label: 'Group: detail and related URL', iconName: 'group', size: 's' })}
        </div>
        <div class="groups" role="treegrid" aria-label="Affected pages, grouped" style="${cols}">
          <div class="grid-head" role="row">
            <span class="cell" role="columnheader"><span class="visually-hidden">Expand</span></span>
            <span class="cell" role="columnheader">Detail</span>
            <span class="cell" role="columnheader">Related URL</span>
            <span class="cell cell--num is-sorted" role="columnheader" aria-sort="descending">Pages${icon('arrow-down', 'icon--xs')}</span>
            <span class="cell" role="columnheader">Share</span>
          </div>
          ${grouped.map(groupRow).join('')}
        </div>
      </div>
    </section>
  </main>`;

  return page({
    title: `${check.title} · Issues · gardenfurnituresets.co.uk · Shouting Iguana`,
    body: app({ side: gardenSide('issues'), top: doneTop('Issues'), main }),
  });
}

// ---------------------------------------------------------------------------------------------------- urls

// The Overview preset. Widths are view state (resizable, remembered per view), so they live with the view, not in tokens.
const overviewColumns = [
  { id: 'n', label: '#', width: 48, num: true },
  { id: 'url', label: 'URL', width: 352, pin: true },
  { id: 'status', label: 'Status', width: 64 },
  { id: 'indexability', label: 'Indexability', width: 96 },
  { id: 'issues', label: 'Issues', width: 76, num: true, sort: 1 },
  { id: 'inlinks', label: 'Inlinks', width: 76, num: true, sort: 2 },
  { id: 'response', label: 'Response', width: 96, num: true },
  { id: 'title', label: 'Title', width: 300 },
  { id: 'size', label: 'Size', width: 84, num: true },
  { id: 'words', label: 'Words', width: 72, num: true },
  { id: 'depth', label: 'Depth', width: 64, num: true },
];
const urlBudget = Math.floor((overviewColumns[1].width - 16) / 7.2);
const reasonWord = {
  noindex: 'Noindex', canonicalised: 'Canonicalised', redirected: 'Redirected', clientError: 'Client error',
  serverError: 'Server error', blockedByRobots: 'Blocked by robots.txt', blocked: 'Blocked', unreachable: 'Unreachable',
};
// The analysis's default limits, which the fixtures README lists and the garden analysis used.
const SLOW_MS = 1000;
const MIN_WORDS = 200;

function gridRows() {
  const all = data.gardenUrls().rows.filter((r) => r.isInternal && r.type === 'html');
  all.sort((a, b) => (b.issueCount - a.issueCount) || (b.uniqueInlinks - a.uniqueInlinks) || (a.id - b.id));
  return { rows: all, total: data.gardenUrls().total };
}

function cellsFor(r, i) {
  const worst = data.worstSeverity(r.issues);
  const issuesCell = r.issueCount
    ? `<span class="value-flag">${sev(worst)}${int(r.issueCount)}</span>`
    : '<span class="faint">0</span>';
  const response = r.responseMs > SLOW_MS ? `<span class="value-flag">${sev('warning')}${ms(r.responseMs)}</span>` : ms(r.responseMs);
  const words = r.isPage && r.wordCount < MIN_WORDS ? `<span class="value-flag">${sev('notice')}${int(r.wordCount)}</span>` : int(r.wordCount ?? 0);
  const index = r.indexable ? '<span class="muted">Indexable</span>' : `<span class="strong">${reasonWord[r.indexabilityReason] ?? r.indexabilityReason}</span>`;
  return {
    n: `<span class="cell-rownum">${int(i + 1)}</span>`,
    url: url(r.url, urlBudget),
    status: http(r.statusCode),
    indexability: index,
    issues: issuesCell,
    inlinks: int(r.uniqueInlinks),
    title: r.title ? esc(r.title) : '<span class="faint">None</span>',
    response,
    size: bytes(r.sizeBytes),
    words,
    depth: r.depth ?? '<span class="faint">Not linked</span>',
  };
}

const colsStyle = (cols) => `--cols: ${cols.map((c) => `${c.width}px`).join(' ')}`;

function gridHead(cols) {
  return `<div class="grid-head" role="row">${cols
    .map((c) => {
      const sortMark = c.sort ? `<span class="sort-mark" title="Sorted ${c.sort === 1 ? 'first' : 'second'}, highest first">${icon('arrow-down', 'icon--xs')}<span class="sort-rank">${c.sort}</span></span>` : '';
      return `<span class="cell${c.num ? ' cell--num' : ''}${c.pin ? ' cell--pin' : ''}${c.sort ? ' is-sorted' : ''}" role="columnheader"${c.sort ? ' aria-sort="descending"' : ''}>${c.num && c.sort ? sortMark : ''}${c.label}${!c.num ? sortMark : ''}</span>`;
    })
    .join('')}</div>`;
}

const SK_WIDTHS = [62, 48, 71, 55, 66, 40, 58, 74, 51, 63, 45, 69, 57, 52, 67, 43, 60, 72, 49, 64, 54, 70, 46, 59, 65, 50];

function skeletonRow(i) {
  const w = SK_WIDTHS[i % SK_WIDTHS.length];
  const v = SK_WIDTHS[(i * 7 + 3) % SK_WIDTHS.length];
  const cell = (inner, num = false, pin = false) => `<span class="cell${num ? ' cell--num' : ''}${pin ? ' cell--pin' : ''}">${inner}</span>`;
  const bar = (pct, align = 'left') => `<span class="sk sk--text" style="width:${pct}%;${align === 'right' ? 'margin-left:auto' : ''}"></span>`;
  return `<div class="grid-row sk-row" aria-hidden="true">
    ${cell(bar(46, 'right'), true)}
    ${cell(`<span class="sk-url"><span class="sk sk--text" style="width:36%"></span><span class="sk sk--text sk--strong" style="width:${Math.round(w * 0.6)}%"></span></span>`, false, true)}
    ${cell('<span class="sk sk--pill sk--code"></span>')}
    ${cell(bar(68))}
    ${cell(bar(32, 'right'), true)}
    ${cell(bar(38, 'right'), true)}
    ${cell(bar(v + 18))}
    ${cell(bar(54, 'right'), true)}
    ${cell(bar(58, 'right'), true)}
    ${cell(bar(44, 'right'), true)}
    ${cell(bar(22, 'right'), true)}
  </div>`;
}

function inspectorSummary(I) {
  const s = I.summary;
  const titleLen = s.titleLength;
  const descLen = s.metaDescriptionLength;
  const titleTone = titleLen > 60 || titleLen < 30 ? 'notice' : null;
  const descTone = descLen > 155 || descLen < 70 ? 'notice' : null;
  const tens = (max) => Array.from({ length: max / 10 + 1 }, (_, i) => i * 10);
  const issueCounts = I.issues.rows.reduce((m, r) => ((m[r.severity] = (m[r.severity] ?? 0) + 1), m), {});
  const issueText = sevKeys.filter((k) => issueCounts[k]).map((k) => `${issueCounts[k]} ${severityWord[k].toLowerCase()}${issueCounts[k] > 1 ? 's' : ''}`).join(', ');
  return `
    <section class="facts" aria-label="Indexability">
      <div class="verdict">${icon('circle-check')}<span class="verdict-title">Indexable</span><span class="muted">${http(s.statusCode)}</span></div>
      <p class="muted">Answers 200, its canonical is itself, and nothing says noindex. It isn't in a sitemap: the site has none.</p>
      <a href="#" class="flag">${sev('warning')}<span>${I.issues.total} issues: ${issueText}</span>${icon('chev-right', 'icon--xs')}</a>
    </section>
    <section class="facts" aria-label="Title and description">
      <div class="fact">
        <div class="fact-head"><span class="fact-name">Title</span><span class="num">${titleLen} characters</span>${titleTone ? `<span class="flag">${sev('notice')}Too long</span>` : ''}</div>
        <p class="fact-value">${esc(s.title)}</p>
        ${gauge({ min: 0, max: 80, lo: 30, hi: 60, value: titleLen, ticks: tens(80), tone: titleTone, labels: [[30, '30'], [60, '60']], aria: `${titleLen} characters; the check allows 30 to 60` })}
      </div>
      <div class="fact">
        <div class="fact-head"><span class="fact-name">Meta description</span><span class="num">${descLen} characters</span>${descTone ? `<span class="flag">${sev('notice')}Too long</span>` : ''}</div>
        <p class="fact-value">${esc(s.metaDescription)}</p>
        ${gauge({ min: 0, max: 200, lo: 70, hi: 155, value: descLen, ticks: Array.from({ length: 9 }, (_, i) => i * 25), tone: descTone, labels: [[70, '70'], [155, '155']], aria: `${descLen} characters; the check allows 70 to 155` })}
      </div>
      <div class="fact">
        <div class="fact-head"><span class="fact-name">H1</span><span class="num">${s.h1Count} H1, ${s.h2Count} H2</span></div>
        <p class="fact-value">${esc(s.h1)}</p>
      </div>
    </section>
    <section class="facts" aria-label="Facts">
      <div class="fact-row"><span class="fact-name">Canonical</span><span class="strong">Itself</span><span></span></div>
      <div class="fact-row"><span class="fact-name">Words</span>${gauge({ min: 0, max: 1000, lo: MIN_WORDS, hi: 1000, value: s.wordCount, ticks: [0, 200, 400, 600, 800, 1000], labels: [[MIN_WORDS, '200']], aria: `${s.wordCount} words; the check wants at least 200` })}<span class="fact-num">${int(s.wordCount)}</span></div>
      <div class="fact-row"><span class="fact-name">Response</span>${gauge({ min: 0, max: 1200, lo: 0, hi: SLOW_MS, value: s.responseMs, ticks: [0, 200, 400, 600, 800, 1000, 1200], labels: [[SLOW_MS, '1,000 ms']], aria: `${s.responseMs} ms; slow above 1,000 ms` })}<span class="fact-num">${ms(s.responseMs)}</span></div>
      <div class="fact-row"><span class="fact-name">First byte</span><span class="muted">${Math.round((s.ttfbMs / s.responseMs) * 100)}% of the response</span><span class="fact-num">${ms(s.ttfbMs)}</span></div>
      <div class="fact-row"><span class="fact-name">Size</span><span class="muted">${bytes(s.bodyBytes)} of HTML, HTTP/${s.httpVersion === '2.0' ? '2' : s.httpVersion}</span><span class="fact-num">${bytes(s.sizeBytes)}</span></div>
      <div class="fact-row"><span class="fact-name">Depth</span><span class="muted">First found on ${esc(pathOf(s.foundOn))}</span><span class="fact-num">${s.depth}</span></div>
      <div class="fact-row"><span class="fact-name">Inlinks</span><span class="muted">Hyperlinks from ${int(I.inlinks.uniqueUrls)} pages</span><span class="fact-num">${int(I.inlinks.total)}</span></div>
      <div class="fact-row"><span class="fact-name">Outlinks</span><span class="muted">${int(I.outlinks.hyperlinks)} of them hyperlinks</span><span class="fact-num">${int(I.outlinks.total)}</span></div>
      <div class="fact-row"><span class="fact-name">Structured data</span><span class="strong">${esc(s.structuredDataTypes.join(', '))}</span><span class="fact-num">${I.structuredData.length}</span></div>
    </section>`;
}

function inspectorSkeleton() {
  const line = (w) => `<span class="sk sk--text" style="width:${w}%"></span>`;
  return `<div class="skeleton-view shimmer sk-insp" aria-hidden="true">
    <section class="facts">${line(38)}${line(84)}${line(44)}</section>
    <section class="facts">
      <div class="fact">${line(22)}${line(92)}<span class="sk sk--gauge"></span></div>
      <div class="fact">${line(34)}${line(96)}${line(71)}<span class="sk sk--gauge"></span></div>
      <div class="fact">${line(12)}${line(78)}</div>
    </section>
    <section class="facts">${[58, 70, 64, 52, 66, 60, 56].map((w) => `<div class="fact-row">${line(70)}${line(w)}${line(100)}</div>`).join('')}</section>
  </div>`;
}

function urlsScreen({ loading = false } = {}) {
  const I = data.inspector('product');
  const { rows, total } = gridRows();
  const shownRows = rows.slice(0, 30);
  const selectedIndex = shownRows.findIndex((r) => r.id === I.summary.id);
  const cols = overviewColumns;

  const body = loading
    ? `<div class="skeleton-view shimmer">${Array.from({ length: 28 }, (_, i) => skeletonRow(i)).join('')}</div>`
    : shownRows
        .map((r, i) => {
          const c = cellsFor(r, i);
          const sel = i === selectedIndex;
          return `<div class="grid-row${sel ? ' is-selected is-focused' : ''}" role="row" id="row-${r.id}" aria-rowindex="${i + 2}" aria-selected="${sel}">${cols
            .map((col) => `<span class="cell${col.num ? ' cell--num' : ''}${col.pin ? ' cell--pin' : ''}" role="gridcell">${c[col.id]}</span>`)
            .join('')}</div>`;
        })
        .join('');

  const presets = ['Overview', 'Indexability', 'Content', 'Performance', 'Links'];
  const count = loading
    ? '<span class="count skeleton-view" aria-hidden="true"><span class="sk sk--text sk--inline" style="width:var(--size-label)"></span></span><span class="visually-hidden" aria-live="polite">Loading URLs</span>'
    : `<span class="count" aria-live="polite"><b>${int(rows.length)}</b> of ${int(total)} URLs</span>`;
  const s = I.summary;
  const { host, rest } = urlParts(s.url);

  const main = `<main class="main main--inspect" id="main">
    <section class="pane" aria-label="URLs">
      <div class="viewbar">
        <div class="segmented" role="tablist" aria-label="Column preset">${presets
          .map((p, i) => `<button type="button" class="toggle${i === 0 ? ' is-on' : ''}" role="tab" aria-selected="${i === 0}">${p}</button>`)
          .join('')}</div>
        ${button({ label: 'Saved views', trailing: 'chev-down', size: 's', kind: 'ghost' })}
        <div class="spacer"></div>
        ${iconButton('columns', 'Choose columns', { size: 's' })}
        ${button({ label: 'Export', iconName: 'download', size: 's' })}
      </div>
      <div class="filterbar" role="toolbar" aria-label="Filters">
        <span class="chip"><span>Internal</span><button type="button" class="chip-x" aria-label="Remove the filter Internal">${icon('close', 'icon--xs')}</button></span>
        <span class="chip"><span>Type is <b>HTML</b></span><button type="button" class="chip-x" aria-label="Remove the filter Type is HTML">${icon('close', 'icon--xs')}</button></span>
        <button type="button" class="chip chip--add">${icon('plus', 'icon--xs')}Filter${kbd('Ctrl', 'F')}</button>
        <div class="spacer"></div>
        ${count}
        <label class="field field--quiet field--s">${icon('search', 'icon--s')}<span class="visually-hidden">Search URLs and titles</span><input placeholder="Search URLs and titles"></label>
      </div>
      <div class="grid-wrap"><div class="grid" role="grid" tabindex="0" aria-label="URLs" aria-multiselectable="true" aria-rowcount="${rows.length + 1}" aria-busy="${loading}"${loading ? '' : ` aria-activedescendant="row-${I.summary.id}"`} style="${colsStyle(cols)}">
        <div class="grid-inner">
          ${gridHead(cols)}
          ${body}
        </div>
      </div><span class="grid-edge" aria-hidden="true"></span></div>
    </section>

    <aside class="inspector" aria-label="Inspector">
      <div class="insp-resize" role="separator" aria-orientation="vertical" aria-label="Resize the inspector"></div>
      <div class="insp-head">
        <div class="insp-url">
          <span class="insp-path">${esc(rest)}</span>
          <span class="insp-host">${esc(s.url.startsWith('https') ? 'https://' : 'http://')}${esc(host)}</span>
        </div>
        <div class="insp-tools">
          ${iconButton('copy', 'Copy the URL', { size: 's' })}
          ${iconButton('external', 'Open in browser', { size: 's' })}
          ${iconButton('dock-bottom', 'Dock at the bottom', { size: 's' })}
          ${iconButton('close', 'Close the inspector', { size: 's', keys: ['Esc'] })}
        </div>
      </div>
      <div class="tabs">
        <div class="tablist" role="tablist" aria-label="About this URL">
        <button type="button" class="tab" role="tab" aria-selected="true">Summary</button>
        <button type="button" class="tab" role="tab" aria-selected="false">Issues${loading ? '' : ` <span class="num">${I.issues.total}</span>`}</button>
        <button type="button" class="tab" role="tab" aria-selected="false">Inlinks${loading ? '' : ` <span class="num">${int(I.inlinks.total)}</span>`}</button>
        <button type="button" class="tab" role="tab" aria-selected="false">Outlinks${loading ? '' : ` <span class="num">${int(I.outlinks.total)}</span>`}</button>
        </div>
        <button type="button" class="tab tab--more" aria-haspopup="menu">More${icon('chev-down', 'icon--xs')}</button>
      </div>
      <div class="insp-body" role="tabpanel" aria-label="Summary"${loading ? ' aria-busy="true"' : ''}>
        ${loading ? inspectorSkeleton() : inspectorSummary(I)}
      </div>
    </aside>
  </main>`;

  return page({
    title: loading ? 'URLs (loading) · gardenfurnituresets.co.uk · Shouting Iguana' : 'URLs · gardenfurnituresets.co.uk · Shouting Iguana',
    body: app({ side: gardenSide('urls'), top: doneTop('URLs'), main }),
  });
}

const urls = () => urlsScreen();
const urlsLoading = () => urlsScreen({ loading: true });

// ---------------------------------------------------------------------------------------------------- palette

// Not one of the five screens: the command palette open over Issues, because direction A leads with the keyboard.
function palette() {
  const query = 'brok';
  const counts = Object.fromEntries(garden.analysis.counts.map((c) => [c.checkId, c.count]));
  const matches = data.checks.checks.filter((c) => c.title.toLowerCase().includes(query));
  matches.sort((a, b) => (counts[b.id] ?? 0) - (counts[a.id] ?? 0));
  const mark = (title) => {
    const i = title.toLowerCase().indexOf(query);
    return `${esc(title.slice(0, i))}<mark>${esc(title.slice(i, i + query.length))}</mark>${esc(title.slice(i + query.length))}`;
  };
  const urlMatches = data.gardenUrls().rows.filter((r) => `${r.url} ${r.title ?? ''}`.toLowerCase().includes(query)).length;
  const items = matches
    .map((c, i) => {
      const n = counts[c.id];
      const figure = n ? `${int(n)} ${n === 1 ? 'page' : 'pages'}` : 'No issues';
      const glyph = n ? sev(c.severity) : `<svg class="sev sev--muted" role="img" aria-label="No issues"><use href="#i-circle-check"/></svg>`;
      return `<div class="palette-item${i === 0 ? ' is-active' : ''}" role="option"${i === 0 ? ' id="palette-first"' : ''} aria-selected="${i === 0}">
        ${glyph}<span>${mark(c.title)}</span><span class="palette-where">${esc(data.categoryTitle[c.category])}</span><span class="palette-figure">${figure}</span>
      </div>`;
    })
    .join('');
  const overlay = `<div class="scrim" aria-hidden="true"></div>
  <div class="palette" role="dialog" aria-modal="true" aria-label="Command palette">
    <div class="palette-input">${icon('search')}<span class="palette-query" role="combobox" aria-label="Search or run a command" aria-expanded="true" aria-controls="palette-list" aria-activedescendant="palette-first">${query}<span class="caret" aria-hidden="true"></span></span>${kbd('Esc')}</div>
    <div class="palette-list" id="palette-list" role="listbox" aria-label="Results">
      <div class="palette-group">Checks</div>
      ${items}
      <div class="palette-group">URLs</div>
      <div class="palette-item" role="option" aria-selected="false">${icon('search', 'icon--s')}<span>Search URLs and titles for “${query}”</span><span class="palette-where">${urlMatches ? `${int(urlMatches)} matches` : 'No matches in this crawl'}</span><span class="palette-figure">${kbd('Ctrl', 'F')}</span></div>
      <div class="palette-group">Actions</div>
      <div class="palette-item" role="option" aria-selected="false">${icon('download', 'icon--s')}<span>Export every check, with a summary sheet</span><span class="palette-where">Issues</span><span class="palette-figure">${kbd('Ctrl', 'E')}</span></div>
    </div>
    <div class="palette-foot"><span class="hint">${kbd('↑', '↓')}<span>Move</span></span><span class="hint">${kbd('Enter')}<span>Open</span></span><span class="hint">${kbd('Esc')}<span>Close</span></span><span class="spacer"></span><span>Checks, URLs, actions, crawls and settings</span></div>
  </div>`;
  return issues({ hover: false }).replace('<div class="app">', '<div class="app" inert>').replace('</body>', `${overlay}\n</body>`);
}

export const screens = { home, live, issues, urls, 'urls-loading': urlsLoading, palette };

const only = process.argv.slice(2);
writeFileSync(join(here, 'tokens.css'), tokensCss());
for (const [name, fn] of Object.entries(screens)) {
  if (only.length && !only.includes(name)) continue;
  out(`${name}.html`, fn());
  console.log(`wrote ${name}.html`);
}
