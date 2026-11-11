// Simple's screens: Home, checking a site, the Summary, What to fix, one problem, and the score's breakdown.
import { page, favicon } from '../lib/shell.mjs';
import { icon } from '../lib/icons.mjs';
import { esc, num, ring, mark, badge, btn, duration } from '../lib/ui.mjs';
import { SEVERITY, FIXER, CATEGORY, SKIP, SKIP_ACTION, TITLE, PLAIN } from '../lib/words.mjs';
import { score, POINTS, EXTENTS, SCORE_VERSION, SERIOUS_CEILING } from '../lib/score.mjs';
import { garden, SITE, problems, facts, totals, gardenScore, lineOf, toFix, toLook, fixFirst, scoreAfterFirst, path, pageName, INVENTED, LIVE } from '../lib/data.mjs';

const CHECKED = '31 October 2026';
const gardenCrawl = { site: SITE, meta: 'Checked 31 Oct, 14:13', counts: { fix: toFix.length } };
const crumbsFor = (site, date, section) => [{ label: 'Home' }, { label: site, favicon: site }, { label: date, kind: 'date' }, { label: section }];
const gardenCrumbs = section => crumbsFor(SITE, '31 Oct 2026, 14:13', section);
const runningElsewhere = { site: INVENTED.running.site, checked: INVENTED.running.checked, ratio: INVENTED.running.checked / INVENTED.running.found };

// "5 of 265 pages", "9 of 4,521 addresses", "the whole site"
function countOf(line) {
  if (line.unit === 'site') return '<b>The whole site</b>';
  return `<b>${num(line.count)}</b> of ${num(line.of)} ${line.unit}`;
}
const lineFor = (p, sc = gardenScore) => sc.lines.find(l => l.checkId === p.id);
const isTemplate = p => p.targets?.[0] && p.targets[0].pages >= 10 && p.targets[0].pages / p.count >= 0.5;

function fixRow(p, { n = null, line = lineFor(p), href = 'problem.html', summary = p.plain?.what[0] } = {}) {
  return `<a class="fixrow${n == null ? ' fixrow--plain' : ''}${p.severity === 'notice' ? ' fixrow--quiet' : ''}" href="${href}">
        ${n == null ? '' : `<span class="fixrow__n">${n}</span>`}
        ${mark(p.severity)}
        <span class="fixrow__text">
          <span class="fixrow__title">${esc(p.plainTitle)}${isTemplate(p) ? `<span class="tag">${icon('layers', { size: 'xs' })}One fix</span>` : ''}</span>
          <span class="fixrow__sum">${esc(summary ?? '')}</span>
        </span>
        <span class="fixrow__count">${countOf(line)}</span>
        ${icon('chevron-right', { cls: 'fixrow__chev' })}
      </a>`;
}

const bandLead = { 'very-good': 'Your site is in very good shape.', good: 'Your site is in good shape.', 'needs-work': 'Your site needs some work.', poor: 'Your site has problems that will hold it back in search.' };
function counts(errors, warnings) {
  const parts = [];
  if (errors) parts.push(`${errors} ${errors === 1 ? SEVERITY.error.one : SEVERITY.error.many}`);
  if (warnings) parts.push(`${warnings} ${warnings === 1 ? SEVERITY.warning.one : SEVERITY.warning.many}`);
  return parts.join(' and ');
}

function tally({ errors, warnings, notices, passed, skipped }) {
  const row = (ic, n, label, zero) => `<a class="tally__row${n === 0 ? ' is-zero' : ''}" href="fix.html">${ic}<span class="tally__n">${num(n)}</span><span>${label}</span></a>`;
  return `<div class="tally" aria-label="In numbers">
      ${row(mark('error'), errors, errors === 1 ? 'Serious problem' : 'Serious problems')}
      ${row(mark('warning'), warnings, warnings === 1 ? 'Problem worth fixing' : 'Problems worth fixing')}
      ${row(mark('notice'), notices, notices === 1 ? 'Thing worth a look' : 'Things worth a look')}
      ${row(mark('passed'), passed, passed === 1 ? 'Check found nothing wrong' : 'Checks found nothing wrong')}
      ${row(mark('skipped'), skipped, skipped === 1 ? 'Check couldn\'t run' : 'Checks couldn\'t run')}
    </div>`;
}

const reportCard = `<div class="card">
        <h3 class="trio__title">${icon('file-text')}The report</h3>
        <p class="trio__text">Everything on this screen and behind it, in one document to keep, print or send to whoever looks after your site.</p>
        <div class="trio__actions">${btn('Open the report', { kind: 'secondary', ic: 'file-text' })}${btn('Save as PDF', { kind: 'ghost', ic: 'download' })}</div>
      </div>`;

// ------------------------------------------------------------------ Summary: the garden crawl
const cleanCategories = (() => {
  const bad = new Set(garden.checks.map(c => c.category));
  return [...new Set(garden.passed.map(p => p.category))].filter(c => !bad.has(c)).map(c => CATEGORY[c].toLowerCase());
})();
const listWords = a => a.length > 1 ? `${a.slice(0, -1).join(', ')} or ${a.at(-1)}` : a[0];

