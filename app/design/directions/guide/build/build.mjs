// Writes direction C's pages from the real fixtures, so every number, URL, title and check comes from the crawls.
// The only invented content is the illustrative .example sites on Home (listed in the README).
// Usage: node build/build.mjs   (needs app/design/data/fixtures; see app/design/data/README.md)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sprite } from './icons.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.resolve(here, '..');
const FX = path.resolve(here, '../../../data/fixtures');
const J = p => JSON.parse(fs.readFileSync(path.join(FX, p), 'utf8'));

// ---------- Data ----------
const catalog = J('api/checks.json');
const checkById = Object.fromEntries(catalog.checks.map(c => [c.id, c]));
const catTitle = Object.fromEntries(catalog.categories.map(c => [c.id, c.title]));
const library = J('api/crawls.json');
const garden = library.find(c => c.site === 'gardenfurnituresets.co.uk');
const synthetic = library.find(c => c.site !== 'gardenfurnituresets.co.uk');
const stats = J('api/garden/stats.json');
const analysis = J('api/garden/analysis.json');
const synthAnalysis = J('api/synthetic/analysis.json');
const options = J('api/garden/options.json');
const defaults = J('api/crawl-options-defaults.json');
const urls = J('garden/urls.json');
const live = J('garden/live.json');
const countById = Object.fromEntries(analysis.counts.map(x => [x.checkId, x.count]));
const skippedIds = analysis.skipped.map(s => s.checkId);
const issueRows = id => J(`api/garden/issues/${id}.json`).rows;

const ORIGIN = 'https://www.gardenfurnituresets.co.uk';
const SITE = garden.site;
const SEVS = ['error', 'warning', 'notice'];
const WORD = {
  error: { one: 'Error', many: 'Errors', lone: 'error', lmany: 'errors' },
  warning: { one: 'Warning', many: 'Warnings', lone: 'warning', lmany: 'warnings' },
  notice: { one: 'Notice', many: 'Notices', lone: 'notice', lmany: 'notices' },
};
const checksWithIssues = catalog.checks.filter(c => countById[c.id] > 0);
const checksBySev = Object.fromEntries(SEVS.map(s => [s, checksWithIssues.filter(c => c.severity === s)]));
const issuesBySev = Object.fromEntries(SEVS.map(s => [s, checksBySev[s].reduce((a, c) => a + countById[c.id], 0)]));
const ranCount = analysis.counts.length;
const passedCount = ranCount - checksWithIssues.length;

// Notes explain skipped checks in plain words; reasons per check arrive with E2. Until then, match by subject.
const skipNote = id => analysis.notes.find(nt => (id === 'duplicate-site-version' ? /versions/ : /sitemap/).test(nt));
const shortNote = id => `${skipNote(id).split(', so ')[0]}.`;

// ---------- Formatting ----------
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const n = x => (x == null ? '' : new Intl.NumberFormat('en-GB').format(x));
const kb = b => (b == null ? '' : b >= 1024 * 1024 ? `${(b / 1024 / 1024).toFixed(1)} MB` : `${n(Math.round(b / 1024))} KB`);
const pct = (a, b) => `${Math.round((a / b) * 100)}%`;
const MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const two = x => String(x).padStart(2, '0');
// The crawls ran after British Summer Time ended (25 Oct 2026), so UK time is UTC.
const hm = iso => { const d = new Date(iso); return `${two(d.getUTCHours())}:${two(d.getUTCMinutes())}`; };
const day = iso => { const d = new Date(iso); return `${d.getUTCDate()} ${MON[d.getUTCMonth()]}`; };
const dayTime = iso => `${day(iso)}, ${hm(iso)}`;
const fullTime = iso => { const d = new Date(iso); return `${d.getUTCDate()} ${MON[d.getUTCMonth()]} ${d.getUTCFullYear()}, ${hm(iso)}:${two(d.getUTCSeconds())}`; };
const duration = ms => {
  const s = Math.round(ms / 1000), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return h ? `${h} h ${m} min` : m ? `${m} min ${sec} s` : `${sec} s`;
};
const plural = (k, one, many) => `${n(k)} ${k === 1 ? one : many}`;

// ---------- Components ----------
const ic = (id, cls = '') => `<svg class="icon${cls ? ` ${cls}` : ''}" aria-hidden="true"><use href="#i-${id}"/></svg>`;
const sevIcon = s => `<svg class="sev-icon sev-${s}" aria-hidden="true"><use href="#sev-${s}"/></svg>`;
const sevMark = s => `<span class="sev sev-${s}">${sevIcon(s)}${WORD[s].one}</span>`;
const sevCount = (s, k, unit) => {
  const words = unit === 'checks'
    ? `${k === 1 ? 'check' : 'checks'} found ${WORD[s].lmany}`
    : k === 1 ? WORD[s].lone : WORD[s].lmany;
  return `<span class="sevcount sev-${s}${k === 0 ? ' zero' : ''}" title="${n(k)} ${words}">${sevIcon(s)}<span class="n">${n(k)}</span><span class="sr-only"> ${words}</span></span>`;
};
const sevCounts = (o, unit, { skipZero = false } = {}) =>
  `<span class="sevcounts">${SEVS.filter(s => !skipZero || o[s]).map(s => sevCount(s, o[s] ?? 0, unit)).join('')}</span>`;
const statusClass = c => (c == null ? 'none' : `${String(c)[0]}xx`);
const REASON = { 200: 'OK', 301: 'Moved Permanently', 302: 'Found', 403: 'Forbidden', 404: 'Not Found' };
const code = (c, words = false) => `<span class="code code-${statusClass(c)}">${c ?? 'No response'}${words && REASON[c] ? ` ${REASON[c]}` : ''}</span>`;
const fav = (letter, cls = '') => `<span class="fav${cls ? ` ${cls}` : ''}" aria-hidden="true">${esc(letter)}</span>`;
const midTrunc = (s, max) => {
  if (s.length <= max) return s;
  const keep = max - 1, head = Math.ceil(keep * 0.42);
  return `${s.slice(0, head)}…${s.slice(s.length - (keep - head))}`;
};
const pathOf = u => (u.startsWith(ORIGIN) ? u.slice(ORIGIN.length) || '/' : null);
// Internal URLs show their path after the site's favicon; external URLs show in full. Hover and copy give the full URL.
const urlCell = (u, max = 44) => {
  const p = pathOf(u);
  const letter = p ? 'G' : new URL(u).hostname.replace(/^www\./, '')[0].toUpperCase();
  return `<span class="url-cell">${fav(letter)}<span class="url" title="${esc(u)}">${esc(midTrunc(p ?? u, max))}</span></span>`;
};
const figLink = (text, href = 'urls.html') => `<a class="fig-link" href="${href}">${text}</a>`;

// ---------- The shell ----------
function doc({ title, body }) {
  return `<!doctype html>
<html lang="en-GB">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:opsz,wght@14..32,400..700&amp;family=JetBrains+Mono:wght@400;500;600&amp;display=swap">
<link rel="stylesheet" href="tokens.css">
<link rel="stylesheet" href="guide.css">
<script>try{var t=localStorage.getItem('si-guide-theme');if(t==='light'||t==='dark')document.documentElement.setAttribute('data-theme',t)}catch(e){}</script>
<script src="guide.js" defer></script>
</head>
<body>
<a class="skip-link" href="#main">Skip to content</a>
${sprite()}
${body}
</body>
</html>
`;
}

const KESTREL = { site: 'kestrel-outdoor.example', crawled: 2051, limit: 5000, rate: 14 };

function sidebar({ current, mode }) {
  const item = (id, label, icon, href, extra = '') =>
    `<a class="nav-item" href="${href}"${current === id ? ' aria-current="page"' : ''}>${ic(icon)}<span>${label}</span>${extra}</a>`;
  const count = k => `<span class="nav-count">${n(k)}</span>`;
  const isLive = mode === 'live';
  const snap = live.snapshots[live.markers['60%'].index];
  const sevRows = isLive
    ? `<p class="nav-pending">Checked when the crawl ends</p>`
    : `<div class="nav-sev" role="group" aria-label="Checks with issues, by severity">
${SEVS.map(s => `        <a href="issues.html">${sevIcon(s)}<span class="sev-${s}">${WORD[s].many}</span><span class="nav-count">${checksBySev[s].length}</span></a>`).join('\n')}
      </div>`;
  return `<nav class="sidebar" aria-label="Main">
    <div class="brand">
      <img src="../../assets/logo.png" alt="">
      <span class="brand-name">Shouting Iguana</span>
      <button class="btn btn-quiet btn-icon btn-sm" type="button" aria-label="Collapse the sidebar">${ic('panel')}</button>
    </div>
    <div class="nav">
      ${item('home', 'Home', 'home', 'home.html')}
    </div>
    <section class="nav-crawl" aria-label="Open crawl">
      <a class="crawl-card" href="${isLive ? 'live.html' : 'overview.html'}">
        ${fav('G', 'fav-lg')}
        <span class="crawl-card-text">
          <span class="crawl-site">${SITE}</span>
          <span class="crawl-when">${dayTime(garden.startedAt)}${isLive ? ', running' : ''}</span>
        </span>
      </a>
      <div class="nav">
        ${item('overview', 'Overview', 'overview', isLive ? 'live.html' : 'overview.html', isLive ? '<span class="nav-count"><span class="live-dot" aria-hidden="true"></span></span>' : '')}
        ${item('issues', 'Issues', 'issues', isLive ? '#' : 'issues.html', isLive ? '' : count(checksWithIssues.length))}
        ${sevRows}
        ${item('urls', 'URLs', 'urls', 'urls.html', count(isLive ? snap.fetched : garden.urlCount))}
        ${item('links', 'Links', 'links', '#', isLive ? '' : count(stats.linkCount))}
        ${item('sitemaps', 'Sitemaps', 'sitemaps', '#', '<span class="nav-note">None found</span>')}
        ${item('robots', 'robots.txt', 'robots', '#')}
        ${item('crawl-settings', 'Crawl settings', 'sliders', '#')}
      </div>
    </section>
    <section class="nav-running" aria-label="Crawling in the background">
      <h2 class="nav-heading"><span class="live-dot" aria-hidden="true"></span>Crawling in the background</h2>
      <a class="run-link" href="#">
        ${fav('K')}<span class="run-site">${KESTREL.site}</span><span class="run-pct">${pct(KESTREL.crawled, KESTREL.limit)}</span>
        <span class="meter" style="--p: ${pct(KESTREL.crawled, KESTREL.limit)}"><i></i></span>
      </a>
    </section>
    <div class="nav-foot">
      ${item('settings', 'Settings', 'settings', '#')}
      <button class="btn btn-quiet btn-icon btn-sm" type="button" data-theme-toggle aria-label="Theme: follows the system. Change theme">${ic('monitor')}</button>
    </div>
  </nav>`;
}

