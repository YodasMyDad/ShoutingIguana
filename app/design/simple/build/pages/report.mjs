// The report: the one document a crawl ends in. In the app the ENGINE writes this file; this is its design, on the
// garden crawl. One self-contained HTML file: inline CSS, inline SVG, no script, no web font, light only.
// It prints to A4 and Letter from the system print dialog, which is also how it becomes a PDF.
import { esc, num, duration } from '../lib/ui.mjs';
import { drawnBodies } from '../lib/icons.mjs';
import { SEVERITY, FIXER, SKIP, SKIP_ACTION, TITLE, PASSED, CATEGORY } from '../lib/words.mjs';
import { POINTS, EXTENTS, SCORE_VERSION, SERIOUS_CEILING } from '../lib/score.mjs';
import { garden, SITE, problems, facts, totals, gardenScore, lineOf, toFix, toLook, fixFirst, scoreAfterFirst, path, pageName } from '../lib/data.mjs';

export const EXAMPLES = { error: 5, warning: 5, notice: 3 };   // the first pages of each problem; the rest are in the workbook

// The severity shapes as standalone SVG: the clarity light values, as the report has no theme.
const COLOURS = { '--color-error': '#D93636', '--color-warning': '#F5A70A', '--color-warning-edge': '#B06C00', '--color-warning-glyph': '#3A2600', '--color-notice': '#5F6B79', '--color-surface': '#FFFFFF' };
const shape = (sev, size = 14) => {
  let body = drawnBodies[`sev-${sev}`];
  for (const [k, v] of Object.entries(COLOURS)) body = body.replaceAll(`var(${k})`, v);
  const stroke = sev === 'skipped' || sev === 'passed' ? ' fill="none" stroke="#68686F" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"' : ' fill="none" stroke-linecap="round" stroke-linejoin="round"';
  return `<svg class="mark" width="${size}" height="${size}" viewBox="0 0 24 24" role="img" aria-label="${SEVERITY[sev]?.word ?? sev}"${stroke}>${body}</svg>`;
};
const ringSvg = (value, size = 112) => {
  const r = 44, c = 2 * Math.PI * r;
  return `<svg class="ring" width="${size}" height="${size}" viewBox="0 0 100 100" role="img" aria-label="Score ${value} out of 100">
      <circle cx="50" cy="50" r="${r}" fill="none" stroke="#EEEEF1" stroke-width="7"/>
      <circle cx="50" cy="50" r="${r}" fill="none" stroke="#1F86F5" stroke-width="7" stroke-linecap="round" stroke-dasharray="${(c * value / 100).toFixed(1)} ${c.toFixed(1)}" transform="rotate(-90 50 50)"/>
      <text x="50" y="52" text-anchor="middle" dominant-baseline="middle" font-size="34" font-weight="500" fill="#18181B" letter-spacing="-1">${value}</text>
      <text x="50" y="72" text-anchor="middle" font-size="8.5" fill="#68686F">out of 100</text>
    </svg>`;
};