function summaryMain() {
  const s = gardenScore;
  return `    <div class="page--simple">
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">${SITE}</h1>
          <p class="s-sub">Checked on ${CHECKED}. ${num(facts.pages)} pages read in ${duration(new Date(facts.finishedAt) - new Date(facts.startedAt))}.</p>
        </div>
        <div class="s-actions">${btn('Check again', { ic: 'refresh', size: 'lg' })}${btn('Open the report', { kind: 'primary', ic: 'file-text', size: 'lg' })}</div>
      </header>

      <section class="card hero" aria-label="Score">
        <div class="hero__main">
          ${ring(s.value)}
          <div class="hero__text">
            <h2 class="hero__word">${s.band.word}</h2>
            <p class="hero__sentence">${bandLead[s.band.id]} There are ${counts(totals.checks.error, totals.checks.warning)}.</p>
            <p class="hero__covers">The score covers all ${num(facts.pages)} pages and ${totals.checksRun} of ${totals.checksInCatalog} checks. ${totals.checksInCatalog - totals.checksRun} checks couldn't run.</p>
            <p class="hero__how"><a class="s-link" href="score.html">How the score is worked out${icon('chevron-right', { size: 'sm' })}</a></p>
          </div>
        </div>
        ${tally({ errors: totals.checks.error, warnings: totals.checks.warning, notices: totals.checks.notice, passed: totals.checksPassed, skipped: garden.skipped.length })}
      </section>

      <section class="s-section card fixlist" aria-labelledby="fix-first">
        <div class="fixlist__head"><h2 class="s-h2" id="fix-first">Fix these first</h2><span class="s-h2__meta">The most serious first, then the most pages</span></div>
        ${fixFirst.map((p, i) => fixRow(p, { n: i + 1 })).join('\n        ')}
        <div class="fixlist__foot"><span>Fixing these five takes the score from <b class="strong">${s.value}</b> to <b class="strong">${scoreAfterFirst}</b>.</span><a class="s-link" href="fix.html">See all ${toFix.length} problems and ${toLook.length} things worth a look${icon('chevron-right', { size: 'sm' })}</a></div>
      </section>

      <section class="s-section trio" aria-label="The rest">
        <div class="card">
          <h3 class="trio__title">${icon('circle-check')}What's fine</h3>
          <p class="trio__figure">${totals.checksPassed}<span>of ${totals.checksRun} checks found nothing wrong</span></p>
          <p class="trio__text">Nothing wrong with ${listWords(cleanCategories)}.</p>
          <div class="trio__actions"><a class="s-link" href="fix.html">See what was checked${icon('chevron-right', { size: 'sm' })}</a></div>
        </div>
        <div class="card">
          <h3 class="trio__title">${mark('skipped', 'md')}Couldn't be checked</h3>
          <p class="trio__figure">${garden.skipped.length}<span>checks couldn't run</span></p>
          <ul class="trio__list">
            <li>${mark('skipped', 'md')}<span><b>2 sitemap checks.</b> ${SKIP['no-sitemap']}</span></li>
            <li>${mark('skipped', 'md')}<span><b>Your site's other addresses.</b> They couldn't all be tried.</span></li>
            <li>${mark('skipped', 'md')}<span><b>Almost identical pages.</b> ${SKIP['earlier-version']}</span></li>
          </ul>
          <div class="trio__actions"><span class="s-note"><a class="s-link" href="#">Check again</a>&nbsp;to run 2 of them.</span></div>
        </div>
        ${reportCard}
      </section>

      <p class="facts-line">Shouting Iguana read ${num(facts.pages)} pages and checked ${num(facts.urlsChecked - facts.pages)} images, files and links: ${num(facts.urlsChecked)} addresses in all, on ${CHECKED}, 14:13 to 14:51.</p>
    </div>`;
}

export const summaryPage = () => page({
  title: `Summary: ${SITE}`, file: 'summary.html', section: 'summary', crawl: gardenCrawl,
  crumbs: gardenCrumbs('Summary'), state: { kind: 'done' }, main: summaryMain(),
});