function topbar({ section, pill, controls }) {
  const crumbs = section === 'Home'
    ? `<span aria-current="page">Home</span>`
    : `<a class="crumb-site" href="overview.html">${fav('G')}${SITE}</a>
        ${ic('chevron-right', 'sep')}
        <a class="crumb-dim" href="overview.html" title="${fullTime(garden.startedAt)}">${dayTime(garden.startedAt)}</a>
        ${ic('chevron-right', 'sep')}
        <span aria-current="page">${section}</span>`;
  return `<header class="topbar">
      <div class="history">
        <button class="btn btn-quiet btn-icon" type="button" aria-label="Back (Alt+Left)" title="Back (Alt+Left)">${ic('chevron-left')}</button>
        <button class="btn btn-quiet btn-icon" type="button" aria-label="Forward (Alt+Right)" title="Forward (Alt+Right)" disabled>${ic('chevron-right')}</button>
      </div>
      <nav class="crumbs" aria-label="You are here">
        ${crumbs}
      </nav>
      ${pill ?? ''}
      ${controls ? `<div class="controls">${controls}</div>` : ''}
      <span class="spacer"></span>
      <button class="search" type="button" aria-label="Search or jump to (Ctrl+K)">${ic('search')}<span>Search or jump to…</span><kbd>Ctrl K</kbd></button>
    </header>`;
}

const donePill = `<span class="pill" role="status">${ic('check')}Done<span class="pill-meta">${n(garden.urlCount)} URLs</span></span>`;
const doneControls = `<button class="btn btn-sm" type="button" title="Crawl this site again with the same settings">${ic('again')}Crawl again</button>`;

function shell({ title, current, mode = 'done', section, pill = donePill, controls = doneControls, main }) {
  return doc({
    title,
    body: `<div class="shell">
  ${sidebar({ current, mode })}
  ${topbar({ section, pill, controls })}
  <main id="main" class="main">
${main}
  </main>
</div>`,
  });
}

// ---------- Home ----------
function home() {
  const speed = defaults.speedPresets.find(p => p.concurrency === defaults.options.concurrency
    && p.maxRequestsPerSecond === defaults.options.maxRequestsPerSecond);
  const speedName = speed.id[0].toUpperCase() + speed.id.slice(1);
  const rate = defaults.options.maxRequestsPerSecond ? `up to ${defaults.options.maxRequestsPerSecond} requests a second` : 'no rate limit';
  const robots = defaults.options.respectRobotsTxt ? 'robots.txt respected' : 'robots.txt ignored';
  const limit = defaults.options.maxUrls ? `up to ${n(defaults.options.maxUrls)} URLs` : 'no URL limit';

  const gardenDur = duration(new Date(garden.finishedAt) - new Date(garden.startedAt));
  const synthDur = duration(new Date(synthetic.finishedAt) - new Date(synthetic.startedAt));
  const state = (icon, word) => `<span class="state">${ic(icon)}${word}</span>`;
  const actions = (primary, href = '#') => `<div class="row-actions"><a class="btn btn-sm" href="${href}">${primary}</a><button class="btn btn-sm btn-icon btn-quiet" type="button" aria-label="More actions: continue, crawl again, analyse again, show in folder, remove, delete">${ic('more')}</button></div>`;

  const sites = [
    { letter: 'K', name: KESTREL.site, href: '#', when: 'Today, 17:27', exact: '1 Nov 2026, 17:27', status: `<span class="state"><span class="live-dot" aria-hidden="true"></span>Running</span>`,
      urls: KESTREL.crawled, pages: 1388, issues: null, why: 'Standard speed, up to 5,000 URLs. Checked when the crawl ends.', primary: 'Open',
      older: [{ when: '18 Oct, 10:05', status: state('check', 'Done'), urls: 4862, pages: 3120, issues: { error: 1, warning: 5, notice: 9 } }] },
    { letter: '1', name: synthetic.site, href: '#', when: `Today, ${hm(synthetic.startedAt)}`, exact: fullTime(synthetic.startedAt), status: state('check', 'Done'),
      urls: synthetic.urlCount, pages: synthetic.pageCount, issues: synthetic.issues,
      why: `The synthetic test site, crawled in ${synthDur}. ${plural(synthAnalysis.skipped.length, 'check was', 'checks were')} skipped.`, primary: 'Open' },
    { letter: 'G', name: SITE, href: 'overview.html', when: `Yesterday, ${hm(garden.startedAt)}`, exact: fullTime(garden.startedAt), status: state('check', 'Done'),
      urls: garden.urlCount, pages: garden.pageCount, issues: garden.issues,
      why: `Polite speed, ${gardenDur}. No sitemap found, so ${plural(skippedIds.length, 'check was', 'checks were')} skipped.`, primary: 'Open' },
    { letter: 'M', name: 'millbrook-tiles.example', href: '#', when: '30 Oct, 09:41', exact: '30 Oct 2026, 09:41', status: `${state('stop', 'Stopped')}<span class="tag">Partial</span>`,
      urls: 3140, pages: 2877, issues: { error: 2, warning: 7, notice: 10 },
      why: 'You stopped it after 41 min. Continue picks up where it left off; until then the issues cover only what was crawled.', primary: 'Continue' },
    { letter: 'N', name: 'northvale-cycles.example', href: '#', when: '27 Oct, 11:02', exact: '27 Oct 2026, 11:02', status: `${state('check', 'Done')}<span class="tag">Out of date</span>`,
      urls: 1982, pages: 412, issues: { error: 3, warning: 9, notice: 14 },
      why: 'The analysis limits changed after it was analysed. Analyse again to bring its issues up to date.', primary: 'Analyse again', expanded: true,
      older: [
        { when: '13 Oct, 10:47', status: state('check', 'Done'), urls: 1964, pages: 409, issues: { error: 5, warning: 11, notice: 14 } },
        { when: '29 Sep, 16:20', status: state('check', 'Done'), urls: 1931, pages: 398, issues: { error: 6, warning: 12, notice: 15 } },
      ] },
    { letter: 'F', name: 'fernwood-legal.example', href: '#', when: '22 Oct, 15:20', exact: '22 Oct 2026, 15:20', status: state('check', 'Done'),
      urls: 846, pages: 188, issues: { error: 0, warning: 4, notice: 9 }, why: 'Fast speed, 3 min 10 s.', primary: 'Open',
      older: [
        { when: '8 Oct, 14:02', status: state('check', 'Done'), urls: 839, pages: 186, issues: { error: 1, warning: 4, notice: 10 } },
        { when: '24 Sep, 09:15', status: state('check', 'Done'), urls: 822, pages: 181, issues: { error: 1, warning: 6, notice: 10 } },
        { when: '10 Sep, 11:38', status: state('check', 'Done'), urls: 790, pages: 176, issues: { error: 2, warning: 6, notice: 11 } },
      ] },
  ];
  const crawlCount = sites.reduce((a, s) => a + 1 + (s.older?.length ?? 0), 0);

  const rows = sites.map((s, i) => {
    const gid = `older-${i}`;
    const older = s.older ? `
      <tr class="older-row"><td colspan="7"><button class="older-toggle" type="button" aria-expanded="${s.expanded ? 'true' : 'false'}" aria-controls="${gid}">${ic('chevron-down')}${plural(s.older.length, 'older crawl', 'older crawls')}</button></td></tr>
${s.older.map(o => `      <tr class="old-crawl" data-group="${gid}"${s.expanded ? '' : ' hidden'}>
        <td><span class="when">${o.when}</span></td><td></td><td>${o.status}</td>
        <td class="num">${n(o.urls)}</td><td class="num">${n(o.pages)}</td><td>${sevCounts(o.issues, 'checks')}</td>
        <td>${actions('Open')}</td>
      </tr>`).join('\n')}` : '';
    return `    <tbody>
      <tr class="site-row">
        <td><div class="site-cell">${fav(s.letter)}<div><a class="site-name" href="${s.href}">${esc(s.name)}</a><div class="site-why">${esc(s.why)}</div></div></div></td>
        <td><span class="when" title="${s.exact}">${s.when}</span></td>
        <td>${s.status}</td>
        <td class="num">${n(s.urls)}</td>
        <td class="num">${n(s.pages)}</td>
        <td>${s.issues ? sevCounts(s.issues, 'checks') : '<span class="muted">After the crawl</span>'}</td>
        <td>${actions(s.primary, s.href)}</td>
      </tr>${older}
    </tbody>`;
  }).join('\n');

  const kestrelEtaS = Math.round((KESTREL.limit - KESTREL.crawled) / KESTREL.rate);
  const main = `    <div class="home">
      <div class="home-inner">
        <section aria-labelledby="start-title">
          <h1 id="start-title" class="page-title">Start a crawl</h1>
          <form class="url-form" action="live.html">
            <label class="url-field">${ic('globe')}<span class="sr-only">The site to crawl</span><input type="url" name="url" placeholder="Paste a site’s address, such as https://www.example.com" autocomplete="off" spellcheck="false"></label>
            <button class="btn btn-primary btn-lg" type="submit">Crawl</button>
            <a class="btn btn-lg" href="#">${ic('sliders')}Settings…</a>
          </form>
          <p class="hint">Enter crawls with your defaults: <b>${speedName}</b> speed, ${defaults.options.concurrency} requests at a time with ${rate}, ${robots}, ${limit}. <a href="#">Change your defaults</a></p>
        </section>

        <section class="home-sec" aria-labelledby="now-title">
          <div class="sec-row"><h2 id="now-title" class="sec-title">Crawling now</h2><span class="sec-note">It carries on while you open other crawls</span></div>
          <article class="run-card" aria-label="${KESTREL.site}, running">
            ${fav('K', 'fav-lg')}
            <div>
              <div class="run-title"><a href="#">${KESTREL.site}</a><span class="pill pill-running"><span class="live-dot" aria-hidden="true"></span>Running</span></div>
              <div class="run-sub">Started today at 17:27. Standard speed, up to ${n(KESTREL.limit)} URLs.</div>
            </div>
            <div class="run-actions">
              <button class="btn btn-sm" type="button" title="Pause (F7)">${ic('pause')}Pause</button>
              <button class="btn btn-sm" type="button" title="Stop (F8)">${ic('stop')}Stop</button>
              <a class="btn btn-sm" href="#">Open</a>
            </div>
            <div class="run-progress">
              <div class="bar" role="progressbar" aria-label="URLs crawled, of the limit" aria-valuemin="0" aria-valuemax="${KESTREL.limit}" aria-valuenow="${KESTREL.crawled}" style="--p: ${pct(KESTREL.crawled, KESTREL.limit)}"><i></i></div>
              <div class="run-figures"><span><b>${n(KESTREL.crawled)}</b> of ${n(KESTREL.limit)} URLs</span><span><b>${KESTREL.rate}</b> URLs a second</span><span>About <b>${Math.ceil(kestrelEtaS / 60)} min</b> left</span></div>
            </div>
          </article>
        </section>

        <section class="home-sec" aria-labelledby="lib-title">
          <div class="sec-row">
            <h2 id="lib-title" class="sec-title">Library</h2><span class="sec-note">${sites.length} sites, ${crawlCount} crawls</span>
            <div class="lib-tools">
              <span class="drop-hint">${ic('folder')}Open a .sicrawl file with Ctrl+O, or drop it here</span>
              <label class="field">${ic('search')}<span class="sr-only">Search the library</span><input type="search" placeholder="Search sites and crawls"></label>
            </div>
          </div>
          <table class="lib-table">
            <colgroup><col style="width: 33%"><col style="width: 12%"><col style="width: 12%"><col style="width: 8%"><col style="width: 8%"><col style="width: 17%"><col style="width: 10%"></colgroup>
            <thead><tr><th scope="col">Site, with its latest crawl</th><th scope="col">Crawled</th><th scope="col">Status</th><th scope="col" class="num">URLs</th><th scope="col" class="num">Pages</th><th scope="col">Checks with issues</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
${rows}
          </table>
        </section>
      </div>
    </div>`;
  return shell({ title: 'Home – Shouting Iguana', current: 'home', section: 'Home', pill: '', controls: '', main });
}