const CSS = `
  :root { --text: #18181B; --secondary: #46464D; --muted: #68686F; --rule: #E3E3E7; --rule-soft: #EEEEF1; --sunken: #FAFAFB; --accent: #0B6BD3; --accent-subtle: #EBF4FF; --accent-border: #A8CFFF; }
  * { box-sizing: border-box; }
  html { -webkit-text-size-adjust: 100%; }
  body { margin: 0; background: #fff; color: var(--text); font: 10pt/1.45 Inter, "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif; -webkit-font-smoothing: antialiased; }
  .sheet { max-width: 760px; margin: 0 auto; padding: 44px 40px 64px; }
  h1, h2, h3, h4, p, ul, ol, dl, dd, figure, table { margin: 0; }
  ul, ol { padding: 0; list-style: none; }
  a { color: var(--accent); text-decoration: none; }
  .mono { font-family: "JetBrains Mono", "Cascadia Mono", Consolas, monospace; font-size: 0.88em; overflow-wrap: anywhere; }
  .muted { color: var(--muted); }
  .num { font-variant-numeric: tabular-nums; }
  .mark { flex: none; vertical-align: -2px; }
  .ring { flex: none; }

  .eyebrow { font-size: 9pt; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }
  h1 { margin-top: 4px; font-size: 24pt; line-height: 1.15; font-weight: 600; letter-spacing: -0.02em; overflow-wrap: anywhere; }
  .meta { margin-top: 8px; color: var(--secondary); }
  h2 { margin: 20px 0 4px; font-size: 14pt; line-height: 1.25; font-weight: 600; letter-spacing: -0.01em; }
  h2 + .lead { margin-bottom: 12px; color: var(--secondary); }
  .lead { max-width: 62ch; }

  .summary { display: grid; grid-template-columns: 1.5fr 1fr; margin-top: 18px; border: 1px solid var(--rule); border-radius: 10px; }
  .summary__main { display: flex; gap: 20px; align-items: center; padding: 18px 22px; }
  .summary__word { font-size: 17pt; line-height: 1.2; font-weight: 600; letter-spacing: -0.015em; }
  .summary__sentence { margin-top: 4px; font-size: 11.5pt; line-height: 1.4; }
  .summary__covers { margin-top: 8px; font-size: 9.5pt; color: var(--secondary); }
  .tally { padding: 16px 20px; border-left: 1px solid var(--rule-soft); display: grid; align-content: center; gap: 7px; font-size: 10pt; color: var(--secondary); }
  .tally li { display: grid; grid-template-columns: 16px 30px 1fr; gap: 8px; align-items: center; }
  .tally b { text-align: right; font-size: 12pt; font-weight: 500; color: var(--text); font-variant-numeric: tabular-nums; }

  .first { margin-top: 8px; border: 1px solid var(--rule); border-radius: 10px; }
  .first li { display: grid; grid-template-columns: 18px 16px 1fr auto; gap: 10px; align-items: baseline; padding: 6px 14px; border-top: 1px solid var(--rule-soft); }
  .first li:first-child { border-top: 0; }
  .first__n { color: var(--muted); text-align: right; font-variant-numeric: tabular-nums; }
  .first__title { font-weight: 600; }
  .first__count { color: var(--secondary); white-space: nowrap; font-variant-numeric: tabular-nums; }
  .first__foot { margin-top: 6px; font-size: 9pt; color: var(--secondary); }

  table { width: 100%; border-collapse: collapse; font-size: 9.5pt; line-height: 1.4; }
  th { padding: 6px 10px; text-align: left; font-size: 8.5pt; font-weight: 600; color: var(--muted); border-bottom: 1px solid var(--rule); }
  td { padding: 5px 10px; vertical-align: top; border-bottom: 1px solid var(--rule-soft); }
  td.num, th.num { text-align: right; white-space: nowrap; }
  tfoot td { border-bottom: 0; font-weight: 600; font-size: 10.5pt; }
  .score-note { margin-top: 6px; font-size: 9pt; color: var(--secondary); }

  .group-title { display: flex; align-items: center; gap: 8px; margin: 20px 0 0; padding-bottom: 6px; border-bottom: 2px solid var(--text); font-size: 12pt; font-weight: 600; }
  .group-title span { font-weight: 400; color: var(--muted); }
  .problem { padding: 13px 0 14px; border-bottom: 1px solid var(--rule); }
  .problem h3 { display: flex; align-items: baseline; gap: 8px; font-size: 11.5pt; line-height: 1.3; font-weight: 600; letter-spacing: -0.005em; }
  .problem__tech { margin-left: auto; padding-left: 12px; font-size: 8.5pt; font-weight: 400; letter-spacing: 0; color: var(--muted); white-space: nowrap; }
  .problem__meta { margin-top: 2px; font-size: 9pt; color: var(--muted); }
  .problem__meta b { color: var(--text); font-weight: 600; }
  .problem h4 { margin: 8px 0 1px; font-size: 9pt; font-weight: 600; }
  .problem p, .problem .steps { font-size: 9.5pt; line-height: 1.42; }
  .problem .problem__meta, .problem .more { font-size: 9pt; }
  .problem .more { font-size: 8.5pt; }
  .steps { counter-reset: s; display: grid; gap: 1px; }
  .steps li { counter-increment: s; display: grid; grid-template-columns: 16px 1fr; gap: 6px; }
  .steps li::before { content: counter(s) "."; color: var(--muted); font-variant-numeric: tabular-nums; }
  .onefix { margin-top: 10px; padding: 9px 12px; border: 1px solid var(--accent-border); border-radius: 8px; background: var(--accent-subtle); font-size: 9.5pt; }
  .onefix b { font-weight: 600; }
  .examples { margin-top: 8px; font-size: 9pt; }
  .examples td, .examples th { padding: 3.5px 8px; }
  .examples caption { caption-side: top; padding: 0 0 2px; text-align: left; font-size: 9pt; font-weight: 600; }
  .examples td:first-child { width: 56%; }
  .problem__split { display: grid; grid-template-columns: 1.2fr 1fr; gap: 22px; }
  .problem__split .examples { margin-top: 8px; }
  .examples--one td:first-child { width: auto; }
  .examples--one .found { display: block; color: var(--muted); }
  .more { margin-top: 4px; font-size: 8.5pt; color: var(--muted); }

  .plain-list li { display: grid; grid-template-columns: 16px 1fr; gap: 8px; padding: 7px 0; border-bottom: 1px solid var(--rule-soft); }
  .plain-list b { font-weight: 600; }
  .passed { display: grid; grid-template-columns: 1fr 1fr; gap: 0 28px; padding-top: 9px; font-size: 9.5pt; }
  .passed li { display: grid; grid-template-columns: 14px 1fr; gap: 7px; padding: 2px 0; }
  .passed h4 { margin: 0 0 2px; font-size: 8.5pt; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: 0.05em; }
  .facts { display: grid; grid-template-columns: 200px 1fr; font-size: 9.5pt; }
  .facts dt, .facts dd { padding: 5px 0; border-bottom: 1px solid var(--rule-soft); }
  .facts dt { color: var(--muted); }
  .foot { margin-top: 32px; padding-top: 10px; border-top: 1px solid var(--rule); font-size: 8.5pt; color: var(--muted); display: flex; justify-content: space-between; gap: 16px; }

  @page { margin: 16mm 15mm 16mm; }
  @media print {
    .sheet { max-width: none; padding: 0; }
    h2, h3, h4, .group-title { break-after: avoid; }
    .summary, .first, .problem--compact, .keep, .passed, .plain-list li, tr, .onefix, .facts { break-inside: avoid; }
    .new-page { break-before: page; }
    .new-page > h2:first-child { margin-top: 0; padding-top: 2px; }
    a { color: inherit; }
  }
`;