// ------------------------------------------------------------------ How the score is worked out
function scoreDialog() {
  const s = gardenScore;
  const row = p => { const l = lineFor(p); return `<tr><td><a href="problem.html">${mark(p.severity, 'md')}${esc(p.plainTitle)}</a></td><td>${num(l.count)} of ${num(l.of)} ${l.unit}: ${l.extentLabel}</td><td class="num points">−${l.points}</td></tr>`; };
  const cell = (sev, id) => `<td>${POINTS[sev][id] || 0}</td>`;
  return `<div class="scrim">
  <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="score-title">
    <div class="dialog__head"><h2 class="dialog__title" id="score-title">How the score is worked out</h2><button class="icon-btn" type="button" aria-label="Close" title="Close (Esc)">${icon('x')}</button></div>
    <div class="dialog__body">
      <div class="dialog__lead">
        ${ring(s.value, { size: 'md', of: false })}
        <p>The score starts at 100, and each check that found a problem takes points off: up to 10 for a serious problem and up to 4 for one worth fixing, by how much of the site it affects. <b>Things worth a look take nothing off.</b></p>
      </div>
      <table class="score-table">
        <thead><tr><th scope="col">What took points off</th><th scope="col">How much of the site</th><th scope="col" class="num">Points</th></tr></thead>
        <tbody>
          ${toFix.map(row).join('\n          ')}
          <tr class="is-free"><td><a href="fix.html">${mark('notice', 'md')}${toLook.length} things worth a look</a></td><td>They don't change the score</td><td class="num">0</td></tr>
        </tbody>
        <tfoot><tr><td>Score</td><td>100 − ${s.lost}</td><td class="num">${s.value}</td></tr></tfoot>
      </table>

      <h3 class="dialog__h">What it covers</h3>
      <p class="dialog__p">All ${num(facts.pages)} pages the check found, and ${totals.checksRun} of the ${totals.checksInCatalog} checks. ${garden.skipped.length} checks couldn't run, so anything they would have found isn't counted.</p>

      <h3 class="dialog__h">Points for each problem</h3>
      <table class="points-grid">
        <thead><tr><th scope="col">Affects</th>${EXTENTS.map(e => `<th scope="col">${e.label[0].toUpperCase() + e.label.slice(1)} pages<br>${e.describe}</th>`).join('')}<th scope="col">The whole site<br>one fault for all of it</th></tr></thead>
        <tbody>
          <tr><td>${mark('error', 'md')}Serious</td>${EXTENTS.map(e => cell('error', e.id)).join('')}${cell('error', 'site')}</tr>
          <tr><td>${mark('warning', 'md')}Worth fixing</td>${EXTENTS.map(e => cell('warning', e.id)).join('')}${cell('warning', 'site')}</tr>
          <tr><td>${mark('notice', 'md')}Worth a look</td>${EXTENTS.map(e => cell('notice', e.id)).join('')}${cell('notice', 'site')}</tr>
        </tbody>
      </table>
      <p class="dialog__p">While any serious problem stands, the score can't be above ${SERIOUS_CEILING}. 90 and over is Very good, 70 and over Good, 50 and over Needs work, and under 50 Poor.</p>
    </div>
    <div class="dialog__foot"><span>Score version ${SCORE_VERSION}</span>${btn('Close', { kind: 'secondary' })}</div>
  </div>
</div>`;
}

export const scorePage = () => page({
  title: `How the score is worked out: ${SITE}`, file: 'score.html', section: 'summary', crawl: gardenCrawl,
  crumbs: gardenCrumbs('Summary'), state: { kind: 'done' }, main: summaryMain(), overlay: scoreDialog(),
});

// ------------------------------------------------------------------ Summary: a perfect site
export function summaryPerfectPage() {
  const site = INVENTED.perfect.site;
  const look = [['title-too-long', 41], ['image-missing-dimensions', 12], ['noindex-page', 3]];
  const main = `    <div class="page--simple">
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">${site}</h1>
          <p class="s-sub">Checked on 28 October 2026. ${num(INVENTED.perfect.pages)} pages read in 9 min 2 s.</p>
        </div>
        <div class="s-actions">${btn('Check again', { ic: 'refresh', size: 'lg' })}${btn('Open the report', { kind: 'primary', ic: 'file-text', size: 'lg' })}</div>
      </header>

      <section class="card hero" aria-label="Score">
        <div class="hero__main">
          ${ring(100)}
          <div class="hero__text">
            <h2 class="hero__word">Very good</h2>
            <p class="hero__sentence">Nothing needs fixing. 3 things are worth a look when you have a moment.</p>
            <p class="hero__covers">The score covers all ${num(INVENTED.perfect.pages)} pages and all 86 checks.</p>
            <p class="hero__how"><a class="s-link" href="#">How the score is worked out${icon('chevron-right', { size: 'sm' })}</a></p>
          </div>
        </div>
        ${tally({ errors: 0, warnings: 0, notices: 3, passed: 83, skipped: 0 })}
      </section>

      <section class="s-section card fixlist" aria-labelledby="nothing">
        <div class="fixlist__head"><h2 class="s-h2" id="nothing">${icon('circle-check', { size: 'lg' })}Nothing to fix</h2></div>
        <div class="fixlist__foot" style="border-top:0;background:none"><span>No check found a serious problem or one worth fixing. Sites drift as pages are added and removed, so check again after a big change, or every month or so.</span></div>
        ${look.map(([id, n]) => `<a class="fixrow fixrow--plain fixrow--quiet" href="#">${mark('notice')}<span class="fixrow__text"><span class="fixrow__title">${esc(TITLE[id])}</span><span class="fixrow__sum">${id === 'noindex-page' ? 'These pages ask search engines to leave them out. That\'s right for pages such as a basket or a thank-you page.' : esc(PLAIN[id].what[0])}</span></span><span class="fixrow__count"><b>${n}</b> of ${num(INVENTED.perfect.pages)} pages</span>${icon('chevron-right', { cls: 'fixrow__chev' })}</a>`).join('\n        ')}
      </section>

      <section class="s-section trio" aria-label="The rest">
        <div class="card">
          <h3 class="trio__title">${icon('circle-check')}What's fine</h3>
          <p class="trio__figure">83<span>of 86 checks found nothing wrong</span></p>
          <p class="trio__text">Every page loads, every link works, and every page can be found and read by search engines.</p>
          <div class="trio__actions"><a class="s-link" href="#">See what was checked${icon('chevron-right', { size: 'sm' })}</a></div>
        </div>
        <div class="card">
          <h3 class="trio__title">${mark('passed', 'md')}Couldn't be checked</h3>
          <p class="trio__figure">0<span>checks couldn't run</span></p>
          <p class="trio__text">Every check ran, on every page the site check found.</p>
        </div>
        ${reportCard}
      </section>
    </div>`;
  return page({
    title: `Summary: ${site}`, file: 'summary-perfect.html', section: 'summary', crawl: { site, meta: 'Checked 28 Oct, 09:30', counts: { fix: 0 } },
    crumbs: crumbsFor(site, '28 Oct 2026, 09:30', 'Summary'), state: { kind: 'done' }, main,
  });
}