// ---------- Live: the Overview while the crawl runs ----------
const LIVE_IDX = live.markers['60%'].index;
const snap = live.snapshots[LIVE_IDX];
const atMs = new Date(snap.at).getTime();

function liveSplit() {
  // Which fetched URLs had which code at this moment, and whether they're internal, from the URL rows.
  const done = urls.rows.filter(r => new Date(r.crawledAt).getTime() + (r.responseMs ?? 0) <= atMs);
  const by = (pred) => done.filter(pred);
  return {
    fetched: done.length,
    r3: by(r => r.statusClass === '3xx'), r4: by(r => r.statusClass === '4xx'), none: by(r => r.statusClass === 'noResponse'),
  };
}

function splitFigure({ total, c2, c3, c4, c5, none, rows3, rows4, rowsNone, title }) {
  const other = c3 + c4 + c5 + none;
  const start = (c2 / total) * 100;
  const segs = [
    ['3xx', c3, rows3], ['4xx', c4, rows4], ['5xx', c5, []], ['none', none, rowsNone],
  ];
  const describe = (rows) => {
    const byCode = {};
    for (const r of rows) {
      const key = `${r.statusCode ?? 'none'}|${r.isInternal ? 'internal' : 'external'}`;
      byCode[key] = (byCode[key] ?? 0) + 1;
    }
    return Object.entries(byCode).map(([k, v]) => {
      const [c, where] = k.split('|');
      if (c === 'none') return `${n(v)} ${where}, disallowed by their robots.txt`;
      return `${c} ${REASON[c] ?? ''} × ${n(v)}, ${where}`;
    }).join('; ');
  };
  const label = { '3xx': '3xx', '4xx': '4xx', '5xx': '5xx', none: 'No response' };
  const keyRows = [
    `<tr><td><span class="swatch fill-2xx"></span></td><td class="k">2xx</td><td class="d">200 OK × ${n(c2)}</td><td class="num"><a href="urls.html">${n(c2)}</a></td><td class="pct">${((c2 / total) * 100).toFixed(1)}%</td></tr>`,
    ...segs.map(([k, v, rows]) => `<tr><td><span class="swatch fill-${k}"></span></td><td class="k">${label[k]}</td><td class="d">${v ? describe(rows) : 'None'}</td><td class="num">${v ? `<a href="urls.html">${n(v)}</a>` : '<span class="muted">0</span>'}</td><td class="pct">${v ? `${((v / total) * 100).toFixed(1)}%` : ''}</td></tr>`),
  ].join('\n');
  return `<figure class="figure" aria-labelledby="split-title">
            <div class="fig-head"><h2 id="split-title" class="sec-title">${title}</h2><span class="sec-note">${n(total)} URLs</span></div>
            <div class="split" role="img" aria-label="${n(c2)} of ${n(total)} answered 2xx. The other ${other}, magnified below: ${c3} 3xx, ${c4} 4xx, ${c5} 5xx, ${none} no response.">
              <div class="stackbar"><a class="fill-2xx" style="flex: ${c2}" href="urls.html" aria-label="2xx: ${n(c2)}"></a><a class="fill-other" style="flex: ${other}" href="urls.html" aria-label="Not 2xx: ${other}"></a></div>
              <svg class="split-zoom" viewBox="0 0 100 32" preserveAspectRatio="none" aria-hidden="true"><path d="M${start.toFixed(2)} 0 L100 0 L100 32 L0 32 Z"/><line x1="${start.toFixed(2)}" y1="0" x2="0" y2="32"/><line x1="100" y1="0" x2="100" y2="32"/></svg>
              <div class="stackbar">${segs.filter(([, v]) => v).map(([k, v]) => `<a class="fill-${k}" style="flex: ${v}" href="urls.html" aria-label="${label[k]}: ${v}"></a>`).join('')}</div>
              <div class="split-ends"><span>The <b>${other}</b> that didn’t answer 2xx, magnified</span><span><b>${((other / total) * 100).toFixed(1)}%</b> of all</span></div>
            </div>
            <table class="keytable">
${keyRows}
            </table>
          </figure>`;
}

