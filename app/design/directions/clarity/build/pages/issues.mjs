// Issues: the checks by category, and one check selected with what, why, how, and its rows grouped by detail.
import { page, kbd } from '../lib/shell.mjs';
import { icon, sevIcon, severityWord } from '../lib/icons.mjs';
import { esc, num, pct, splitUrl, middle, dateTime } from '../lib/format.mjs';
import { gardenChecks, groupedIssues, checkById, categoryById, gardenCrawl, gardenAnalysis, inspector } from '../lib/data.mjs';

const SELECTED = 'empty-anchor-text';
const START = splitUrl(gardenCrawl.startUrl);
const path = url => {
  const { scheme, host, path: p } = splitUrl(url);
  return scheme === START.scheme && host === START.host ? p : url;
};

// The catalog's description holds what's wrong and why it matters in one sentence ("…, so …").
// The real app needs the two as separate fields; here the sentence is split at ", so".
function whatAndWhy(description) {
  const at = description.indexOf(', so ');
  if (at < 0) return [description, ''];
  const why = description.slice(at + 5);
  return [description.slice(0, at) + '.', why.charAt(0).toUpperCase() + why.slice(1)];
}

function checksList(g) {
  const groups = g.groups.map(cat => `
        <div class="check-group" role="group" aria-labelledby="cat-${cat.id}">
          <div class="check-group__title" id="cat-${cat.id}">${esc(cat.title)}<span>${cat.passed} passed</span></div>
          ${cat.checks.map(c => {
            if (c.skipped) return `
          <a class="check-row check-row--skipped" href="#" aria-label="${esc(c.title)}: skipped. ${esc(c.reason)}">
            <svg class="icon icon--md sev-icon sev-icon--skipped" aria-hidden="true"><use href="#i-sev-skipped"/></svg>
            <span class="check-row__title">${esc(c.title)}</span>
            <span class="badge badge--dashed">Skipped</span>
            <span class="check-row__reason">${esc(c.reason)}</span>
          </a>`;
            const sel = c.id === SELECTED;
            return `
          <a class="check-row${sel ? ' is-selected' : ''}" href="#"${sel ? ' aria-current="true"' : ''}>
            ${sevIcon(c.severity, 'md')}
            <span class="check-row__title">${esc(c.title)}</span>
            <span class="check-row__count">${num(c.count)}</span>
          </a>`;
          }).join('')}
        </div>`).join('');

  return `
      <section class="card checks" aria-label="Checks">
        <div class="checks__head">
          <label class="field">${icon('search', { size: 'sm' })}<input class="field__input" type="search" placeholder="Filter checks" aria-label="Filter checks"></label>
          <div class="segmented" role="radiogroup" aria-label="Severity">
            <button class="segmented__item" type="button" role="radio" aria-checked="true">All <span class="num">${g.withIssues}</span></button>
            <button class="segmented__item" type="button" role="radio" aria-checked="false">${sevIcon('error')}<span class="sr-only">Errors</span><span class="num">${g.bySeverity.error}</span></button>
            <button class="segmented__item" type="button" role="radio" aria-checked="false">${sevIcon('warning')}<span class="sr-only">Warnings</span><span class="num">${g.bySeverity.warning}</span></button>
            <button class="segmented__item" type="button" role="radio" aria-checked="false">${sevIcon('notice')}<span class="sr-only">Notices</span><span class="num">${g.bySeverity.notice}</span></button>
          </div>
        </div>
        <div class="checks__list">${groups}
        </div>
        <div class="checks__foot">
          <button class="switch" type="button" role="switch" aria-checked="false"><span class="switch__track" aria-hidden="true"></span>Show the ${g.passed} checks that passed</button>
        </div>
      </section>`;
}