// ------------------------------------------------------------------ Summary: a check stopped early
export function summaryStoppedPage() {
  const site = INVENTED.stopped.site, pages = INVENTED.stopped.pages, urls = INVENTED.stopped.urls;
  const checks = [
    { id: 'broken-internal-links', severity: 'error', count: 6 }, { id: 'title-missing', severity: 'error', count: 2 },
    { id: 'image-missing-alt', severity: 'warning', count: 41 }, { id: 'title-duplicate', severity: 'warning', count: 12 }, { id: 'slow-response', severity: 'warning', count: 9 },
  ];
  const s = score({ checks, pages, urls });
  const sums = {
    'title-missing': 'These pages have no title: the text on the browser tab, and the link people click in search results.',
    'image-missing-alt': 'Some images have no description (their "alt text") for people who can\'t see them, or for search engines.',
  };
  const rows = checks.map((c, i) => fixRow({ ...c, plainTitle: TITLE[c.id], plain: PLAIN[c.id] }, { n: i + 1, line: s.lines[i], href: '#', summary: PLAIN[c.id]?.what[0] ?? sums[c.id] }));
  const main = `    <div class="page--simple">
      <div class="s-banner" role="status">
        ${icon('info', { size: 'lg' })}
        <div class="s-banner__text">
          <p class="s-banner__title">This check was stopped early</p>
          <p class="s-banner__body">It read ${num(pages)} pages and still had 1,380 addresses to check. Everything below covers only what it read, so the site may have more problems than these.</p>
        </div>
        <div class="s-banner__actions">${btn('Continue checking', { kind: 'primary', ic: 'play' })}</div>
      </div>
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">${site}</h1>
          <p class="s-sub">Stopped on 15 October 2026 after 6 min 40 s. ${num(pages)} pages read so far.</p>
        </div>
        <div class="s-actions">${btn('Open the report', { ic: 'file-text', size: 'lg' })}</div>
      </header>

      <section class="card hero" aria-label="Score so far">
        <div class="hero__main">
          ${ring(s.value, { partial: true })}
          <div class="hero__text">
            <h2 class="hero__word">${s.band.word}<span class="hero__word-tag">so far</span></h2>
            <p class="hero__sentence">On the pages read so far there are ${counts(2, 3)}.</p>
            <p class="hero__covers">The score covers the ${num(pages)} pages read before the check was stopped, and 79 of 86 checks. 7 checks need a finished check.</p>
            <p class="hero__how"><a class="s-link" href="#">How the score is worked out${icon('chevron-right', { size: 'sm' })}</a></p>
          </div>
        </div>
        ${tally({ errors: 2, warnings: 3, notices: 9, passed: 65, skipped: 7 })}
      </section>

      <section class="s-section card fixlist" aria-labelledby="fix-first">
        <div class="fixlist__head"><h2 class="s-h2" id="fix-first">Fix these first</h2><span class="s-h2__meta">Among the ${num(pages)} pages read so far</span></div>
        ${rows.join('\n        ')}
        <div class="fixlist__foot"><span>Continue the check to see the rest of the site.</span><a class="s-link" href="#">See all 5 problems and 9 things worth a look${icon('chevron-right', { size: 'sm' })}</a></div>
      </section>
    </div>`;
  return page({
    title: `Summary: ${site}`, file: 'summary-stopped.html', section: 'summary', crawl: { site, meta: 'Stopped 15 Oct, 11:02', counts: { fix: 5 } },
    crumbs: crumbsFor(site, '15 Oct 2026, 11:02', 'Summary'), state: { kind: 'stopped' }, main,
  });
}