function livePage() {
  const s = snap;
  const split = liveSplit();
  if (split.fetched !== s.fetched) console.warn(`live split: ${split.fetched} rows done vs ${s.fetched} fetched (approximate, see data README)`);
  const sc = s.statusClasses;
  // URLs per second, in 5 s buckets from the start to now.
  const pts = [];
  for (let i = 0; i <= LIVE_IDX; i += 5) {
    const seg = live.snapshots.slice(i, Math.min(i + 5, LIVE_IDX + 1));
    pts.push(seg.reduce((a, x) => a + x.pagesPerSecond, 0) / seg.length);
  }
  const cap = options.maxRequestsPerSecond;
  const yMax = Math.max(cap * 1.5, Math.ceil(Math.max(...pts) * 1.15));
  const midIso = new Date(new Date(live.startedAt).getTime() + s.elapsedMs / 2).toISOString();
  const xy = pts.map((v, i) => [(i / (pts.length - 1)) * 1000, 100 - (v / yMax) * 100]);
  const line = xy.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ');
  const area = `M0 100 ${xy.map(([x, y]) => `L${x.toFixed(1)} ${y.toFixed(1)}`).join(' ')} L1000 100 Z`;
  const capY = 100 - (cap / yMax) * 100;
  const lastY = xy[xy.length - 1][1];
  const found = s.discovered, crawled = s.fetched, queued = s.queued, skipped = s.skipped, inFlight = s.inFlight;

  const main = `    <div class="scroll">
      <div class="ov-inner">
        <div class="ov-head">
          <div>
            <h1 class="page-title">Overview</h1>
            <p class="lede">Crawling at the <b>Polite</b> pace: ${options.concurrency} at a time, up to ${options.maxRequestsPerSecond} requests a second. ${figLink(n(crawled))} URLs crawled so far, and ${figLink(n(queued))} are queued.</p>
          </div>
          <span class="live-tag"><span class="live-dot" aria-hidden="true"></span>Live</span>
        </div>

        <div class="ledger" aria-live="off">
          <div class="ledger-item"><div class="ledger-label">Crawled</div><a class="ledger-value" href="urls.html">${n(crawled)}</a><div class="ledger-sub">URLs fetched</div></div>
          <div class="ledger-item"><div class="ledger-label">Found</div><a class="ledger-value" href="urls.html">${n(found)}</a><div class="ledger-sub">URLs discovered</div></div>
          <div class="ledger-item"><div class="ledger-label">Queued</div><a class="ledger-value" href="urls.html">${n(queued)}</a><div class="ledger-sub">to be fetched</div></div>
          <div class="ledger-item"><div class="ledger-label">Pages</div><a class="ledger-value" href="urls.html">${n(s.pages)}</a><div class="ledger-sub">internal HTML</div></div>
          <div class="ledger-item"><div class="ledger-label">Rate</div><span class="ledger-value">${s.pagesPerSecond.toFixed(1)}<span class="unit"> a second</span></span><div class="ledger-sub">URLs, last 5 s</div></div>
          <div class="ledger-item"><div class="ledger-label">Elapsed</div><span class="ledger-value" title="Started ${fullTime(live.startedAt)}">${duration(s.elapsedMs).replace(/ s$/, '<span class="unit"> s</span>').replace(' min ', '<span class="unit"> min </span>')}</span><div class="ledger-sub">since ${hm(live.startedAt)}</div></div>
          <div class="ledger-item"><div class="ledger-label">Time left</div><span class="ledger-value unknown">Not known yet</span><div class="ledger-sub">still finding URLs</div></div>
        </div>

        <div class="ov-cols">
          <div class="ov-col">
            <figure class="figure" aria-labelledby="found-title">
              <div class="fig-head"><h2 id="found-title" class="sec-title">Where the ${n(found)} found URLs are</h2><span class="sec-note">Each part opens its URLs</span></div>
              <div class="stackbar" role="img" aria-label="${n(crawled)} crawled, ${n(queued)} queued, ${n(skipped)} skipped, ${inFlight} in flight">
                <a class="fill-crawled" style="flex: ${crawled}" href="urls.html" aria-label="Crawled: ${n(crawled)}"></a>
                <a class="fill-queued" style="flex: ${queued}" href="urls.html" aria-label="Queued: ${n(queued)}"></a>
                <a class="fill-skipped" style="flex: ${skipped}" href="urls.html" aria-label="Skipped: ${n(skipped)}"></a>
              </div>
              <table class="keytable">
                <tr><td><span class="swatch fill-crawled"></span></td><td class="k">Crawled</td><td class="d">Fetched and recorded</td><td class="num"><a href="urls.html">${n(crawled)}</a></td><td class="pct">${pct(crawled, found)}</td></tr>
                <tr><td><span class="swatch fill-queued"></span></td><td class="k">Queued</td><td class="d">Found, waiting their turn</td><td class="num"><a href="urls.html">${n(queued)}</a></td><td class="pct">${pct(queued, found)}</td></tr>
                <tr><td><span class="swatch fill-skipped"></span></td><td class="k">Skipped</td><td class="d">Found only through nofollow links, which this crawl doesn’t follow</td><td class="num"><a href="urls.html">${n(skipped)}</a></td><td class="pct">${pct(skipped, found)}</td></tr>
                <tr><td></td><td class="k">In flight</td><td class="d">Being fetched right now</td><td class="num">${n(inFlight)}</td><td class="pct"></td></tr>
              </table>
            </figure>

            <figure class="figure" aria-labelledby="rate-title">
              <div class="fig-head"><h2 id="rate-title" class="sec-title">URLs a second</h2><span class="sec-note">Since the start, in 5-second steps</span></div>
              <div class="spark" role="img" aria-label="The rate climbed to ${cap} a second in the first minute and has held at the cap since.">
                <svg viewBox="0 0 1000 100" preserveAspectRatio="none" aria-hidden="true">
                  <line class="base" x1="0" y1="100" x2="1000" y2="100"/>
                  <line class="cap" x1="0" y1="${capY.toFixed(1)}" x2="1000" y2="${capY.toFixed(1)}"/>
                  <path class="area" d="${area}"/>
                  <path class="line" d="${line}"/>
                </svg>
                <span class="spark-label" style="top: ${capY.toFixed(1)}%">The Polite cap: ${cap} a second</span>
                <span class="spark-now" style="top: ${lastY.toFixed(1)}%"></span>
              </div>
              <div class="spark-axis"><span>${hm(live.startedAt)}</span><span>${hm(midIso)}</span><span>Now, ${hm(s.at)}</span></div>
              <p class="caption">Steady at the cap since the first minute. Polite sends at most ${cap} requests a second, so the site is never rushed. Standard and Fast have no cap.</p>
            </figure>
          </div>

          <div class="ov-col">
            <section aria-labelledby="alerts-title">
              <div class="fig-head" style="margin-bottom: var(--space-3)"><h2 id="alerts-title" class="sec-title">Alerts</h2></div>
              <div class="panel empty-note">
                ${ic('shield')}
                <div>
                  <p class="strong">No alerts so far</p>
                  <p class="caption">Nothing has blocked or slowed the crawl. If the site starts refusing requests, stops answering, or its robots.txt can’t be read, it shows here, with what to do about it.</p>
                </div>
              </div>
            </section>

            ${splitFigure({ total: crawled, c2: sc['2xx'], c3: sc['3xx'], c4: sc['4xx'], c5: sc['5xx'], none: sc.noResponse,
              rows3: split.r3, rows4: split.r4, rowsNone: split.none, title: 'Status codes so far' })}

            <section aria-labelledby="next-title">
              <div class="fig-head"><h2 id="next-title" class="sec-title">When the crawl ends</h2></div>
              <p class="caption">The analysis runs all ${catalog.checks.length} checks on its own, then this page becomes the answer: issues by severity and what to fix first. You can work in URLs while it crawls; the counts keep up.</p>
            </section>

            <section aria-labelledby="set-title">
              <div class="fig-head"><h2 id="set-title" class="sec-title">This crawl’s settings</h2><a class="sec-note" href="#">Crawl settings</a></div>
              <dl class="kv">
                <dt>Speed</dt><dd>Polite: ${options.concurrency} at a time, up to ${options.maxRequestsPerSecond} requests a second</dd>
                <dt>Limit</dt><dd>Up to ${n(options.maxUrls)} URLs</dd>
                <dt>robots.txt</dt><dd>${options.respectRobotsTxt ? 'Respected' : 'Ignored'}</dd>
                <dt>Nofollow links</dt><dd>${options.followNofollow ? 'Followed' : 'Not followed'}</dd>
                <dt>Resources</dt><dd>${options.checkResources ? 'Checked: images, scripts and stylesheets' : 'Not checked'}</dd>
                <dt>HTML</dt><dd>${options.storeHtml ? 'Stored, for the source view and issue evidence' : 'Not stored'}</dd>
              </dl>
            </section>
          </div>
        </div>
      </div>
    </div>`;

  const pill = `<span class="pill pill-running" role="status"><span class="live-dot" aria-hidden="true"></span>Running<span class="pill-meta">${n(crawled)} URLs</span></span>`;
  const controls = `<button class="btn btn-sm" type="button" title="Pause (F7)">${ic('pause')}Pause</button><button class="btn btn-sm" type="button" title="Stop (F8)">${ic('stop')}Stop</button>`;
  return shell({ title: `Overview, crawling – ${SITE}`, current: 'overview', mode: 'live', section: 'Overview', pill, controls, main });
}

// ---------- Overview, done: the answer ----------
function detailSummary(id) {
  const rows = issueRows(id);
  const by = {};
  for (const r of rows) if (r.detail) by[r.detail] = (by[r.detail] ?? 0) + 1;
  const top = Object.entries(by).sort((a, b) => b[1] - a[1])[0];
  if (!top) return '';
  if (top[1] === rows.length) return `Detail on all ${n(rows.length)}: ${top[0]}`;
  if (top[1] > 1) return `Most often: ${top[0]} (${n(top[1])} of ${n(rows.length)})`;
  return `For example: ${top[0]}`;
}
const unitFor = c => (c.category === 'responseCodes' ? ['URL', 'URLs'] : ['page', 'pages']);

