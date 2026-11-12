// The screens both modes have: the Report screen, Settings and New crawl, each as Simple and Advanced show it,
// and Advanced's Overview with the switch.
import { page, kbd } from '../lib/shell.mjs';
import { icon, sevIcon } from '../lib/icons.mjs';
import { esc, num, ring, mark, btn, checkbox, toggle } from '../lib/ui.mjs';
import { SITE, problems, facts, totals, gardenScore, toFix, garden, LIVE } from '../lib/data.mjs';

const gardenCrawl = mode => mode === 'simple'
  ? { site: SITE, meta: 'Checked 31 Oct, 14:13', counts: { fix: toFix.length } }
  : { site: SITE, meta: '31 Oct 2026, 14:13', counts: { issues: totals.checksWithIssues }, sevCounts: [['error', 'Errors', totals.checks.error], ['warning', 'Warnings', totals.checks.warning], ['notice', 'Notices', totals.checks.notice]] };
const gardenCrumbs = section => [{ label: 'Home' }, { label: SITE, favicon: SITE }, { label: '31 Oct 2026, 14:13', kind: 'date' }, { label: section }];

// ------------------------------------------------------------------ The Report screen (both modes)
export function reportScreenPage(mode = 'simple') {
  const main = `    <div class="page--report">
      <div class="report-bar">
        <div class="report-bar__text">
          <h1 class="report-bar__title">Report</h1>
          <p class="report-bar__sub">${SITE} · checked 31 October 2026 · 12 pages</p>
        </div>
        <div class="segmented" role="radiogroup" aria-label="How much of the report">
          <button class="segmented__item" type="button" role="radio" aria-checked="true">Full report</button>
          <button class="segmented__item" type="button" role="radio" aria-checked="false">Summary page</button>
        </div>
        ${btn('Spreadsheet of everything…', { ic: 'table' })}
        ${btn('Save…', { ic: 'download' })}
        ${btn('Print or save as PDF', { kind: 'primary', ic: 'printer', kbd: 'Ctrl+P' })}
      </div>
      <div class="report-stage">
        <iframe class="report-frame" src="report.html" title="The report for ${SITE}" sandbox></iframe>
      </div>
    </div>`;
  return page({
    mode, title: `Report: ${SITE}`, file: 'report-screen.html', section: 'report', crawl: gardenCrawl(mode),
    crumbs: gardenCrumbs('Report'), state: { kind: 'done' }, main,
  });
}