// ------------------------------------------------------------------ What to fix
export function fixPage() {
  const group = (sev, list, lead, shown = list.length) => `
      <section class="s-section" aria-labelledby="h-${sev}">
        <h2 class="s-h2" id="h-${sev}">${mark(sev)}${SEVERITY[sev].heading}<span class="s-h2__count">${list.length}</span></h2>
        <p class="s-lead">${lead}</p>
        <div class="card fixlist">
          ${list.slice(0, shown).map(p => fixRow(p, { href: isTemplate(p) ? 'problem-template.html' : 'problem.html' })).join('\n          ').replace('border-top', 'border-top')}
          ${shown < list.length ? `<div class="fixlist__foot"><button class="btn btn--ghost" type="button" aria-expanded="false">${icon('chevron-down', { size: 'sm' })}<span class="btn__label">Show all ${list.length} things worth a look</span></button></div>` : ''}
        </div>
      </section>`;
  const skipRow = (id, reason) => `<div class="fixrow fixrow--plain fixrow--quiet">${mark('skipped')}<span class="fixrow__text"><span class="fixrow__title">${esc(TITLE[id])}</span><span class="fixrow__sum">${SKIP[reason]} ${SKIP_ACTION[reason]}</span></span><span class="fixrow__count">Not checked</span><span></span></div>`;
  const main = `    <div class="page--simple">
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">What to fix</h1>
          <p class="s-sub">${toFix.length} problems to fix and ${toLook.length} things worth a look on ${SITE}, from ${totals.checksRun} checks on ${num(facts.pages)} pages.</p>
        </div>
        <div class="s-actions">${btn('Copy this list', { ic: 'copy', size: 'lg' })}${btn('Open the report', { ic: 'file-text', size: 'lg' })}</div>
      </header>
      ${group('error', problems.filter(p => p.severity === 'error'), 'These stop pages working or being found. Fix them first.')}
      ${group('warning', problems.filter(p => p.severity === 'warning'), 'These hold pages back. Fix them when the serious ones are done.')}
      ${group('notice', toLook, 'Often fine as they are. They don\'t change the score: read each one and decide.', 3)}

      <section class="s-section" aria-labelledby="h-skipped">
        <h2 class="s-h2" id="h-skipped">${mark('skipped')}Couldn't check<span class="s-h2__count">${garden.skipped.length}</span></h2>
        <p class="s-lead">These checks didn't run, so the site may have problems they would have found.</p>
        <div class="card fixlist">
          ${garden.skipped.slice().sort((a, b) => a.reason.localeCompare(b.reason)).reverse().map(s => skipRow(s.check_id, s.reason)).join('\n          ')}
        </div>
      </section>

      <section class="s-section" aria-labelledby="h-passed">
        <h2 class="s-h2" id="h-passed">${mark('passed')}Nothing wrong<span class="s-h2__count">${totals.checksPassed}</span></h2>
        <p class="s-lead">These checks ran and found nothing.</p>
        <div class="card fixlist"><div class="fixlist__foot" style="border-top:0"><button class="btn btn--ghost" type="button" aria-expanded="false">${icon('chevron-down', { size: 'sm' })}<span class="btn__label">Show the ${totals.checksPassed} checks that found nothing wrong</span></button></div></div>
      </section>
    </div>`;
  return page({
    title: `What to fix: ${SITE}`, file: 'fix.html', section: 'fix', crawl: gardenCrawl,
    crumbs: gardenCrumbs('What to fix'), state: { kind: 'done' }, main,
  });
}

// ------------------------------------------------------------------ One problem
function problemHead(p, line) {
  const pts = line.points ? `Takes <b>${line.points} points</b> off the score` : 'Doesn\'t change the score';
  return `<a class="back-link" href="fix.html">${icon('arrow-left', { size: 'sm' })}What to fix</a>
      <header class="problem-head">
        <div class="problem-head__top">${badge(p.severity, 'sev-badge--lg')}<a class="s-link" href="advanced-overview.html" title="Opens this check in Advanced, with every column and filter">See it in Advanced${icon('arrow-up-right', { size: 'sm' })}</a></div>
        <h1 class="s-title">${esc(p.plainTitle)}</h1>
        <div class="problem-meta">
          <span class="problem-meta__item">${icon('file-text', { size: 'sm' })}<span>${countOf(line)}</span></span>
          <span class="problem-meta__item">${icon('gauge', { size: 'sm' })}<span>${pts}</span></span>
          <span class="problem-meta__item">${icon('user', { size: 'sm' })}<span>Usually fixed by ${FIXER[p.plain.fixer]}</span></span>
        </div>
      </header>
      <section class="card explain2">
        <div class="explain2__col">
          <h2 class="explain2__title">What's wrong, and why it matters</h2>
          ${p.plain.what.map(t => `<p>${esc(t)}</p>`).join('')}
        </div>
        <div class="explain2__col">
          <h2 class="explain2__title">How to fix it</h2>
          <ol class="steps">${p.plain.fix.map(t => `<li><span>${esc(t)}</span></li>`).join('')}</ol>
        </div>
      </section>`;
}
const doneCard = `<section class="card done-card">
        <div class="done-card__text">
          <p class="done-card__title">Fixed it?</p>
          <p class="done-card__body">Shouting Iguana can't see a fix until it reads the site again. Check again, and this drops off the list if it's gone.</p>
        </div>
        ${btn('Check the site again', { ic: 'refresh', size: 'lg' })}
      </section>`;