function overviewPage() {
  const ranked = [...checksWithIssues].sort((a, b) => SEVS.indexOf(a.severity) - SEVS.indexOf(b.severity) || countById[b.id] - countById[a.id]);
  const fixes = ranked.filter(c => c.severity !== 'notice');
  const notices = ranked.filter(c => c.severity === 'notice');
  const errs = checksBySev.error;

  const fixRows = fixes.map((c, i) => {
    const [one, many] = unitFor(c);
    const sum = detailSummary(c.id);
    return `            <li><a class="fix" href="issues.html">
              <span class="fix-rank">${i + 1}</span>
              <span class="fix-title">${sevIcon(c.severity)}<span class="sr-only">${WORD[c.severity].one}: </span><span class="t">${esc(c.title)}</span></span>
              <span class="fix-n"><b>${n(countById[c.id])}</b> ${countById[c.id] === 1 ? one : many}</span>
              <span class="fix-why">${esc(c.description)}${sum ? ` <span class="muted">${esc(sum)}.</span>` : ''}</span>
            </a></li>`;
  }).join('\n');

  // One note can explain several skipped checks: show each note once, with the checks it covers.
  const byNote = new Map();
  for (const id of skippedIds) byNote.set(skipNote(id), [...(byNote.get(skipNote(id)) ?? []), checkById[id].title]);
  const skipRows = [...byNote].map(([note, titles]) => `              <div class="skip">${sevIcon('skipped')}<span class="skip-title">${titles.map(esc).join(', and ')}</span><span class="skip-why">${esc(note)}</span></div>`).join('\n');

  const pages = urls.rows.filter(r => r.isPage);
  const depth = {};
  for (const p of pages) depth[p.depth] = (depth[p.depth] ?? 0) + 1;
  const depthKeys = Object.keys(depth).map(Number).sort((a, b) => a - b);
  const maxDepth = Math.max(...Object.values(depth));
  const times = pages.map(p => p.responseMs).sort((a, b) => a - b);
  const median = times.length % 2 ? times[(times.length - 1) / 2] : Math.round((times[times.length / 2 - 1] + times[times.length / 2]) / 2);
  const slow = times.filter(t => t > 1000).length;
  const all = urls.rows;
  const cls = k => all.filter(r => r.statusClass === k);
  const nonIdx = pages.filter(p => !p.indexable);

  const main = `    <div class="scroll">
      <div class="ov-inner">
        <div class="ov-head">
          <div>
            <h1 class="page-title">Overview</h1>
            <p class="lede"><b>${checksWithIssues.length} checks</b> found issues on ${SITE}. Fix the ${errs.length} errors first: ${errs.map(c => `${figLink(esc(c.title), 'issues.html')} on ${plural(countById[c.id], ...unitFor(c))}`).join(', and ')}.</p>
            <div class="meta-row" style="margin-top: var(--space-3)"><span class="meta-item">${ic('analyse')}Analysed ${dayTime(analysis.analyzedAt)}, by engine ${analysis.engineVersion}</span><span class="meta-item">${ic('check')}${ranCount} of ${catalog.checks.length} checks ran on ${n(analysis.pagesAnalyzed)} pages</span></div>
          </div>
          <div class="report-actions"><button class="btn btn-sm" type="button" title="Export every check, with a summary sheet (Ctrl+E)">${ic('export')}Export</button></div>
        </div>

        <div class="sevstrip">
${SEVS.map(s => `          <a class="sevblock" href="issues.html"><span class="sev sev-${s}">${sevIcon(s)}${WORD[s].many}</span><span class="big">${checksBySev[s].length}<span class="unit"> ${checksBySev[s].length === 1 ? 'check' : 'checks'}</span></span><span class="sub">${plural(issuesBySev[s], 'issue', 'issues')}</span></a>`).join('\n')}
          <div class="sevblock rest">
            <div class="rest-line">${sevIcon('passed')}<span class="sev-passed sr-only">Passed:</span><span><b>${passedCount}</b> checks passed</span></div>
            <div class="rest-line">${sevIcon('skipped')}<span><b>${skippedIds.length}</b> skipped, with reasons</span></div>
            <p class="rest-line" style="margin-top: var(--space-3)">An issue is one check on one URL.</p>
          </div>
        </div>

        <div class="ov-cols">
          <div class="ov-col">
            <section aria-labelledby="fix-title">
              <div class="fig-head" style="margin-bottom: var(--space-3)"><h2 id="fix-title" class="sec-title">Fix first</h2><span class="sec-note">Errors, then warnings; the most pages first</span></div>
              <ol class="fixfirst">
${fixRows}
              </ol>
              <p class="fix-more">Then ${plural(notices.length, 'notice', 'notices')}, led by <a href="issues.html">${esc(notices[0].title)}</a> on ${n(countById[notices[0].id])} pages. <a href="issues.html">See every check</a></p>
            </section>
          </div>

          <div class="ov-col">
            <section aria-labelledby="skip-title" class="panel">
              <div class="fig-head" style="margin-bottom: var(--space-3)"><h2 id="skip-title" class="sec-title">Not checked on this crawl</h2></div>
              <div class="skips">
${skipRows}
              </div>
            </section>

            ${splitFigure({ total: garden.urlCount, c2: cls('2xx').length, c3: cls('3xx').length, c4: cls('4xx').length, c5: cls('5xx').length, none: cls('noResponse').length,
              rows3: cls('3xx'), rows4: cls('4xx'), rowsNone: cls('noResponse'), title: 'Status codes' })}

            <section aria-labelledby="idx-title">
              <div class="fig-head" style="margin-bottom: var(--space-2)"><h2 id="idx-title" class="sec-title">Indexability</h2></div>
              <p class="caption"><a class="fig-link" href="urls.html">${n(stats.indexablePageCount)} of ${n(stats.htmlPageCount)}</a> pages can appear in search results. The other ${nonIdx.length}, <span class="url">${esc(pathOf(nonIdx[0].url))}</span>, names <span class="url">${esc(pathOf(nonIdx[0].canonicalUrl))}</span> as its canonical.</p>
            </section>

            <section aria-labelledby="depth-title">
              <div class="fig-head" style="margin-bottom: var(--space-3)"><h2 id="depth-title" class="sec-title">Pages by depth</h2><span class="sec-note">Clicks from the start URL</span></div>
              <div class="cols" role="img" aria-label="${depthKeys.map(k => `${depth[k]} at depth ${k}`).join(', ')}">
${depthKeys.map(k => `                <a href="urls.html"><span class="colval">${n(depth[k])}</span><span class="colbar" style="height: ${Math.max(2, (depth[k] / maxDepth) * 72).toFixed(0)}%"></span></a>`).join('\n')}
              </div>
              <div class="cols-axis">${depthKeys.map(k => `<span>${k}</span>`).join('')}</div>
            </section>

            <section aria-labelledby="time-title">
              <div class="fig-head" style="margin-bottom: var(--space-3)"><h2 id="time-title" class="sec-title">Response times</h2><span class="sec-note">Pages</span></div>
              <div class="statline">
                <div class="stat"><span class="v">${n(Math.round(stats.averageResponseMs))} ms</span><span class="l">Average</span></div>
                <div class="stat"><span class="v">${n(median)} ms</span><span class="l">Median</span></div>
                <div class="stat"><a class="v fig-link" href="issues.html">${slow}</a><span class="l">Over 1 s</span></div>
              </div>
            </section>
          </div>
        </div>
      </div>
    </div>`;
  return shell({ title: `Overview – ${SITE}`, current: 'overview', section: 'Overview', main });
}

// ---------- Issues: the checks list and one check, explained ----------
const SELECTED = 'empty-anchor-text';

function checksList() {
  const cats = catalog.categories.map(cat => {
    const items = catalog.checks.filter(c => c.category === cat.id && (countById[c.id] > 0 || skippedIds.includes(c.id)));
    items.sort((a, b) => (skippedIds.includes(a.id) - skippedIds.includes(b.id)) || SEVS.indexOf(a.severity) - SEVS.indexOf(b.severity) || (countById[b.id] ?? 0) - (countById[a.id] ?? 0));
    return { cat, items };
  }).filter(x => x.items.length);
  return cats.map(({ cat, items }) => {
    return `          <li>
            <h2 class="cat-title"><span>${esc(cat.title)}</span></h2>
            <ul>
${items.map(c => {
      if (skippedIds.includes(c.id)) {
        return `              <li><a class="check check-skipped" href="#">${sevIcon('skipped')}<span class="t">${esc(c.title)}</span><span class="n">Skipped</span><span class="reason">${esc(shortNote(c.id))}</span></a></li>`;
      }
      const cur = c.id === SELECTED ? ' aria-current="true"' : '';
      return `              <li><a class="check" href="#"${cur}>${sevIcon(c.severity)}<span class="t"><span class="sr-only">${WORD[c.severity].one}: </span>${esc(c.title)}</span><span class="n">${n(countById[c.id])}</span></a></li>`;
    }).join('\n')}
            </ul>
          </li>`;
  }).join('\n');
}