const countOf = l => l.unit === 'site' ? 'the whole site' : `${num(l.count)} of ${num(l.of)} ${l.unit}`;
const isTemplate = p => p.targets?.[0] && p.targets[0].pages >= 10 && p.targets[0].pages / p.count >= 0.5;
const SHEET = { 'broken-internal-links': 'Links to broken pages', 'internal-client-error': 'Pages not found' };

function example(p, r) {
  const related = r.related ? ` → <span class="mono" style="display:inline">${esc(path(r.related))}</span>` : '';
  const detail = (r.detail ?? '').replace('HTTP 404', '404 Not Found');
  return `<tr><td><span class="mono">${esc(path(r.url))}</span></td><td>${esc(detail)}${related}</td></tr>`;
}

// n: the problem's number, in "Fix first" order. Page 1's list, this section and the workbook's sheet share it.
function problem(p, n) {
  const l = lineOf(p.id);
  const rows = p.rows.slice(0, EXAMPLES[p.severity]);
  const more = p.count - rows.length;
  const big = p.targets?.[0];
  const compact = p.severity === 'notice';   // things worth a look: the text beside the examples, to save paper
  const caption = more > 0 ? `The first ${rows.length} of ${num(p.count)}` : p.count === 1 ? 'Where it is' : `All ${p.count}`;
  const text = `<h4>What's wrong, and why it matters</h4>
        <p>${p.plain.what.map(esc).join(' ')}</p>
        <h4>How to fix it</h4>
        <ol class="steps">${p.plain.fix.map(t => `<li><span>${esc(t)}</span></li>`).join('')}</ol>`;
  const table = compact
    ? `<table class="examples examples--one">
          <caption>${caption}</caption>
          <tbody>${rows.map(r => `<tr><td><span class="mono">${esc(path(r.url))}</span>${r.detail ? `<span class="found">${esc(r.detail)}${r.related ? ` → <span class="mono">${esc(path(r.related))}</span>` : ''}</span>` : ''}</td></tr>`).join('')}</tbody>
        </table>`
    : `<table class="examples">
          <caption>${caption}</caption>
          <thead><tr><th scope="col">${l.unit === 'addresses' ? 'Address' : 'Page'}</th><th scope="col">What was found</th></tr></thead>
          <tbody>${rows.map(r => example(p, r)).join('')}</tbody>
        </table>`;
  const tail = more > 0 ? `<p class="more">And ${num(more)} more: the full list is on sheet ${n} of the spreadsheet.</p>` : '';
  const onefix = isTemplate(p) ? `<p class="onefix"><b>One fix clears ${num(big.pages)} of these ${num(p.count)} pages.</b> They share one link with no words: the link to <span class="mono">${esc(path(big.related))}</span>, most likely the logo in the part of the site every page shares.</p>` : '';
  const head = `<h3>${shape(p.severity)}<span>${n}. ${esc(p.plainTitle)}</span><span class="problem__tech">${esc(p.title)} · ${SEVERITY[p.severity].technical}</span></h3>
      <p class="problem__meta"><b>${countOf(l)}</b> · ${l.points ? `takes ${l.points} points off the score` : 'doesn’t change the score'} · usually fixed by ${FIXER[p.plain.fixer]}</p>`;
  // A page may break between a problem's text and its examples, never inside either.
  return compact
    ? `<article class="problem problem--compact" data-unit>
      ${head}
      <div class="problem__split"><div>${text}${onefix}</div><div>${table}${tail}</div></div>
    </article>`
    : `<article class="problem">
      <div class="keep" data-unit>${head}
      ${text}${onefix}</div>
      <div class="keep" data-unit>${table}${tail}</div>
    </article>`;
}