const whereActions = `<div class="where__actions">${btn('Copy the list', { ic: 'copy' })}${btn('Save as a spreadsheet', { ic: 'table' })}</div>`;

export function problemPage() {
  const p = problems.find(x => x.id === 'broken-internal-links');
  const line = lineOf(p.id);
  const rows = p.targets.map(t => `<tr>
              <td><span class="addr">${esc(path(t.related))}</span><span class="where__state">${mark('error', 'sm')}404 Not Found</span></td>
              <td>${t.sample.map(s => `<span class="where__page">${esc(pageName(s.title, s.url))}</span><span class="where__sub addr">${esc(path(s.url))}</span>`).join('')}</td>
            </tr>`);
  const main = `    <div class="page--simple">
      ${problemHead(p, line)}
      <section class="card where" aria-labelledby="where">
        <div class="where__head"><h2 class="where__title" id="where">Where it is</h2><span class="where__meta">${p.targetCount} broken addresses, linked from ${p.count} pages</span>${whereActions}</div>
        <table class="where-table">
          <thead><tr><th scope="col">This address doesn't work</th><th scope="col">Linked from</th></tr></thead>
          <tbody>
            ${rows.join('\n            ')}
          </tbody>
        </table>
        <div class="where__foot">All ${p.targetCount} are shown. Addresses are on ${SITE}.</div>
      </section>
      ${doneCard}
    </div>`;
  return page({
    title: `${p.plainTitle}: ${SITE}`, file: 'problem.html', section: 'fix', crawl: gardenCrawl,
    crumbs: gardenCrumbs('What to fix'), state: { kind: 'done' }, main,
  });
}

// A template fault: one thing to fix, not 264.
export function problemTemplatePage() {
  const p = problems.find(x => x.id === 'empty-anchor-text');
  const line = lineOf(p.id);
  const big = p.targets[0];
  const rows = p.targets.map((t, i) => `<tr>
              <td>${path(t.related) === '/' ? '<span class="where__page">The link to your home page</span><span class="where__sub addr">/</span>' : `<span class="where__sub" style="margin:0">A link to</span><span class="addr">${esc(path(t.related))}</span>`}</td>
              <td>${i === 0
    ? `<span class="where__page">${num(t.pages)} pages</span> <span class="tag">${icon('layers', { size: 'xs' })}One fix</span><span class="where__sub">${t.sample.map(s => esc(pageName(s.title, s.url))).join(', ')} and ${num(t.pages - t.sample.length)} more. <a class="s-link" href="#">Show them</a></span>`
    : t.sample.map(s => `<span class="where__page">${esc(pageName(s.title, s.url))}</span><span class="where__sub addr">${esc(path(s.url))}</span>`).join('')}</td>
            </tr>`);
  const main = `    <div class="page--simple">
      ${problemHead(p, line)}
      <section class="onefix" aria-label="One fix">
        ${icon('layers', { size: 'lg' })}
        <div>
          <p class="onefix__title">One fix clears ${num(big.pages)} of these ${num(p.count)} pages</p>
          <p class="onefix__body">The same link has no words on ${num(big.pages)} pages: the link to your home page. That's almost certainly your logo, which sits in the part of the site every page shares. Give the logo image a description once, there, and all ${num(big.pages)} are fixed.</p>
        </div>
      </section>
      <section class="card where" aria-labelledby="where">
        <div class="where__head"><h2 class="where__title" id="where">Where it is</h2><span class="where__meta">${p.targetCount} links with nothing to read, on ${num(p.count)} pages</span>${whereActions}</div>
        <table class="where-table">
          <thead><tr><th scope="col">The link with no words</th><th scope="col">On these pages</th></tr></thead>
          <tbody>
            ${rows.join('\n            ')}
          </tbody>
        </table>
        <div class="where__foot">All ${p.targetCount} links are shown. The full list of ${num(p.count)} pages is in the spreadsheet.</div>
      </section>
      ${doneCard}
    </div>`;
  return page({
    title: `${p.plainTitle}: ${SITE}`, file: 'problem-template.html', section: 'fix', crawl: gardenCrawl,
    crumbs: gardenCrumbs('What to fix'), state: { kind: 'done' }, main,
  });
}