// ------------------------------------------------------------------ Settings (both modes)
const SETTINGS = {
  simple: ['General', 'Data folder', 'Updates', 'About'],
  advanced: ['General', 'Crawl defaults', 'Analysis', 'Data folder', 'Shortcuts', 'Diagnostics', 'Updates', 'About'],
};
export function settingsPage(mode) {
  const simple = mode === 'simple';
  const choice = (id, title, text) => `<button class="choice" type="button" role="radio" aria-checked="${mode === id}"><span class="choice__radio" aria-hidden="true"></span><span class="choice__title">${title}</span><span class="choice__text">${text}</span></button>`;
  const seg = (items, on) => `<div class="segmented segmented--lg" role="radiogroup">${items.map(i => `<button class="segmented__item" type="button" role="radio" aria-checked="${i === on}">${i}</button>`).join('')}</div>`;
  const main = `    <div class="page${simple ? ' page--simple' : ''}">
      <div class="settings">
        <nav class="settings-nav" aria-label="Settings">
          ${SETTINGS[mode].map((s, i) => `<a class="settings-nav__item${i === 0 ? ' is-current' : ''}" href="#"${i === 0 ? ' aria-current="page"' : ''}>${s}</a>`).join('\n          ')}
          ${simple ? `<div class="settings-nav__note">Crawl defaults, the checks' limits, shortcuts and diagnostics are Advanced settings.<br><a class="s-link" href="settings-advanced.html">Show them in Advanced${icon('arrow-up-right', { size: 'sm' })}</a></div>` : ''}
        </nav>
        <div>
          <header class="s-header">
            <div class="s-header__text">
              <h1 class="s-title">General</h1>
              <p class="s-sub">How much the app shows, and how it looks. Changes apply at once and are remembered.</p>
            </div>
          </header>
          <section class="setting" aria-labelledby="set-mode">
            <h2 class="setting__label" id="set-mode">Mode</h2>
            <p class="setting__help">The same site check either way, with the same checks and the same numbers. This is the switch at the top right of every screen${simple ? '' : ' (Ctrl+Shift+A)'}.</p>
            <div class="setting__control choices" role="radiogroup" aria-labelledby="set-mode">
              ${choice('simple', 'Simple', 'A score, what to fix and a report, in plain words.')}
              ${choice('advanced', 'Advanced', 'Every URL, link, filter and setting, in technical terms.')}
            </div>
          </section>
          <section class="setting" aria-labelledby="set-theme">
            <h2 class="setting__label" id="set-theme">Theme</h2>
            <p class="setting__help">Light, dark, or whichever your computer is using.</p>
            <div class="setting__control">${seg(['Follow the system', 'Light', 'Dark'], 'Follow the system')}</div>
          </section>${simple ? '' : `
          <section class="setting" aria-labelledby="set-density">
            <h2 class="setting__label" id="set-density">Density</h2>
            <p class="setting__help">How tall the rows of every grid are. Rows of 32 px.</p>
            <div class="setting__control">${seg(['Compact', 'Standard', 'Comfortable'], 'Standard')}</div>
          </section>`}
        </div>
      </div>
    </div>`;
  return page({ mode, title: 'Settings', file: `settings-${mode}.html`, section: 'settings', crumbs: [{ label: 'Home' }, { label: 'Settings' }], main });
}

// ------------------------------------------------------------------ New crawl, in Simple
export function newSimplePage() {
  const preset = (on, title, text) => `<button class="choice" type="button" role="radio" aria-checked="${on}"><span class="choice__radio" aria-hidden="true"></span><span class="choice__title">${title}</span><span class="choice__text">${text}</span></button>`;
  const main = `    <div class="page--simple">
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">Check a website</h1>
          <p class="s-sub">The address is all it needs. Change the rest only if you want to.</p>
        </div>
      </header>
      <form class="form" onsubmit="return false">
        <div class="form-row">
          <label class="form-label" for="address">Web address</label>
          <p class="form-help">Any page on the site. The check starts there and follows every link it finds on the same site.</p>
          <div class="form-control"><div class="field field--lg">${icon('globe')}<input class="field__input" id="address" type="url" value="https://www.gardenfurnituresets.co.uk/" spellcheck="false"></div></div>
        </div>
        <div class="form-row">
          <span class="form-label" id="l-speed">How fast</span>
          <p class="form-help">Faster checks finish sooner but ask more of the site's hosting. If the site starts to struggle, the check slows down by itself.</p>
          <div class="form-control choices" style="--cols:3" role="radiogroup" aria-labelledby="l-speed">
            ${preset(false, 'Polite', 'Up to 2 addresses a second. For small or shared hosting.')}
            ${preset(true, 'Standard', '5 addresses at a time. Right for most sites.')}
            ${preset(false, 'Fast', '16 addresses at a time. For big sites on strong hosting.')}
          </div>
        </div>
        <div class="form-row">
          <span class="form-label" id="l-much">How much</span>
          <p class="form-help">A limit makes a first look at a very big site quicker. The score then covers only what was read.</p>
          <div class="form-control inline-choice" role="radiogroup" aria-labelledby="l-much">
            <span class="radio" role="radio" aria-checked="true" tabindex="0"><span class="radio__dot"></span>The whole site</span>
            <span class="radio" role="radio" aria-checked="false" tabindex="-1"><span class="radio__dot"></span>Stop after</span>
            <div class="field field--num"><input class="field__input num" type="text" inputmode="numeric" value="5,000" aria-label="Addresses to stop after" disabled></div>
            <span class="muted">addresses</span>
          </div>
        </div>
        <div class="form-row">
          <span class="form-label">Also check</span>
          <div class="form-control checks-row">
            ${checkbox('Links to other websites', 'So a link from your site to a page that has gone is found too.', true)}
            ${checkbox('Images and other files', 'So broken and oversized images are found. Turn it off for a quicker look at pages only.', true)}
          </div>
        </div>
        <div class="form-actions">
          <button class="btn btn--primary btn--xl" type="button">Check site<kbd class="kbd kbd--inline">Ctrl+Enter</kbd></button>
          ${btn('Cancel', { kind: 'ghost', size: 'lg' })}
          ${checkbox('Use these choices every time', '', false)}
        </div>
        <div class="form-more">${icon('sliders')}<span>Which parts of the site to read, sitemaps, robots.txt, sign-in headers, a proxy and timeouts are Advanced settings. What you've typed here goes with you. <a class="s-link" href="new-advanced.html">Open in Advanced${icon('arrow-up-right', { size: 'sm' })}</a></span></div>
      </form>
    </div>`;
  return page({ mode: 'simple', title: 'Check a website', file: 'new-simple.html', section: 'home', crumbs: [{ label: 'Home' }, { label: 'Check a website' }], main });
}

// ------------------------------------------------------------------ New crawl, in Advanced
export function newAdvancedPage() {
  const preset = (on, title, text) => `<button class="choice" type="button" role="radio" aria-checked="${on}"><span class="choice__radio" aria-hidden="true"></span><span class="choice__title">${title}</span><span class="choice__text">${text}</span></button>`;
  const opt = (label, help, control) => `<div class="opt"><span class="opt__label">${label}</span><span class="opt__help">${help}</span><span class="opt__control">${control}</span></div>`;
  const fold = (title, sum, body = '') => `<section class="fold${body ? ' is-open' : ''}">
            <div class="fold__head" role="button" tabindex="0" aria-expanded="${body ? 'true' : 'false'}">${icon('chevron-right', { cls: 'fold__chev' })}<h2 class="fold__title">${title}</h2><span class="fold__sum">${sum}</span></div>
            ${body ? `<div class="fold__body">${body}</div>` : ''}
          </section>`;
  const numField = (value, label) => `<div class="field field--num"><input class="field__input num" type="text" inputmode="numeric" value="${value}" aria-label="${label}"></div>`;
  const main = `    <div class="page page--form">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">New crawl</h1>
          <p class="page-subtitle">Every setting has a default. Change what this crawl needs, then start it.</p>
        </div>
      </header>
      <div class="card start-card">
        <label class="start-card__label" for="start-url">Start URL</label>
        <div class="field field--lg">${icon('globe')}<input class="field__input" id="start-url" type="url" value="https://www.gardenfurnituresets.co.uk/" spellcheck="false"></div>
        <div class="presets start-card__presets" role="radiogroup" aria-label="Speed preset">
          ${preset(false, 'Polite', 'Up to 2 requests a second, 2 at a time.')}
          ${preset(true, 'Standard', '5 at a time, no cap, slowing down when the site does.')}
          ${preset(false, 'Fast', '16 at a time, no cap.')}
        </div>
      </div>
      <div class="card" style="margin-top:var(--space-4)">
        ${fold('Scope', 'This folder only, no subdomains', [
          opt('Stay in the start folder', 'Crawl only URLs under the start URL\'s folder.', toggle('Stay in the start folder', true)),
          opt('Include subdomains', 'Treat blog.example.com as part of example.com.', toggle('Include subdomains', false)),
          opt('Follow nofollow links', 'Crawl URLs that are linked only with rel="nofollow".', toggle('Follow nofollow links', false)),
          opt('Crawl canonical targets', 'Fetch the URL a canonical names, even when nothing links to it.', toggle('Crawl canonical targets', true)),
          opt('Crawl hreflang and pagination targets', 'Fetch URLs named only by hreflang, rel="next" or rel="prev".', toggle('Crawl hreflang and pagination targets', false)),
        ].join(''))}
        ${fold('Limits', 'No URL limit, any depth, HTML up to 8 MB')}
        ${fold('Robots and sitemaps', 'Obey robots.txt, find sitemaps')}
        ${fold('Headers and network', 'ShoutingIguana user agent, no proxy, cookies kept')}
        ${fold('Advanced', '20 s timeout, 2 retries, resources and external links checked, HTML not stored')}
      </div>
      <div class="form-bar">
        <button class="btn btn--primary btn--lg" type="button">Start crawl<kbd class="kbd kbd--inline">Ctrl+Enter</kbd></button>
        ${btn('Save as my defaults', { size: 'lg' })}
        ${btn('Cancel', { kind: 'ghost', size: 'lg' })}
        <span class="form-bar__note">Simple shows the start URL, the speed, a URL limit and two of these switches.</span>
      </div>
    </div>`;
  return page({ mode: 'advanced', title: 'New crawl', file: 'new-advanced.html', section: 'home', crumbs: [{ label: 'Home' }, { label: 'New crawl' }], main });
}

// ------------------------------------------------------------------ Advanced's Overview, with the switch
export function advancedOverviewPage() {
  const t = totals;
  const stat = (sev, word, n, sub) => `<a class="stat" href="#"><span class="stat__label">${sev === 'passed' ? icon('circle-check', { size: 'sm' }) : sevIcon(sev, 'sm', '')}${word}</span><span class="stat__value">${n}<small>checks</small></span><span class="stat__context">${sub}</span></a>`;
  const sevWord = { error: 'Error', warning: 'Warning', notice: 'Notice' };
  const first = problems.slice(0, 6).map(p => `<a class="ov-row" href="#">${sevIcon(p.severity, 'md', sevWord[p.severity])}<span class="ov-row__title">${esc(p.title)}</span><span class="ov-row__n"><b>${num(p.count)}</b> ${p.id === 'internal-client-error' ? 'URLs' : 'pages'}</span><span class="ov-row__sub">${esc(DESCRIPTIONS[p.id] ?? '')}</span></a>`).join('\n          ');
  const main = `    <div class="page">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">Overview</h1>
          <p class="page-subtitle">${t.checksWithIssues} checks found ${num(t.issues.error + t.issues.warning + t.issues.notice)} issues on this crawl. ${t.checksPassed} passed and ${garden.skipped.length} were skipped.</p>
        </div>
        <a class="ov-head-score" href="score.html" title="How the score is worked out">${ring(gardenScore.value, { size: 'md', of: false })}<span class="ov-head-score__text"><b>Score: ${gardenScore.band.word}</b>How it's worked out</span></a>
      </header>
      <div class="card ov-notes">${icon('info')}<span>Some checks didn't run on this crawl, so it may have more issues than these.</span>${btn('Show the 3 notes', { kind: 'ghost' })}</div>
      <div class="card stats" style="--cols:4">
        ${stat('error', 'Errors', t.checks.error, `${t.issues.error} issues between them`)}
        ${stat('warning', 'Warnings', t.checks.warning, `${t.issues.warning} issues between them`)}
        ${stat('notice', 'Notices', t.checks.notice, `${t.issues.notice} issues between them`)}
        ${stat('passed', 'Passed', t.checksPassed, `${garden.skipped.length} were skipped, with why below`)}
      </div>
      <div class="ov-grid">
        <section class="card">
          <div class="card__header"><h2 class="card__title">Fix first</h2><span class="card__meta">The most severe first, then the most pages affected</span></div>
          <div class="card__body">
          ${first}
          </div>
        </section>
        <section class="card">
          <div class="card__header"><h2 class="card__title">Not checked on this crawl</h2><span class="card__meta">${garden.skipped.length} checks skipped</span></div>
          <div class="card__body">
            <div class="ov-skip">${sevIcon('skipped', 'md', 'Skipped')}<div><p class="ov-skip__title">Orphan page in a sitemap and Indexable page not in a sitemap</p><p class="ov-skip__why">No sitemap could be read.</p></div></div>
            <div class="ov-skip">${sevIcon('skipped', 'md', 'Skipped')}<div><p class="ov-skip__title">Near-duplicate content</p><p class="ov-skip__why">This crawl was made by an earlier version, which didn't record what this check needs. Crawl again to check it.</p></div></div>
            <div class="ov-skip">${sevIcon('skipped', 'md', 'Skipped')}<div><p class="ov-skip__title">Duplicate version of the site</p><p class="ov-skip__why">The site's other versions (www or not, http or https) weren't all checked.</p></div></div>
          </div>
        </section>
      </div>
    </div>`;
  return page({
    mode: 'advanced', title: `Overview: ${SITE}`, file: 'advanced-overview.html', section: 'overview', crawl: gardenCrawl('advanced'),
    crumbs: gardenCrumbs('Overview'), state: { kind: 'done' }, main,
  });
}
// The catalog's own descriptions, as Advanced shows them (the first line of each).
const DESCRIPTIONS = {
  'internal-client-error': 'This internal URL, a page or a resource such as an image or script, answered with a 4xx client error such as 404 Not Found, 410 Gone or 403 Forbidden.',
  'broken-internal-links': 'The page links to internal URLs that return a 4xx or 5xx error or don\'t respond, directly or at the end of their redirects.',
  'image-too-large': 'The page embeds images whose files, after any redirects, are over 200 KB as transferred.',
  'description-duplicate': 'Another indexable page has the same meta description, ignoring case and spacing.',
  'title-duplicate': 'Another indexable page has the same title, ignoring case and spacing.',
  'slow-response': 'The crawler took more than 1,000 ms to receive this page\'s HTML, from request to last byte.',
};

// ------------------------------------------------------------------ The shell at its tightest: Advanced, while a crawl runs
export function advancedLivePage() {
  const L = LIVE;
  const stat = (label, value, ctx) => `<div class="stat"><span class="stat__label">${label}</span><span class="stat__value">${value}</span><span class="stat__context">${ctx}</span></div>`;
  const main = `    <div class="page">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">Overview <span class="badge badge--accent live-badge"><span class="live-dot" aria-hidden="true"></span>Live</span></h1>
          <p class="page-subtitle">Crawling <span class="mono">https://www.gardenfurnituresets.co.uk/</span></p>
        </div>
      </header>
      <div class="card stats" style="--cols:4">
        ${stat('Crawled', num(L.checked), 'URLs with an answer')}
        ${stat('Found', num(L.found), `${num(L.notFollowed)} not followed`)}
        ${stat('Queued', num(L.queued), 'Waiting their turn')}
        ${stat('Pages', num(L.pages), 'HTML pages read')}
      </div>
      <p class="s-note" style="margin-top:var(--space-4)">The rest of the live Overview is as built. This page exists to show the top bar at its fullest: the status, Pause, Stop, search and the switch.</p>
    </div>`;
  return page({
    mode: 'advanced', title: `Overview: ${SITE}`, file: 'advanced-live.html', section: 'overview',
    crawl: { site: SITE, meta: '31 Oct 2026, 14:13, crawling', counts: {} },
    running: { site: SITE, checked: L.checked, ratio: L.checked / (L.checked + L.queued), current: true },
    crumbs: gardenCrumbs('Overview'), state: { kind: 'running', count: L.checked }, main,
  });
}