function evidenceSnippet() {
  const src = JSON.parse(fs.readFileSync(path.join(FX, 'garden/inspector/category.json'), 'utf8'));
  const html = src.source.html;
  const open = '<a href="/" class="flex h-full justify-center items-center">';
  const at = html.indexOf(open);
  const end = html.indexOf('</a>', at);
  const el = html.slice(at, end + 4);
  if (at < 0 || /aria-label|<title|title=|alt=/.test(el) || el.replace(/<[^>]+>/g, '').trim()) throw new Error('evidence snippet changed');
  const svgOpen = el.match(/<svg[^>]*>/)[0];
  const pathStart = el.match(/<path fill="#fff" d="[^"]{0,18}/)[0];
  const paths = (el.match(/<path /g) ?? []).length;
  const t = s => esc(s).replace(/ ([a-zA-Z:-]+)=/g, ' <span class="at">$1</span>=').replace(/&lt;(\/?)([a-z]+)/g, '&lt;$1<span class="tg">$2</span>');
  // Whitespace is reformatted for the panel: one block per line, wrapped lines hang under their tag.
  const [aTag, aClass] = open.split(' class=');
  const [svgTag, svgRest] = svgOpen.replace(' xmlns="http://www.w3.org/2000/svg"', '').split(' viewBox=');
  const lines = [
    [0, `<mark>${t(aTag)}</mark>`], [3, `<mark>${t(` class=${aClass}`).trim()}</mark>`],
    [2, t(svgTag)], [7, t(` viewBox=${svgRest}`).trim()],
    [4, `${t(pathStart)}…"/&gt;`],
    [4, `<span class="at">… ${paths - 1} more paths</span>`],
    [2, t('</svg>')],
    [0, `<mark>${t('</a>')}</mark>`],
  ];
  return { code: lines.map(([i, l]) => `<span class="ln" style="--i: ${i}">${l}</span>`).join(''), page: src.summary.url, paths };
}

function issuesPage() {
  const c = checkById[SELECTED];
  const rows = issueRows(SELECTED);
  const total = rows.length;
  const groups = {};
  for (const r of rows) {
    const key = `${r.detail}|${r.relatedUrl}`;
    (groups[key] ??= { detail: r.detail, related: r.relatedUrl, rows: [] }).rows.push(r);
  }
  const g = Object.values(groups).sort((a, b) => b.rows.length - a.rows.length);
  const big = g[0], small = g[1];
  const smallPage = small.rows[0].url;
  const ev = evidenceSnippet();

  // Waffle: one square per affected page, in crawl order.
  const colsW = 25, cell = 8, gap = 3;
  const order = rows.map(r => r.url);
  const cells = order.map((u, i) => {
    const x = (i % colsW) * (cell + gap), y = Math.floor(i / colsW) * (cell + gap);
    const cls = u === smallPage ? 'g2' : 'g1';
    return `<rect class="${cls}" x="${x + 0.75}" y="${y + 0.75}" width="${cell - 1.5}" height="${cell - 1.5}" rx="1.5"/>`;
  }).join('');
  const wH = Math.ceil(order.length / colsW) * (cell + gap);
  const wW = colsW * (cell + gap);

  const childRows = (grp, id, shown) => grp.rows.slice(0, shown).map(r => `              <tr class="gchild" data-group="${id}"${id === 'grp-1' ? '' : ' hidden'}><td></td><td colspan="2">${urlCell(r.url, 64)}</td><td class="num"></td><td class="num"><a href="urls.html">Inspect</a></td></tr>`).join('\n');
  const shownBig = 7;
  const relCell = u => (u ? urlCell(u, 30) : '<span class="muted">None</span>');
  const shareCell = k => `<div class="share"><span class="track"><i style="--p: ${((k / total) * 100).toFixed(1)}%"></i></span><span class="pct">${((k / total) * 100).toFixed(1)}%</span></div>`;

  const main = `    <div class="issues">
      <aside class="checks" aria-label="Checks">
        <div class="checks-head">
          <div>
            <h1>Issues</h1>
            <p class="sub">${checksWithIssues.length} checks found issues on ${n(analysis.pagesAnalyzed)} pages. ${skippedIds.length} were skipped.</p>
          </div>
          <div class="sevfilter" role="group" aria-label="Show only one severity">
${SEVS.map(s => `            <button type="button" aria-pressed="false" title="Show only ${WORD[s].lmany}">${sevIcon(s)}<span class="w">${WORD[s].many}</span><span class="n">${checksBySev[s].length}</span></button>`).join('\n')}
          </div>
          <label class="field">${ic('search')}<span class="sr-only">Filter checks</span><input type="search" placeholder="Filter checks"></label>
          <div class="checks-opts">
            <label class="toggle"><input class="check-input" type="checkbox">Show the ${passedCount} passed checks</label>
            <span class="sec-note">By category</span>
          </div>
        </div>
        <ul class="checks-list">
${checksList()}
        </ul>
      </aside>

      <section class="report" aria-labelledby="check-title">
        <div class="report-inner">
          <div class="report-top">
            <div>
              <div class="crumb-cat">${esc(catTitle[c.category])}</div>
              <h1 id="check-title" class="page-title">${esc(c.title)}</h1>
            </div>
            <div class="report-actions">
              <button class="btn btn-sm" type="button" title="Copy this check’s URLs">${ic('copy')}Copy URLs</button>
              <button class="btn btn-sm" type="button" title="Export this check (Ctrl+E)">${ic('export')}Export</button>
              <button class="btn btn-sm btn-quiet" type="button" title="Hide this check, for this crawl or every crawl">${ic('hide')}Hide check</button>
            </div>
          </div>
          <div class="meta-row">
            <span class="sev-badge sev-notice">${sevIcon(c.severity)}${WORD[c.severity].one}</span>
            <span class="meta-item"><b class="num" style="color: var(--text)">${n(total)}</b>&nbsp;of ${n(analysis.pagesAnalyzed)} pages</span>
            <span class="meta-item">${plural(g.length, 'group', 'groups')} by detail</span>
            <span class="meta-item">${ic('analyse')}Analysed ${dayTime(analysis.analyzedAt)}</span>
          </div>
          <p class="lede">Every page crawled has an internal link without anchor text. On ${figLink(n(big.rows.length))} of them it’s the same link, to the home page, so it’s most likely part of the site’s template: fix it there once and it’s fixed on every page.</p>

          <div class="report-grid">
            <dl class="explain">
              <dt>What’s wrong</dt>
              <dd>${n(total)} of ${n(analysis.pagesAnalyzed)} pages have at least one internal link with nothing to read: no text, no alt text and no aria-label. On ${n(big.rows.length)} pages it’s one link to the home page, in the header. The home page has ${small.detail.replace('Links to ', '').replace('URLs', 'more')} like it, to other pages.</dd>
              <dt>Why it matters</dt>
              <dd>${esc(c.description)}</dd>
              <dt>How to fix</dt>
              <dd>${esc(c.fix)}</dd>
            </dl>

            <aside class="evidence panel" aria-label="Evidence from the crawl">
              <h2 class="evidence-title">The ${n(total)} pages, in crawl order</h2>
              <svg class="waffle" viewBox="0 0 ${wW} ${wH}" role="img" aria-label="${n(big.rows.length)} pages share one link without text; 1 page, the home page, has ${small.detail.toLowerCase()} without text.">${cells}</svg>
              <div class="waffle-key">
                <div><span class="sw g1"></span><span><b>${n(big.rows.length)}</b> pages. ${esc(big.detail)}: <span class="url">${esc(pathOf(big.related))}</span></span></div>
                <div><span class="sw g2"></span><span><b>${small.rows.length}</b> page, the home page. ${esc(small.detail)}</span></div>
              </div>
              <h2 class="evidence-title" style="margin-top: var(--space-2)">The link, from the stored HTML</h2>
              <pre class="snippet" aria-label="HTML of the link">${ev.code}</pre>
              <p class="snippet-cap">On <span class="url">${esc(pathOf(ev.page))}</span>, in the header. The link holds only an SVG logo, so there’s no text for a search engine or a screen reader to read.</p>
            </aside>
          </div>

          <section class="groups" aria-labelledby="groups-title">
            <div class="groups-head">
              <h2 id="groups-title" class="sec-title">Affected pages, grouped by detail</h2>
              <span class="sec-note">${n(total)} pages in ${plural(g.length, 'group', 'groups')}</span>
              <span class="spacer" style="flex: 1"></span>
              <label class="field">${ic('search')}<span class="sr-only">Filter these pages</span><input type="search" placeholder="Filter these pages"></label>
              <button class="btn btn-sm" type="button">Group by detail${ic('chevron-down', 'icon-sm')}</button>
            </div>
            <table class="gtable">
              <thead><tr><th class="expander"><span class="sr-only">Expand</span></th><th scope="col">Detail</th><th scope="col">Links to</th><th scope="col" class="num">Pages</th><th scope="col" class="num">Share of affected pages</th></tr></thead>
              <tbody>
              <tr class="grow"><td class="expander"><button class="gtoggle" type="button" aria-expanded="true" aria-controls="grp-1" aria-label="Hide the pages in this group">${ic('chevron-down')}</button></td><td>${esc(big.detail)}</td><td>${relCell(big.related)}</td><td class="num">${n(big.rows.length)}</td><td class="num">${shareCell(big.rows.length)}</td></tr>
${childRows(big, 'grp-1', shownBig)}
              <tr class="gchild gmore" data-group="grp-1"><td></td><td colspan="4"><a href="urls.html">Show all ${n(big.rows.length)} pages in URLs</a></td></tr>
              <tr class="grow"><td class="expander"><button class="gtoggle" type="button" aria-expanded="false" aria-controls="grp-2" aria-label="Show the pages in this group">${ic('chevron-down')}</button></td><td>${esc(small.detail)}</td><td>${relCell(small.related)} <span class="muted">and 3 more</span></td><td class="num">${n(small.rows.length)}</td><td class="num">${shareCell(small.rows.length)}</td></tr>
${childRows(small, 'grp-2', 5)}
              </tbody>
            </table>
          </section>
        </div>
      </section>
    </div>`;
  return shell({ title: `${c.title} – Issues – ${SITE}`, current: 'issues', section: 'Issues', main });
}