// ------------------------------------------------------------------ Checking a site
export function checkingPage() {
  const L = LIVE;
  const etaMin = Math.ceil(L.queued / L.perSecond / 60);
  const main = `    <div class="page--simple">
      <header class="s-header">
        <div class="s-header__text">
          <h1 class="s-title">Checking ${SITE}</h1>
          <p class="s-sub">Started at 14:13, ${duration(L.elapsedMs)} ago. You can close this window: the check carries on, and a notification tells you when it's done.</p>
        </div>
      </header>

      <section class="card checking" aria-label="Progress">
        <ol class="phases" aria-label="Steps">
          <li class="phase is-now" aria-current="step"><span class="phase__dot">1</span>Reading your site</li>
          <li class="phase__line" aria-hidden="true"></li>
          <li class="phase"><span class="phase__dot">2</span>Working out the results</li>
          <li class="phase__line" aria-hidden="true"></li>
          <li class="phase"><span class="phase__dot">3</span>Your score, what to fix and the report</li>
        </ol>
        <div class="checking__figures">
          <div class="cfig"><p class="cfig__value">${num(L.checked)}</p><p class="cfig__label">addresses checked</p><p class="cfig__context">Pages, images, files and links</p></div>
          <div class="cfig"><p class="cfig__value">${num(L.pages)}</p><p class="cfig__label">pages read</p><p class="cfig__context">Each one is checked for 86 things</p></div>
          <div class="cfig"><p class="cfig__value">${num(L.queued)}</p><p class="cfig__label">still to check</p><p class="cfig__context">It finds more as it reads</p></div>
        </div>
        <div class="checking__bar">
          <div class="stack" role="img" aria-label="${num(L.checked)} checked, ${num(L.queued)} still to check">
            <span class="stack__crawled" style="--n:${L.checked}"></span><span class="stack__queued" style="--n:${L.queued}"></span>
          </div>
        </div>
        <p class="checking__eta">${icon('clock')}<span><b>At least ${etaMin} minutes to go.</b> It's still finding new pages, so this can grow. It's reading at a gentle 2 addresses a second.</span></p>
      </section>

      <div class="duo">
        <section class="card">
          <h2 class="trio__title">${icon('shield-check')}Is anything in the way?</h2>
          <p class="trio__text"><b class="strong">Nothing so far.</b> The site is answering normally. If it slows down or turns the check away, it's said here, with what you can do about it.</p>
        </section>
        <section class="card">
          <h2 class="trio__title">${icon('info')}While you wait</h2>
          <ul class="trio__list">
            <li>${icon('pause', { size: 'sm' })}<span><b>Pause</b> holds the check. <b>Stop</b> ends it and keeps what it found, and you can continue it later. Both are at the top right.</span></li>
            <li>${icon('bell', { size: 'sm' })}<span>Results appear when the check finishes. They can't be worked out from half a site.</span></li>
          </ul>
        </section>
      </div>
    </div>`;
  return page({
    title: `Checking ${SITE}`, file: 'checking.html', section: 'summary',
    crawl: { site: SITE, meta: 'Checking now', counts: {} },
    running: { site: SITE, checked: L.checked, ratio: L.checked / (L.checked + L.queued), current: true },
    crumbs: gardenCrumbs('Summary'), state: { kind: 'running', count: L.checked }, main,
  });
}

// ------------------------------------------------------------------ Home
const launch = (first = false) => `<section class="s-launch" aria-labelledby="launch-title">
        <h1 class="s-launch__title" id="launch-title">Check a website</h1>
        <p class="s-launch__sub">Paste a web address. Shouting Iguana reads the site the way a search engine does, and tells you what to fix in plain words.</p>
        <div class="launch__row">
          <label class="launch-field">
            ${icon('globe', { size: 'lg', cls: 'launch-field__icon' })}
            <span class="sr-only">Web address</span>
            <input class="launch-field__input" type="url" placeholder="www.example.co.uk" autocomplete="off" spellcheck="false">
            <button class="btn btn--primary" type="button">Check site<kbd class="kbd kbd--inline">Enter</kbd></button>
          </label>
          <button class="btn btn--secondary s-launch__options" type="button">${icon('sliders', { size: 'sm' })}Options…</button>
        </div>
        <p class="launch__meta"><span class="launch__meta-item">${icon('check', { size: 'sm' })}Checks the whole site, at a speed that's safe for most hosting.${first ? '' : ' A small site takes a few minutes.'}</span></p>
      </section>`;

function sevParts(e, w, suffix = '') {
  if (!e && !w) return `<span class="sevcount">${mark('passed', 'md')}<span class="sevcount__word">Nothing to fix</span></span>`;
  const one = (sev, n) => n ? `<span class="sevcount">${mark(sev, 'md')}<span class="num">${n}</span> <span class="sevcount__word">${sev === 'error' ? 'serious' : 'worth fixing'}</span></span>` : '';
  return `<span class="sevcounts">${one('error', e)}${one('warning', w)}${suffix ? `<span class="muted">${suffix}</span>` : ''}</span>`;
}