export function reportDocument() {
  const s = gardenScore;
  const lead = { 'very-good': 'This site is in very good shape.', good: 'This site is in good shape.', 'needs-work': 'This site needs some work.', poor: 'This site has problems that will hold it back in search.' }[s.band.id];
  const byCategory = {};
  for (const p of garden.passed) (byCategory[p.category] ??= []).push(p.id);
  // Two columns of categories, a pair to a row, so a page can break between rows.
  const blocks = Object.entries(byCategory).map(([cat, ids]) => `<div><h4>${CATEGORY[cat]}</h4><ul>${ids.map(id => `<li>${shape('passed', 12)}<span>${esc(PASSED[id])}</span></li>`).join('')}</ul></div>`);
  const passedRows = [];
  for (let i = 0; i < blocks.length; i += 2) passedRows.push(`<div class="passed" data-unit>${blocks[i]}${blocks[i + 1] ?? '<div></div>'}</div>`);
  const group = (sev, list, text) => `
    <h3 class="group-title" data-unit data-keep>${shape(sev, 16)}${SEVERITY[sev].heading} <span>${list.length}</span></h3>
    <p class="lead muted" data-unit data-keep style="margin-top:6px">${text}</p>
    ${list.map(p => problem(p, problems.indexOf(p) + 1)).join('\n    ')}`;
  return `<!doctype html>
<html lang="en-GB">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Website check: ${SITE}, 31 October 2026</title>
<style>${CSS}</style>
</head>
<body>
<main class="sheet">
  <header data-unit>
    <p class="eyebrow">Website check</p>
    <h1>${SITE}</h1>
    <p class="meta">Checked on 31 October 2026 · ${num(facts.pages)} pages read · ${num(facts.urlsChecked)} addresses checked · ${totals.checksRun} of ${totals.checksInCatalog} checks run</p>
  </header>

  <section class="summary" data-unit aria-label="Summary">
    <div class="summary__main">
      ${ringSvg(s.value)}
      <div>
        <p class="summary__word">${s.band.word}</p>
        <p class="summary__sentence">${lead} There are ${totals.checks.error} serious problems and ${totals.checks.warning} problems worth fixing.</p>
        <p class="summary__covers">The score covers all ${num(facts.pages)} pages and ${totals.checksRun} of ${totals.checksInCatalog} checks. ${garden.skipped.length} checks couldn't run: they are listed near the end.</p>
      </div>
    </div>
    <ul class="tally">
      <li>${shape('error')}<b>${totals.checks.error}</b><span>Serious problems</span></li>
      <li>${shape('warning')}<b>${totals.checks.warning}</b><span>Problems worth fixing</span></li>
      <li>${shape('notice')}<b>${totals.checks.notice}</b><span>Things worth a look</span></li>
      <li>${shape('passed')}<b>${totals.checksPassed}</b><span>Checks found nothing wrong</span></li>
      <li>${shape('skipped')}<b>${garden.skipped.length}</b><span>Checks couldn't run</span></li>
    </ul>
  </section>

  <h2 data-unit data-keep>Fix these first</h2>
  <ol class="first" data-unit>
    ${fixFirst.map((p, i) => `<li><span class="first__n">${i + 1}</span>${shape(p.severity)}<span class="first__title">${esc(p.plainTitle)}</span><span class="first__count">${countOf(lineOf(p.id))}</span></li>`).join('\n    ')}
  </ol>
  <p class="first__foot" data-unit>Fixing these five takes the score from ${s.value} to ${scoreAfterFirst}. Each is explained from page 2 under the same number, with the first pages it's on.</p>

  <h2 data-unit data-keep>How the score is worked out</h2>
  <p class="lead" data-unit data-keep>The score starts at 100, and each check that found a problem takes points off: up to 10 for a serious problem and up to 4 for one worth fixing, by how much of the site it affects. Things worth a look take nothing off.</p>
  <table data-unit style="margin-top:10px">
    <thead><tr><th scope="col">What took points off</th><th scope="col">How much of the site</th><th scope="col" class="num">Points</th></tr></thead>
    <tbody>
      ${toFix.map(p => { const l = lineOf(p.id); return `<tr><td>${shape(p.severity, 12)} ${esc(p.plainTitle)}</td><td>${countOf(l)}: ${l.extentLabel}</td><td class="num">−${l.points}</td></tr>`; }).join('\n      ')}
      <tr><td class="muted">${shape('notice', 12)} ${toLook.length} things worth a look</td><td class="muted">They don't change the score</td><td class="num muted">0</td></tr>
    </tbody>
    <tfoot><tr><td>Score</td><td>100 − ${s.lost}</td><td class="num">${s.value}</td></tr></tfoot>
  </table>
  <p class="score-note" data-unit>A few pages is under 1 in 100; some, under 1 in 10; many, under half; most, half or more. While any serious problem stands, the score can't be above ${SERIOUS_CEILING}. Score version ${SCORE_VERSION}.</p>

  <div class="new-page">
  <h2 data-unit data-break data-keep>What to fix</h2>
  <p class="lead" data-unit data-keep>Every check that found something: what's wrong, why it matters, how to fix it, and the first pages it's on. The full lists are in the spreadsheet that goes with this report, on the sheet with the same number.</p>
  ${group('error', problems.filter(p => p.severity === 'error'), 'These stop pages working or being found. Fix them first.')}
  ${group('warning', problems.filter(p => p.severity === 'warning'), 'These hold pages back. Fix them when the serious ones are done.')}
  ${group('notice', toLook, 'Often fine as they are. They don\'t change the score: read each one and decide.')}
  </div>

  <h2 data-unit data-keep>What couldn't be checked</h2>
  <p class="lead" data-unit data-keep>These checks didn't run, so the site may have problems they would have found.</p>
  <ul class="plain-list">
    ${garden.skipped.map(k => `<li data-unit>${shape('skipped')}<span><b>${esc(TITLE[k.check_id])}.</b> ${SKIP[k.reason]} ${SKIP_ACTION[k.reason]}</span></li>`).join('\n    ')}
  </ul>

  <h2 data-unit data-keep>What was checked and found nothing wrong</h2>
  <p class="lead" data-unit data-keep>${totals.checksPassed} checks ran and found nothing wrong.</p>
  ${passedRows.join('\n  ')}

  <h2 data-unit data-keep>About this check</h2>
  <dl class="facts" data-unit>
    <dt>Started from</dt><dd class="mono">${esc(facts.startUrl)}</dd>
    <dt>When</dt><dd>31 October 2026, 14:13 to 14:51 (${duration(new Date(facts.finishedAt) - new Date(facts.startedAt))})</dd>
    <dt>What was read</dt><dd>${num(facts.pages)} pages, and ${num(facts.urlsChecked - facts.pages)} images, files and links: ${num(facts.urlsChecked)} addresses in all</dd>
    <dt>Pages that can appear in search</dt><dd>${num(facts.indexablePages)} of ${num(facts.pages)}</dd>
    <dt>Not followed</dt><dd>${num(facts.notFollowed)} addresses the site asks search engines not to follow (“nofollow”)</dd>
    <dt>How it was read</dt><dd>Polite speed (up to 2 addresses a second), stopping after 10,000 addresses, obeying the site's rules file (robots.txt)</dd>
    <dt>Finished?</dt><dd>Yes: it read every address it found</dd>
    <dt>The words in this report</dt><dd>Serious is the checks' “Error”, Worth fixing is “Warning” and Worth a look is “Notice”. An address is a URL.</dd>
  </dl>

  <footer class="foot" data-unit><span>Made with Shouting Iguana 2.0 on 1 November 2026</span><span>Score version ${SCORE_VERSION} · engine ${esc(facts.engineVersion)}</span></footer>
</main>
</body>
</html>
`;
}