// ---------- URLs: the grid and the inspector ----------
const COLS = [
  { key: 'url', label: 'URL', w: 'url', pin: true },
  { key: 'status', label: 'Status', w: 'status' },
  { key: 'idx', label: 'Indexability', w: 'indexability' },
  { key: 'title', label: 'Title', w: 'title' },
  { key: 'inlinks', label: 'Inlinks', w: 'number', num: true, sort: 'descending' },
  { key: 'issues', label: 'Issues', w: 'issues' },
  { key: 'resp', label: 'Response', w: 'response', num: true },
  { key: 'depth', label: 'Depth', w: 'depth', num: true },
  { key: 'words', label: 'Words', w: 'words', num: true },
  { key: 'size', label: 'Size', w: 'size', num: true },
];
const SELECTED_URL = `${ORIGIN}/`;
const gridRows = urls.rows.filter(r => r.isPage && r.indexable).sort((a, b) => b.inlinks - a.inlinks || a.id - b.id);

function gridHead() {
  return `<colgroup>${COLS.map(c => `<col style="width: var(--col-${c.w})">`).join('')}</colgroup>
              <thead><tr role="row">${COLS.map((c, i) => `<th role="columnheader" scope="col" aria-colindex="${i + 1}" class="${[c.pin ? 'pin' : '', c.num ? 'num' : ''].join(' ').trim()}"${c.sort ? ` aria-sort="${c.sort}"` : ''}><span class="th">${c.label}${c.sort ? ic('arrow-down', 'sort') : ''}</span></th>`).join('')}</tr></thead>`;
}

function issueCounts(r) {
  const o = { error: 0, warning: 0, notice: 0 };
  for (const id of r.issues) o[checkById[id].severity]++;
  return o;
}

function gridBody(rows) {
  return rows.map((r, i) => {
    const sel = r.url === SELECTED_URL;
    const ic3 = issueCounts(r);
    const cells = [
      `<td role="gridcell" class="pin">${urlCell(r.url, 23)}</td>`,
      `<td role="gridcell">${code(r.statusCode)}</td>`,
      `<td role="gridcell" class="t-dim">${r.indexable ? 'Indexable' : `Non-indexable: ${r.indexabilityReason}`}</td>`,
      `<td role="gridcell" title="${esc(r.title)}">${esc(r.title)}</td>`,
      `<td role="gridcell" class="num">${n(r.inlinks)}</td>`,
      `<td role="gridcell">${r.issues.length ? sevCounts(ic3, 'issues', { skipZero: true }) : '<span class="muted">None</span>'}</td>`,
      `<td role="gridcell" class="num">${n(r.responseMs)} ms</td>`,
      `<td role="gridcell" class="num">${r.depth ?? 'Not linked'}</td>`,
      `<td role="gridcell" class="num">${n(r.wordCount)}</td>`,
      `<td role="gridcell" class="num">${kb(r.sizeBytes)}</td>`,
    ];
    return `                <tr role="row" aria-rowindex="${i + 2}" aria-selected="${sel}"${sel ? ' tabindex="0"' : ''}>${cells.join('')}</tr>`;
  }).join('\n');
}

const PRESETS = ['Overview', 'Indexability', 'Content', 'Performance', 'Links'];
function urlsHead(countHtml) {
  return `<div class="urls-head">
          <h1 id="urls-title">URLs</h1>
          ${countHtml}
          <div class="tools">
            <button class="btn btn-sm" type="button" title="Saved views">${ic('bookmark')}Views${ic('chevron-down', 'icon-sm')}</button>
            <button class="btn btn-sm btn-icon" type="button" aria-label="Choose columns" title="Choose columns">${ic('columns')}</button>
            <button class="btn btn-sm btn-icon" type="button" aria-label="Row density: comfortable. Change to compact" title="Row density">${ic('density')}</button>
            <button class="btn btn-sm" type="button" title="Export this view, with its filters and columns (Ctrl+E)">${ic('export')}Export</button>
          </div>
        </div>
        <div class="viewbar">
          <div class="seg" role="group" aria-label="Column preset">${PRESETS.map((p, i) => `<button type="button" aria-pressed="${i === 0}">${p}</button>`).join('')}</div>
          <label class="field">${ic('search')}<span class="sr-only">Search URLs and titles</span><input type="search" placeholder="Search URLs and titles"><kbd>Ctrl F</kbd></label>
        </div>`;
}
function filterBar() {
  return `<div class="filterbar" role="toolbar" aria-label="Filters">
          <span class="chip"><span class="chip-key">Kind</span>&nbsp;is&nbsp;<span class="chip-val">Page</span><button class="chip-x" type="button" aria-label="Remove the filter Kind is Page">${ic('close', 'icon-sm')}</button></span>
          <span class="chip"><span class="chip-key">Indexability</span>&nbsp;is&nbsp;<span class="chip-val">Indexable</span><button class="chip-x" type="button" aria-label="Remove the filter Indexability is Indexable">${ic('close', 'icon-sm')}</button></span>
          <button class="chip-add" type="button">${ic('plus', 'icon-sm')}Add filter</button>
          <a class="clear" href="#">Clear all</a>
        </div>`;
}
const gridFoot = (text) => `<div class="grid-foot">${text}<span class="keys"><kbd>↑</kbd><kbd>↓</kbd> move <kbd>Enter</kbd> inspect <kbd>Space</kbd> select</span></div>`;