export function homePage() {
  const I = INVENTED;
  const row = ({ site, meta, sc, partial, fix, when, go = icon('chevron-right', { cls: 'fixrow__chev' }) }) => `<a class="site-row" href="summary.html">
            <span class="site-row__site">${favicon(site, 'lg')}<span class="site-cell__text"><span class="site-row__name">${site}</span><span class="site-row__meta">${meta}</span></span></span>
            <span class="site-row__score">${ring(sc.value, { size: 'sm', partial })}<span>${sc.band.word}${partial ? ' so far' : ''}</span></span>
            <span class="site-row__fix">${fix}</span>
            <span class="site-row__when">${when}</span>
            <span class="site-row__go">${go}</span>
          </a>`;
  const stoppedScore = score({ checks: [{ id: 'broken-internal-links', severity: 'error', count: 6 }, { id: 'title-missing', severity: 'error', count: 2 }, { id: 'image-missing-alt', severity: 'warning', count: 41 }, { id: 'title-duplicate', severity: 'warning', count: 12 }, { id: 'slow-response', severity: 'warning', count: 9 }], pages: 312, urls: 2104 });
  const main = `    <div class="page--simple">
      ${launch()}
      <section class="s-section" aria-labelledby="sites">
        <h2 class="s-h2" id="sites">Your sites<span class="s-h2__count">7</span></h2>
        <div class="card sites" style="margin-top:var(--space-3)">
          <div class="site-row site-row--head" aria-hidden="true"><span>Site</span><span>Score</span><span>To fix</span><span>Last checked</span><span class="site-row__go"></span></div>
          <a class="site-row" href="checking.html">
            <span class="site-row__site">${favicon(I.running.site, 'lg')}<span class="site-cell__text"><span class="site-row__name">${I.running.site}</span><span class="site-row__meta">Checking now</span></span></span>
            <span class="site-row__progress"><span class="progress" aria-hidden="true"><span style="--value:${(I.running.checked / I.running.found * 100).toFixed(1)}%"></span></span><span class="site-row__meta">${num(I.running.checked)} addresses checked so far. Results appear when it finishes.</span></span>
            <span class="site-row__go"><span class="live-dot" aria-hidden="true"></span></span>
          </a>
          ${row({ site: SITE, meta: `${num(facts.pages)} pages`, sc: gardenScore, fix: sevParts(2, 4), when: 'Yesterday, 14:13' })}
          ${row({ site: I.perfect.site, meta: `${num(I.perfect.pages)} pages · 2 earlier checks`, sc: I.perfect.score, fix: sevParts(0, 0), when: '28 Oct' })}
          ${row({ site: I.nearlyClean.site, meta: `${I.nearlyClean.pages} pages`, sc: I.nearlyClean.score, fix: sevParts(0, 1), when: '24 Oct' })}
          ${row({ site: I.broken.site, meta: `${num(I.broken.pages)} pages`, sc: I.broken.score, fix: sevParts(7, 6), when: '19 Oct' })}
          ${row({ site: I.stopped.site, meta: `Stopped early · ${I.stopped.pages} pages read`, sc: stoppedScore, partial: true, fix: sevParts(2, 3, 'so far'), when: '15 Oct', go: '<span class="btn btn--secondary btn--sm">Continue</span>' })}
          ${row({ site: I.tiny.site, meta: `${I.tiny.pages} pages`, sc: I.tiny.score, fix: sevParts(1, 0), when: '12 Oct' })}
        </div>
      </section>
    </div>`;
  return page({
    title: 'Home', file: 'home.html', section: 'home', crumbs: [{ label: 'Home' }], main,
    running: { site: I.running.site, checked: I.running.checked, ratio: I.running.checked / I.running.found },
  });
}

export function homeFirstPage() {
  const step = (n, title, text) => `<div class="card how3__step"><span class="how3__n" aria-hidden="true">${n}</span><h3 class="how3__title">${title}</h3><p class="how3__text">${text}</p></div>`;
  const main = `    <div class="page--simple">
      ${launch(true)}
      <section class="how3" aria-label="What happens">
        ${step(1, 'Paste your web address', 'Any page on the site will do. Your home page is the usual place to start.')}
        ${step(2, 'It reads your site', 'A few minutes for a small site, longer for a big one. You can keep working, or close the window: it carries on.')}
        ${step(3, 'You get a score and a to-do list', 'What to fix first, why it matters and how to fix it, in plain words. And a report to keep or send on.')}
      </section>
      <div class="first-foot">
        <span class="first-foot__item">${icon('sliders', { size: 'sm' })}<span>Want every URL, filter and setting? <a class="s-link" href="advanced-overview.html">Switch to Advanced</a>, at the top right.</span></span>
        <span class="first-foot__item">${icon('folder-open', { size: 'sm' })}<span>Have a crawl file already? <a class="s-link" href="#">Open it</a> or drop it here.</span></span>
      </div>
    </div>`;
  return page({ title: 'Home', file: 'home-first.html', section: 'home', crumbs: [{ label: 'Home' }], main });
}