function detail(g) {
  const c = checkById[SELECTED];
  const { total, groups } = groupedIssues(SELECTED);
  const [what, why] = whatAndWhy(c.description);
  const pages = gardenAnalysis.pagesAnalyzed;

  // Evidence for the big group, from the inspector fixtures: the link sits in the page header.
  const product = inspector('product');
  const headerLink = product.outlinks.rows.find(r => r.kind === 'hyperlink' && !r.anchorText && r.position === 'header');
  // The other group: the home page's image links without alt text.
  const home = inspector('home');
  const imageLinks = home.outlinks.rows.filter(r => r.kind === 'hyperlink' && !r.anchorText);

  const [big, small] = groups;
  const shareBar = n => `<div class="group-share"><span class="bar-track"><span class="bar-fill" style="--value:${((n / total) * 100).toFixed(2)}%;--min:var(--bar-min)"></span></span><span class="group-share__pct">${pct(n, total)}</span></div>`;
  const children = big.urls.slice(0, 3).map(u => `
              <tr class="group-child">
                <td colspan="2"><span class="url" title="${esc(u)}"><span class="url__path">${esc(path(u))}</span></span></td>
                <td></td>
                <td class="row-end"><button class="icon-btn icon-btn--sm" type="button" aria-label="Inspect ${esc(path(u))}" title="Inspect (Enter)">${icon('chevron-right', { size: 'sm' })}</button></td>
              </tr>`).join('');

  return `
      <section class="card check-detail" aria-labelledby="check-title">
        <div class="check-detail__head">
          <div class="check-detail__meta">
            <span class="sev-badge sev-badge--${c.severity}">${sevIcon(c.severity)}${severityWord[c.severity]}</span>
            <span>${esc(categoryById[c.category].title)}</span>
          </div>
          <div class="check-detail__title-row">
            <h2 class="check-detail__title" id="check-title">${esc(c.title)}</h2>
            <div class="card__actions">
              <button class="btn btn--secondary btn--sm" type="button">${icon('copy', { size: 'sm' })}Copy URLs</button>
              <button class="btn btn--secondary btn--sm" type="button" title="Export this check (Ctrl+E)">${icon('download', { size: 'sm' })}Export</button>
              <button class="icon-btn icon-btn--sm" type="button" aria-label="Hide this check" title="Hide this check">${icon('eye-off', { size: 'sm' })}</button>
            </div>
          </div>
          <div class="check-figures">
            <div><div class="check-figure__value">${num(total)}</div><div class="check-figure__label">pages affected</div></div>
            <div><div class="check-figure__value">${pct(total, pages)}</div><div class="check-figure__label">of the ${num(pages)} pages crawled</div></div>
            <div><div class="check-figure__value">${groups.length}</div><div class="check-figure__label">groups, by detail and linked URL</div></div>
          </div>
        </div>

        <div class="explain">
          <div><h3 class="explain__title">What’s wrong</h3><p class="explain__text">${esc(what)}</p></div>
          <div><h3 class="explain__title">Why it matters</h3><p class="explain__text">${esc(why)}</p></div>
          <div><h3 class="explain__title">How to fix it</h3><p class="explain__text">${esc(c.fix)}</p></div>
        </div>

        <div class="evidence">
          <div class="evidence__head">
            <h3 class="evidence__title">Affected pages</h3>
            <span class="evidence__meta">${num(total)} issues in ${groups.length} groups</span>
            <div class="card__actions">
              <button class="btn btn--ghost btn--sm" type="button">${icon('layers', { size: 'sm' })}Grouped by detail and linked URL${icon('chevron-down', { size: 'sm' })}</button>
            </div>
          </div>
          <div class="groups">
            <table class="grid" role="treegrid" aria-label="Affected pages, grouped">
              <colgroup><col style="width:34%"><col style="width:30%"><col style="width:12%"><col style="width:24%"></colgroup>
              <thead><tr><th scope="col">Detail</th><th scope="col">Linked URL</th><th scope="col" class="num">Pages</th><th scope="col">Share of issues</th></tr></thead>
              <tbody>
              <tr class="group-row is-open" aria-expanded="true" aria-level="1">
                <td><span class="chev">${icon('chevron-right', { size: 'sm' })}</span>${esc(big.detail)}</td>
                <td><span class="url"><span class="url__path">${esc(path(big.relatedUrl))}</span></span> <span class="muted">the home page</span></td>
                <td class="num">${num(big.urls.length)}</td>
                <td>${shareBar(big.urls.length)}</td>
              </tr>
              <tr class="group-insight"><td colspan="4">
                <div class="insight">${icon('layers', { size: 'sm' })}<span>The same link on ${num(big.urls.length)} of ${num(pages)} pages, so it’s likely in the site’s template: one fix clears the group. On <code>${esc(path(product.summary.url))}</code> it’s a link in the ${esc(headerLink.position)} to <code>${esc(path(headerLink.toUrl))}</code> with no text, alt text or aria-label.</span></div>
              </td></tr>${children}
              <tr class="group-more"><td colspan="4">
                <a class="link" href="urls.html">Show all ${num(big.urls.length)} pages in URLs</a>
                <span class="chip chip--preview chip--sm"><span class="chip__field">Check</span><span class="chip__op">is</span><span class="chip__value">${esc(c.title)}</span></span>
                <span class="chip chip--preview chip--sm"><span class="chip__field">Linked URL</span><span class="chip__op">is</span><span class="chip__value">/</span></span>
              </td></tr>
              <tr class="group-row" aria-expanded="false" aria-level="1">
                <td><span class="chev">${icon('chevron-right', { size: 'sm' })}</span>${esc(small.detail)}</td>
                <td><span class="url"><span class="url__path">${esc(path(small.relatedUrl))}</span></span> <span class="muted">and ${imageLinks.length - 1} more</span></td>
                <td class="num">${num(small.urls.length)}</td>
                <td>${shareBar(small.urls.length)}</td>
              </tr>
              </tbody>
            </table>
          </div>
          <p class="evidence__note">The ${imageLinks.length} links in the second group are images without alt text on <span class="url"><span class="url__path">${esc(path(small.urls[0]))}</span></span>, the home page. One of them points at a 404.</p>
        </div>
      </section>`;
}

export function issuesPage() {
  const g = gardenChecks();
  const main = `
    <div class="page page--fill page--grid">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">Issues</h1>
          <p class="page-subtitle">${g.withIssues} checks found ${num(g.totalIssues)} issues on this crawl. ${g.passed} passed and ${g.skipped} were skipped.</p>
        </div>
        <div class="page-header__actions">
          <button class="btn btn--secondary" type="button" title="Export every check (Ctrl+E)">${icon('download', { size: 'sm' })}Export all checks</button>
        </div>
      </header>
      <div class="issues-layout">
        ${checksList(g)}
        ${detail(g)}
      </div>
    </div>`;

  return page({
    title: 'Issues, gardenfurnituresets.co.uk', file: 'issues.html', section: 'issues', crawlState: 'done',
    crumbs: [
      { label: 'gardenfurnituresets.co.uk', favicon: 'gardenfurnituresets.co.uk', strong: true, href: 'live.html' },
      { label: dateTime(gardenCrawl.startedAt), href: 'live.html' },
      { label: 'Issues' },
    ],
    main,
  });
}
