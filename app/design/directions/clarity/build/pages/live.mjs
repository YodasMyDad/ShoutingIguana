// Overview while the garden crawl runs, 60% of the way: garden/live.json, snapshot markers["60%"].
import { page, kbd } from '../lib/shell.mjs';
import { icon } from '../lib/icons.mjs';
import { esc, num, bytes, duration, durationFig, time, dateTime, pct } from '../lib/format.mjs';
import { sparkline, lineChart } from '../lib/charts.mjs';
import { live60, catalog } from '../lib/data.mjs';

export function livePage() {
  const { live, snap, series, rate, queuePeak, queuePeakAt, options, startedAt } = live60();
  // An upper bound only: the URL limit and the rate cap make one meaningful, the queue doesn't.
  const limitLeftMs = ((options.maxUrls - snap.fetched) / options.maxRequestsPerSecond) * 1000;
  const [dlV, dlU] = bytes(snap.bytesDownloaded, { parts: true });

  const stat = (label, value, context, extra = '', href = 'urls.html') => `
      <a class="stat" href="${href}">
        <span class="stat__label">${label}</span>
        <span class="stat__value">${value}</span>
        ${extra}
        <span class="stat__context">${context}</span>
      </a>`;

  const elapsedMin = snap.elapsedMs / 60000;
  const chart = lineChart({
    series: [
      { name: 'found', kind: 'secondary', values: series.map(s => s.discovered) },
      { name: 'crawled', kind: 'primary', values: series.map(s => s.fetched), area: true },
    ],
    max: 6000, yTicks: [0, 2000, 4000, 6000],
    xMax: elapsedMin, xTicks: [0, 5, 10, 15, 20].map(m => ({ at: m, label: m ? `${m} min` : '0' })),
    height: 'var(--chart-height)', label: `Found and crawled URLs over ${duration(snap.elapsedMs)}: ${num(snap.discovered)} found, ${num(snap.fetched)} crawled`,
  });

  const classes = [
    ['2xx', 's2xx', snap.statusClasses['2xx'], '200 OK'],
    ['3xx', 's3xx', snap.statusClasses['3xx'], `301 × ${num(snap.statusCodes['301'] ?? 0)}, external links`],
    ['4xx', 's4xx', snap.statusClasses['4xx'], `404 × ${num(snap.statusCodes['404'] ?? 0)}, 403 × ${num(snap.statusCodes['403'] ?? 0)}`],
    ['5xx', 's5xx', snap.statusClasses['5xx'], 'None so far'],
    ['No response', 'snone', snap.statusClasses.noResponse, 'Disallowed by robots.txt'],
  ];
  const maxClass = Math.max(...classes.map(c => c[2]));
  const statusRows = classes.map(([label, cls, n, codes]) => {
    const hovered = label === '4xx';
    const w = n ? Math.max((n / maxClass) * 100, 0.6) : 0;
    const tag = n ? 'a' : 'div';
    return `
          <${tag}${n ? ' href="urls.html"' : ''} class="status-row ${cls}${n ? '' : ' is-zero'}${hovered ? ' is-hovered' : ''}"${n ? ` aria-label="${label}: ${num(n)} URLs. Opens them in URLs."` : ''}>
            <span class="status-row__label"><span class="mark" aria-hidden="true"></span>${label}</span>
            <span class="status-row__codes">${hovered
              ? `<span class="chip chip--preview chip--sm"><span class="chip__field">Status</span><span class="chip__op">is</span><span class="chip__value"><span class="mark s4xx" aria-hidden="true"></span>4xx</span></span><span class="status-row__go" title="Opens these URLs">${kbd('Enter')}</span>`
              : codes}</span>
            <span class="status-row__n">${num(n)}</span>
            <span class="status-row__bar"><span class="bar-track"><span class="bar-fill" style="--value:${w.toFixed(2)}%;--min:${n ? 'var(--bar-min)' : '0'}"></span></span></span>
          </${tag}>`;
  }).join('');

  const main = `
    <div class="page page--live">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">Overview <span class="badge badge--accent live-badge"><span class="live-dot" aria-hidden="true"></span>Live</span></h1>
          <p class="page-subtitle">Crawling <span class="mono">${esc(live.startUrl)}</span> at the Polite speed: 2 requests at a time, up to 2 a second. ${dlV}&nbsp;${dlU} downloaded so far.</p>
        </div>
        <div class="page-header__actions">
          <span class="updated" aria-live="off">Updated ${time(snap.at, { seconds: true })}</span>
        </div>
      </header>

      <section class="card stats stats--live" style="--cols:6" aria-label="Progress">
        ${stat('Crawled', num(snap.fetched), `URLs, of a ${num(options.maxUrls)} limit`)}
        ${stat('Found', num(snap.discovered), `${num(snap.skipped)} nofollow, not followed`)}
        ${stat('Queued', num(snap.queued), `Down from ${num(queuePeak)} at ${duration(queuePeakAt)}`)}
        ${stat('Pages', num(snap.pages), 'HTML pages read')}
        ${stat('URLs a second', snap.pagesPerSecond.toFixed(1), 'At the Polite limit of 2', sparkline(rate, { max: 3, limit: 2, label: 'URLs a second, every 30 s, against the limit of 2' }), '#')}
        ${stat('Elapsed', durationFig(snap.elapsedMs), `At this pace, done within ${duration(limitLeftMs, { ceil: true })}`, '', '#')}
      </section>

      <div class="live-grid">
        <section class="card live-progress" aria-labelledby="progress-title">
          <div class="card__header">
            <h2 class="card__title" id="progress-title">Found and crawled</h2>
            <span class="card__meta">URLs over ${duration(snap.elapsedMs)}</span>
          </div>
          <div class="card__body">
            ${chart}
            <div class="found-split">
              <div class="stack" role="img" aria-label="Of ${num(snap.discovered)} URLs found: ${num(snap.fetched)} crawled, ${num(snap.queued)} queued, ${num(snap.skipped)} not followed">
                <span class="stack__crawled" style="--n:${snap.fetched}"></span><span class="stack__queued" style="--n:${snap.queued + snap.inFlight}"></span><span class="stack__skipped" style="--n:${snap.skipped}"></span>
              </div>
              <div class="legend">
                <span class="legend__item"><span class="legend__swatch stack__crawled"></span>Crawled <span class="legend__value">${num(snap.fetched)}</span></span>
                <span class="legend__item"><span class="legend__swatch stack__queued"></span>Queued <span class="legend__value">${num(snap.queued)}</span></span>
                <span class="legend__item"><span class="legend__swatch stack__skipped"></span>Not followed <span class="legend__value">${num(snap.skipped)}</span></span>
                <span class="legend__item legend__item--end">Of the ${num(snap.discovered)} found</span>
              </div>
            </div>
          </div>
          <div class="card__foot">
            ${icon('layers', { size: 'sm' })}<span>When the crawl ends, the analysis runs on its own: ${catalog.checks.length} checks. <a class="link" href="urls.html">URLs</a> works now, while it crawls.</span>
          </div>
        </section>

        <div class="live-side">
          <section class="card" aria-labelledby="status-title">
            <div class="card__header">
              <h2 class="card__title" id="status-title">Status codes</h2>
              <span class="card__meta">${num(snap.fetched)} URLs</span>
            </div>
            <div class="card__body card__body--tight">
              <div class="status-rows">${statusRows}
              </div>
            </div>
          </section>

          <section class="card" aria-labelledby="alerts-title">
            <div class="card__header">
              <h2 class="card__title" id="alerts-title">Alerts</h2>
            </div>
            <div class="card__body">
              <div class="empty empty--row" role="status">
                <span class="empty__icon">${icon('shield-check', { size: 'md' })}</span>
                <div>
                  <p class="empty__title">None so far</p>
                  <p class="empty__text">Nothing has blocked, slowed or refused this crawl. If the site starts blocking it, goes down or its robots.txt can’t be read, the alert shows here with what to do.</p>
                </div>
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>`;

  return page({
    title: 'Overview (crawling), gardenfurnituresets.co.uk', file: 'live.html', section: 'overview', crawlState: 'running',
    liveCount: snap.fetched,
    crumbs: [
      { label: 'gardenfurnituresets.co.uk', favicon: 'gardenfurnituresets.co.uk', strong: true },
      { label: dateTime(startedAt) },
      { label: 'Overview' },
    ],
    main,
  });
}