function inspector() {
  const d = JSON.parse(fs.readFileSync(path.join(FX, 'garden/inspector/home.json'), 'utf8'));
  const s = d.summary;
  const gate = (label, sub) => `<li class="gate"><span class="gate-mark">${ic('check')}</span><span><b>${label}</b>${sub}</span></li>`;
  const named = d.issuesNamingThisUrl;
  const namedCheck = checkById[named.rows[0].checkId];
  const gauge = (v, lo, hi, max, unit) => `<div class="gauge" style="--v: ${v}; --lo: ${lo}; --hi: ${hi}; --max: ${max}" role="img" aria-label="${v} ${unit}; the analysis expects ${lo} to ${hi}"><span class="gauge-band"></span>${v > hi ? '<span class="gauge-over"></span>' : ''}<span class="gauge-mark"></span></div>
                <div class="gauge-scale"><span><b>${v}</b> ${unit}</span><span>${lo} to ${hi} expected</span></div>`;
  const tabs = [['Summary', null], ['Issues', d.issues.total], ['Inlinks', d.inlinks.total], ['Outlinks', d.outlinks.total], ['Redirects', null], ['Headers', d.headers.rows.length], ['Structured data', d.structuredData.length], ['Hreflang', null], ['Source', null]];
  const issueLine = r => `<a class="mini-issue" href="issues.html">${sevIcon(r.severity)}<span class="mt"><span class="sr-only">${WORD[r.severity].one}: </span>${esc(r.title)}</span><span class="md">${r.relatedUrl ? `${esc(r.detail)}: <span class="url">${esc(pathOf(r.relatedUrl) ?? r.relatedUrl)}</span>` : esc(r.detail ?? 'On the site as a whole')}</span></a>`;
  const ttfb = s.ttfbMs, dl = s.responseMs - s.ttfbMs;
  return `<aside class="inspector" aria-label="Inspector: ${esc(s.url)}">
        <span class="resize" role="separator" aria-orientation="vertical" aria-label="Resize the inspector" tabindex="0"></span>
        <div class="insp-head">
          <div class="insp-top">
            <div class="insp-facts">${code(s.statusCode, true)}<span class="verdict">Indexable</span><span>HTML page</span></div>
            <div class="insp-tools">
              <button class="btn btn-quiet btn-icon btn-sm" type="button" aria-label="Open in browser" title="Open in browser">${ic('external')}</button>
              <button class="btn btn-quiet btn-icon btn-sm" type="button" aria-label="Copy the URL" title="Copy the URL">${ic('copy')}</button>
              <button class="btn btn-quiet btn-icon btn-sm" type="button" aria-label="Close the inspector (Esc)" title="Close (Esc)">${ic('close')}</button>
            </div>
          </div>
          <div class="insp-url">${esc(s.url)}</div>
          <div class="insp-sub">The start URL, depth ${s.depth}. Crawled ${fullTime(s.crawledAt)}.</div>
        </div>
        <div class="tabs" role="tablist" aria-label="About this URL">
          ${tabs.map(([t, k], i) => `<button class="tab" role="tab" type="button" aria-selected="${i === 0}"${i ? ' tabindex="-1"' : ''}>${t}${k != null ? ` <span class="count">${n(k)}</span>` : ''}</button>`).join('')}
        </div>
        <div class="insp-body" role="tabpanel" aria-label="Summary">
          <section class="insp-sec">
            <div class="insp-sec-head"><h3>Indexable, and why</h3></div>
            <ol class="gates">
              ${gate(`${s.statusCode} ${REASON[s.statusCode]}`, 'answered')}
              ${gate('Allowed', 'by robots.txt')}
              ${gate('No noindex', 'meta or header')}
              ${gate('Canonical', 'is itself')}
            </ol>
            <p class="verdict-line">${ic('check')}<span><b>It can appear in search results.</b> Nothing stops it.</span></p>
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head"><h3>Issues on this URL</h3>${sevCounts(issueCounts(s), 'issues', { skipZero: true })}</div>
            <div class="mini-issues">
              ${d.issues.rows.map(issueLine).join('\n              ')}
            </div>
            <p class="caption" style="margin-top: var(--space-3)">It’s also named in ${n(named.total)} issues on other pages: <a href="issues.html">${esc(namedCheck.title)}</a>.</p>
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head"><h3>Content</h3></div>
            <dl class="kv">
              <dt>Title</dt><dd>${esc(s.title)}${gauge(s.titleLength, 30, 60, 80, 'characters')}</dd>
              <dt>Description</dt><dd>${esc(s.metaDescription)}${gauge(s.metaDescriptionLength, 70, 155, 200, 'characters')}</dd>
              <dt>H1</dt><dd>${esc(s.h1)}<span class="why">${s.h1Count === 1 ? 'The only H1' : `${s.h1Count} H1s`}</span></dd>
              <dt>Words</dt><dd class="num">${n(s.wordCount)}</dd>
              <dt>Canonical</dt><dd>Itself<span class="why url">${esc(s.canonicalUrl)}</span></dd>
            </dl>
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head"><h3>Speed and size</h3></div>
            <div class="timing" style="--ttfb: ${ttfb}; --dl: ${dl}; --max: 1200; --at: 1000" role="img" aria-label="First byte after ${ttfb} ms, then ${dl} ms to download: ${s.responseMs} ms. Pages over 1,000 ms count as slow."><span class="t1"></span><span class="t2"></span><span class="limit"></span></div>
            <div class="timing-key"><span><b>${ttfb} ms</b> to the first byte</span><span><b>${dl} ms</b> to download</span><span><b>${s.responseMs} ms</b> in all</span><span>Slow is over 1,000 ms</span></div>
            <dl class="kv" style="margin-top: var(--space-4)">
              <dt>Transferred</dt><dd>${kb(s.sizeBytes)}, ${kb(s.bodyBytes)} of HTML once unpacked</dd>
              <dt>Protocol</dt><dd>HTTP/${s.httpVersion === '2.0' ? '2' : s.httpVersion}</dd>
            </dl>
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head"><h3>Links</h3></div>
            <dl class="kv">
              <dt>Inlinks</dt><dd><a href="#">${n(d.inlinks.total)}</a> from ${n(d.inlinks.uniqueUrls)} pages</dd>
              <dt>Outlinks</dt><dd><a href="#">${n(s.outlinks)}</a> hyperlinks, ${n(d.outlinks.total)} links of every kind</dd>
              <dt>Structured data</dt><dd>${d.structuredData.map(x => `${x.types.join(', ')}${x.valid ? ', valid' : ', invalid'}`).join('; ')}</dd>
            </dl>
          </section>
        </div>
      </aside>`;
}

function urlsPage() {
  const rows = gridRows.slice(0, 26);
  const count = `<span class="countline" aria-live="polite"><b>${n(gridRows.length)}</b> of ${n(garden.urlCount)} URLs</span>`;
  const main = `    <div class="urls">
      <section class="urls-main" aria-labelledby="urls-title">
        ${urlsHead(count)}
        ${filterBar()}
        <div class="grid-wrap">
          <table class="dgrid" role="grid" aria-labelledby="urls-title" aria-rowcount="${gridRows.length + 1}" aria-colcount="${COLS.length}">
              ${gridHead()}
              <tbody>
${gridBody(rows)}
              </tbody>
          </table>
        </div>
        ${gridFoot(`<span>Sorted by <b>Inlinks</b>, most first</span><span><b>1</b> selected, the start URL</span>`)}
      </section>
      ${inspector()}
    </div>`;
  return shell({ title: `URLs – ${SITE}`, current: 'urls', section: 'URLs', main });
}

function urlsLoadingPage() {
  let seed = 7;
  const rnd = () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280; };
  const bone = (min, max, cls = 'sk-line') => `<span class="sk ${cls}" style="width: ${Math.round(min + rnd() * (max - min))}%"></span>`;
  const rowsHtml = Array.from({ length: 22 }, (_, i) => {
    const cells = [
      `<td class="pin"><span class="url-cell"><span class="sk sk-dot" style="border-radius: var(--radius-sm)"></span>${bone(45, 90)}</span></td>`,
      `<td>${bone(50, 60)}</td>`, `<td>${bone(60, 80)}</td>`, `<td>${bone(55, 95)}</td>`, `<td class="num">${bone(35, 50)}</td>`,
      `<td>${bone(30, 55)}</td>`, `<td class="num">${bone(45, 60)}</td>`, `<td class="num">${bone(20, 30)}</td>`, `<td class="num">${bone(40, 55)}</td>`, `<td class="num">${bone(45, 60)}</td>`,
    ];
    return `                <tr aria-hidden="true">${cells.join('')}</tr>`;
  }).join('\n');
  const count = `<span class="countline sk-region"><span class="sk sk-line" style="width: 128px; display: inline-block"></span></span>`;
  const insp = `<aside class="inspector" aria-label="Inspector, loading" aria-busy="true">
        <span class="resize" aria-hidden="true"></span>
        <div class="insp-head">
          <div class="insp-top">
            <div class="insp-facts sk-region" aria-hidden="true"><span class="sk sk-chip" style="width: 72px; height: var(--space-5)"></span><span class="sk sk-line" style="width: 64px"></span><span class="sk sk-line" style="width: 72px"></span></div>
            <div class="insp-tools"><button class="btn btn-quiet btn-icon btn-sm" type="button" aria-label="Close the inspector (Esc)" title="Close (Esc)">${ic('close')}</button></div>
          </div>
          <div class="insp-url">${esc(SELECTED_URL)}</div>
          <div class="insp-sub sk-region" aria-hidden="true"><span class="sk sk-text" style="width: 62%; margin-top: var(--space-2)"></span></div>
        </div>
        <div class="tabs" role="tablist" aria-label="About this URL">
          ${['Summary', 'Issues', 'Inlinks', 'Outlinks', 'Redirects', 'Headers', 'Structured data', 'Hreflang', 'Source'].map((t, i) => `<button class="tab" role="tab" type="button" aria-selected="${i === 0}"${i ? ' tabindex="-1"' : ''}>${t}</button>`).join('')}
        </div>
        <div class="insp-body sk-region" aria-hidden="true">
          <section class="insp-sec">
            <div class="insp-sec-head">${bone(36, 40)}</div>
            <div class="gates" style="--x: 0">${Array.from({ length: 4 }, () => `<div class="gate"><span class="sk sk-dot" style="width: var(--space-5); height: var(--space-5)"></span><span class="sk sk-text" style="width: 70%"></span><span class="sk sk-text" style="width: 50%"></span></div>`).join('')}</div>
            <span class="sk sk-block" style="margin-top: var(--space-4)"></span>
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head">${bone(30, 34)}</div>
            ${Array.from({ length: 5 }, () => `<div class="mini-issue"><span class="sk sk-dot"></span><span style="display: grid; gap: var(--space-2)">${bone(55, 85)}${bone(35, 60, 'sk-text')}</span></div>`).join('\n            ')}
          </section>
          <section class="insp-sec">
            <div class="insp-sec-head">${bone(20, 24)}</div>
            <div class="kv">${Array.from({ length: 4 }, () => `<span class="sk sk-line" style="width: 70%"></span><span style="display: grid; gap: var(--space-2)">${bone(70, 95)}${bone(40, 70)}</span>`).join('')}</div>
          </section>
        </div>
      </aside>`;
  const main = `    <div class="urls">
      <section class="urls-main" aria-labelledby="urls-title" aria-busy="true">
        ${urlsHead(count)}
        ${filterBar()}
        <div class="grid-wrap">
          <p class="sr-only" role="status">Loading URLs</p>
          <table class="dgrid" role="grid" aria-labelledby="urls-title" aria-busy="true">
              ${gridHead()}
              <tbody class="sk-region">
${rowsHtml}
              </tbody>
          </table>
        </div>
        ${gridFoot(`<span class="loading-note sk-region">Loading the first rows…</span>`)}
      </section>
      ${insp}
    </div>`;
  return shell({ title: `URLs, loading – ${SITE}`, current: 'urls', section: 'URLs', main });
}

// ---------- Write ----------
const pages = {
  'home.html': home(),
  'live.html': livePage(),
  'overview.html': overviewPage(),
  'issues.html': issuesPage(),
  'urls.html': urlsPage(),
  'urls-loading.html': urlsLoadingPage(),
};
for (const [file, html] of Object.entries(pages)) {
  fs.writeFileSync(path.join(OUT, file), html);
  console.log(`${file.padEnd(18)} ${(html.length / 1024).toFixed(0)} KB`);
}
