// URLs: the one grid, with the Overview column preset, filter chips, the count, a sort and the inspector.
import { page, kbd, favicon } from '../lib/shell.mjs';
import { icon, sevIcon, severityPlural } from '../lib/icons.mjs';
import { esc, num, bytes, middle, splitUrl, urlHtml, dateTime, dateShort, time } from '../lib/format.mjs';
import { urlGrid, urls, inspector, gardenCrawl, checkById } from '../lib/data.mjs';

const START = splitUrl(gardenCrawl.startUrl);

// Internal URLs on the start URL's scheme and host show their path; the host is in the column header.
// Any other host or scheme (other sites, or the site's other versions) shows in full.
function urlCell(url, budget) {
  const { scheme, host, path } = splitUrl(url);
  if (scheme === START.scheme && host === START.host)
    return `<span class="url" title="${esc(url)}"><span class="url__path">${esc(middle(path, budget))}</span></span>`;
  return urlHtml(url, { budget, showScheme: true });
}

// The full URL, allowed to wrap after each slash and hyphen.
function wrappingUrl(url) {
  const { scheme, host, path } = splitUrl(url);
  const breakable = t => esc(t).replace(/([/-])(?=.)/g, '$1<wbr>');
  return `<span class="url" title="${esc(url)}"><span class="url__scheme">${esc(scheme)}</span><span class="url__host">${esc(host)}</span><span class="url__path">${breakable(path)}</span></span>`;
}

const reasonWords = { none: 'Yes', noindex: 'No: noindex', canonicalised: 'No: canonicalised', redirected: 'No: redirects', clientError: 'No: 4xx', serverError: 'No: 5xx', blockedByRobots: 'No: robots.txt', blocked: 'No: blocked', unreachable: 'No: no response' };

const COLUMNS = [
  { id: 'url', label: 'URL', width: 280, pin: true },
  { id: 'status', label: 'Status', width: 84 },
  { id: 'indexable', label: 'Indexable', width: 104 },
  { id: 'issues', label: 'Issues', width: 84, num: true, sort: 'desc' },
  { id: 'title', label: 'Title', width: 300 },
  { id: 'words', label: 'Words', width: 84, num: true },
  { id: 'response', label: 'Response', width: 100, num: true },
  { id: 'depth', label: 'Depth', width: 72, num: true },
  { id: 'inlinks', label: 'Inlinks', width: 84, num: true },
  { id: 'size', label: 'Size', width: 88, num: true },
];

function headerRow() {
  return `<tr>${COLUMNS.map((c, i) => {
    const sort = c.sort ? ` aria-sort="${c.sort === 'desc' ? 'descending' : 'ascending'}"` : '';
    const label = c.id === 'url'
      ? `URL <span class="muted weight-regular">on ${esc(START.host)}</span>`
      : c.label;
    const sortMark = c.sort ? `${icon(c.sort === 'desc' ? 'arrow-down' : 'arrow-up', { size: 'xs', cls: 'th__sort' })}` : '';
    return `<th scope="col" class="${c.num ? 'num' : ''}${c.pin ? ' pin' : ''}"${sort} aria-colindex="${i + 1}"><span class="th__inner">${label}${sortMark}</span></th>`;
  }).join('')}</tr>`;
}

function row(r, i, selectedId) {
  const sel = r.id === selectedId;
  const status = r.statusClass === '2xx'
    ? `<span class="code s2xx">${r.statusCode}</span>`
    : `<span class="code code--flag s${r.statusClass === 'noResponse' ? 'none' : r.statusClass}">${r.statusCode ?? 'None'}</span>`;
  const idx = r.indexable ? '<span class="text-2">Yes</span>' : `<span>${reasonWords[r.indexabilityReason] ?? 'No'}</span>`;
  const [sizeV, sizeU] = bytes(r.sizeBytes, { parts: true });
  return `<tr aria-rowindex="${i + 2}"${sel ? ' class="is-selected" aria-selected="true"' : ' aria-selected="false"'}>
    <td class="pin">${urlCell(r.url, 38)}</td>
    <td>${status}</td>
    <td>${idx}</td>
    <td class="num">${num(r.issueCount)}</td>
    <td title="${esc(r.title)}">${esc(r.title)}</td>
    <td class="num">${num(r.wordCount)}</td>
    <td class="num">${num(r.responseMs)}<span class="u">ms</span></td>
    <td class="num">${num(r.depth)}</td>
    <td class="num">${num(r.inlinks)}</td>
    <td class="num">${sizeV}<span class="u">${sizeU}</span></td>
  </tr>`;
}

// Skeleton cells: shaped like the content they stand in for.
function skeletonRow(i) {
  const w = (a, b) => `${a + ((i * 37 + b * 11) % (b - a + 1))}%`;
  return `<tr aria-rowindex="${i + 2}">
    <td class="pin"><span class="sk sk--text" style="width:${w(42, 88)}"></span></td>
    <td><span class="sk sk--pill"></span></td>
    <td><span class="sk sk--text" style="width:34%"></span></td>
    <td class="num"><span class="sk sk--text" style="width:22%;margin-left:auto"></span></td>
    <td><span class="sk sk--text" style="width:${w(48, 92)}"></span></td>
    <td class="num"><span class="sk sk--text" style="width:${w(36, 52)};margin-left:auto"></span></td>
    <td class="num"><span class="sk sk--text" style="width:${w(44, 60)};margin-left:auto"></span></td>
    <td class="num"><span class="sk sk--text" style="width:22%;margin-left:auto"></span></td>
    <td class="num"><span class="sk sk--text" style="width:${w(26, 44)};margin-left:auto"></span></td>
    <td class="num"><span class="sk sk--text" style="width:${w(40, 56)};margin-left:auto"></span></td>
  </tr>`;
}

const chip = (field, op, value, extra = '') =>
  `<span class="chip" role="group" aria-label="Filter: ${field} ${op} ${value}"><span class="chip__field">${field}</span><span class="chip__op">${op}</span><span class="chip__value">${extra}${value}</span><button class="chip__remove" type="button" aria-label="Remove the filter ${field} ${op} ${value}">${icon('x', { size: 'sm' })}</button></span>`;

function inspectorPanel(loading) {
  const p = inspector('product');
  const s = p.summary;
  const tabs = [
    ['Summary', null], ['Issues', p.issues.total], ['Inlinks', p.inlinks.total], ['Outlinks', p.outlinks.total],
    ['Redirects', null], ['Headers', p.headers.rows.length], ['Structured data', p.structuredData.length], ['Hreflang', null], ['Source', null],
  ];
  const tabStrip = `<div class="tabs tabs--fade" role="tablist" aria-label="About this URL">${tabs.map(([t, n], i) =>
    `<button class="tab" role="tab" type="button" aria-selected="${i === 0}" tabindex="${i === 0 ? 0 : -1}"${i === 0 ? ' aria-controls="insp-summary"' : ''}>${t}${n != null ? `<span class="tab__count">${loading ? '&nbsp;&nbsp;' : num(n)}</span>` : ''}</button>`).join('')}</div>`;

  const head = `
    <div class="inspector__head">
      <div class="inspector__bar">
        <span class="inspector__kind">${loading ? '<span class="sk sk--text" style="width:36%"></span>' : `${icon('file-text', { size: 'sm' })}Page, depth ${s.depth}`}</span>
        <div class="inspector__actions">
          <button class="icon-btn icon-btn--sm" type="button" aria-label="Open in browser" title="Open in browser">${icon('external-link', { size: 'sm' })}</button>
          <button class="icon-btn icon-btn--sm" type="button" aria-label="Copy the URL" title="Copy the URL (Ctrl+Shift+C)">${icon('copy', { size: 'sm' })}</button>
          <button class="icon-btn icon-btn--sm" type="button" aria-label="Dock at the bottom" title="Dock at the bottom">${icon('panel-bottom', { size: 'sm' })}</button>
          <button class="icon-btn icon-btn--sm" type="button" aria-label="Close the inspector" title="Close (Esc)">${icon('x', { size: 'sm' })}</button>
        </div>
      </div>
      <p class="inspector__url">${loading ? '<span class="sk sk--title" style="width:92%"></span><span class="sk sk--title sk--next" style="width:46%"></span>' : wrappingUrl(s.url)}</p>
    </div>`;

  if (loading) {
    const block = (h, rows) => `<div class="inspector__section"><span class="sk sk--text sk--label" style="width:30%"></span>${rows.map(w => `<span class="sk sk--text sk--next" style="width:${w}%"></span>`).join('')}</div>`;
    return `
  <aside class="inspector" aria-label="Inspector" aria-busy="true">
    <span class="inspector__grip" aria-hidden="true"></span>
    ${head}
    ${tabStrip}
    <div class="inspector__body skeleton-appear" id="insp-summary" role="tabpanel">
      <div class="inspector__section">
        <div class="mini-stats">${[0, 1, 2].map(() => `<div class="mini-stat"><span class="sk sk--text" style="width:52%"></span><span class="sk sk--figure sk--next"></span><span class="sk sk--text sk--next" style="width:70%"></span></div>`).join('')}</div>
      </div>
      ${block(0, [86, 64])}
      ${block(0, [94, 72, 100])}
      ${block(0, [96, 88, 40, 100])}
      ${block(0, [80, 60])}
    </div>
  </aside>`;
  }

  const issueRows = p.issues.rows.map(i => `
        <li class="insp-issue">${sevIcon(i.severity)}<span class="insp-issue__title">${esc(i.title)}</span><span class="insp-issue__detail">${esc(i.detail ?? '')}</span></li>`).join('');
  const bySev = { error: 0, warning: 0, notice: 0 };
  p.issues.rows.forEach(i => bySev[i.severity]++);
  const sevSummary = Object.entries(bySev).filter(([, n]) => n).map(([k, n]) => `${n} ${severityPlural[k][n === 1 ? 0 : 1]}`).join(', ');
  const [tv, tu] = bytes(s.sizeBytes, { parts: true });
  const [bv, bu] = bytes(s.bodyBytes, { parts: true });

  return `
  <aside class="inspector" aria-label="Inspector">
    <span class="inspector__grip" aria-hidden="true" title="Drag to resize"></span>
    ${head}
    ${tabStrip}
    <div class="inspector__body" id="insp-summary" role="tabpanel">
      <section class="inspector__section">
        <div class="mini-stats">
          <div class="mini-stat"><div class="mini-stat__label">Status</div><div class="mini-stat__value"><span class="mark s2xx" aria-hidden="true"></span>${s.statusCode}</div><div class="mini-stat__context">OK</div></div>
          <div class="mini-stat"><div class="mini-stat__label">Response</div><div class="mini-stat__value">${num(s.responseMs)}<span class="u">ms</span></div><div class="mini-stat__context">Limit 1 s</div></div>
          <div class="mini-stat"><div class="mini-stat__label">Words</div><div class="mini-stat__value">${num(s.wordCount)}</div><div class="mini-stat__context">Minimum 200</div></div>
        </div>
      </section>

      <section class="inspector__section">
        <div class="verdict">
          <span class="verdict__icon">${icon('circle-check', { size: 'md' })}</span>
          <div>
            <div class="verdict__title">Indexable</div>
            <p class="verdict__why">It answers 200, its canonical points to itself, and no noindex applies. Robots directives: <span class="mono">${esc(s.robotsDirectives.join(', '))}</span>.</p>
          </div>
        </div>
      </section>

      <section class="inspector__section">
        <div class="fact">
          <div class="fact__label"><span>Title</span><span class="num">${s.titleLength} characters</span></div>
          <p class="fact-text">${esc(s.title)}</p>
          <div class="meter" style="--min:30;--max:60;--scale:80;--value:${s.titleLength}" role="img" aria-label="${s.titleLength} characters; 30 to 60 is the range"><span class="meter__range"></span><span class="meter__mark"></span></div>
          <div class="fact-meta"><span class="sev sev--notice">${sevIcon('notice')}${esc(checkById['title-too-long'].title)}</span><span>1 over the 60 limit</span></div>
        </div>
        <div class="fact">
          <div class="fact__label"><span>Meta description</span><span class="num">${s.metaDescriptionLength} characters</span></div>
          <p class="fact-text">${esc(s.metaDescription)}</p>
          <div class="meter" style="--min:70;--max:155;--scale:200;--value:${s.metaDescriptionLength}" role="img" aria-label="${s.metaDescriptionLength} characters; 70 to 155 is the range"><span class="meter__range"></span><span class="meter__mark"></span></div>
          <div class="fact-meta"><span class="sev sev--notice">${sevIcon('notice')}${esc(checkById['description-too-long'].title)}</span><span>3 over the 155 limit</span></div>
        </div>
        <div class="fact">
          <div class="fact__label"><span>H1</span><span>${s.h1Count} on the page</span></div>
          <p class="fact-text">${esc(s.h1)}</p>
        </div>
      </section>

      <section class="inspector__section">
        <dl class="facts">
          <dt>Canonical</dt><dd><span class="badge">Self-referencing</span></dd>
                    <dt>Headings</dt><dd>${s.h1Count} H1, ${s.h2Count} H2</dd>
          <dt>Images</dt><dd>${num(s.imageCount)}, all with alt text</dd>
          <dt>Structured data</dt><dd>${esc(s.structuredDataTypes.join(', '))}</dd>
          <dt>Language</dt><dd class="mono">${esc(s.language)}</dd>
        </dl>
      </section>

      <section class="inspector__section">
        <h3 class="inspector__section-title">Timings and size</h3>
        <dl class="facts">
          <dt>Response</dt><dd class="num">${num(s.responseMs)} ms</dd>
          <dt>First byte</dt><dd class="num">${num(s.ttfbMs)} ms</dd>
          <dt>Transferred</dt><dd class="num">${tv} ${tu} <span class="muted">${bv} ${bu} of HTML</span></dd>
          <dt>Protocol</dt><dd>HTTP/${s.httpVersion === '2.0' ? '2' : s.httpVersion}</dd>
          <dt>Crawled</dt><dd class="num">${dateShort(s.crawledAt)}, ${time(s.crawledAt, { seconds: true })}</dd>
        </dl>
      </section>

      <section class="inspector__section">
        <h3 class="inspector__section-title">${num(p.issues.total)} issues <span class="muted weight-regular">${sevSummary}</span><a class="link" href="#">Issues tab</a></h3>
        <ul class="insp-issues">${issueRows}
        </ul>
      </section>

      <section class="inspector__section">
        <h3 class="inspector__section-title">Links and discovery</h3>
        <dl class="facts">
          <dt>Inlinks</dt><dd><span class="num">${num(s.inlinks)}</span> <span class="muted">from ${num(s.uniqueInlinks)} pages</span></dd>
          <dt>Outlinks</dt><dd><span class="num">${num(s.outlinks)}</span> <span class="muted">hyperlinks to ${num(s.uniqueOutlinks)} URLs</span></dd>
          <dt>Depth</dt><dd>${s.depth} clicks from the start URL</dd>
          <dt>Found on</dt><dd>${urlCell(s.foundOn, 40)}</dd>
        </dl>
      </section>
    </div>
  </aside>`;
}

export function urlsPage({ loading = false } = {}) {
  const g = urlGrid();
  const selectedId = 119;
  const body = loading
    ? Array.from({ length: 22 }, (_, i) => skeletonRow(i)).join('')
    : g.rows.slice(0, 22).map((r, i) => row(r, i, selectedId)).join('');
  const pages = urls.pages;

  const main = `
    <div class="page page--fill page--grid">
      <header class="page-header">
        <div class="page-header__text">
          <h1 class="page-title">URLs</h1>
          <p class="page-subtitle">${num(urls.total)} URLs fetched, ${num(pages)} of them pages. ${num(urls.notFetched)} more were found but not followed.</p>
        </div>
        <div class="page-header__actions">
          <button class="btn btn--secondary" type="button">${icon('bookmark', { size: 'sm' })}Saved views${icon('chevron-down', { size: 'sm' })}</button>
        </div>
      </header>

      <section class="card view-card" aria-label="URL grid">
        <div class="filterbar" role="toolbar" aria-label="Filters">
          <label class="field filterbar__search">${icon('search', { size: 'sm' })}<input class="field__input" type="search" placeholder="Search" aria-label="Search URLs and titles">${kbd('Ctrl', 'F')}</label>
          ${chip('Type', 'is', 'HTML')}
          ${chip('Internal', 'is', 'yes')}
          ${chip('Indexable', 'is', 'yes')}
          <button class="chip chip--add" type="button">${icon('plus', { size: 'sm' })}Filter</button>
          <button class="btn btn--ghost btn--sm filterbar__clear" type="button">Clear</button>
        </div>
        <div class="viewbar">
          <span class="result-count" role="status" aria-live="polite">
            ${loading ? '<span class="sk sk--figure sk--count"></span>' : `<span class="result-count__n">${num(g.count)}</span>`}
            <span class="result-count__of">of ${num(g.total)} URLs</span>
          </span>
          <button class="btn btn--ghost btn--sm" type="button" title="Sort (shift-click a column header to add a second sort)">${icon('sort', { size: 'sm' })}Issues, most first${icon('chevron-down', { size: 'sm' })}</button>
          <button class="btn btn--ghost btn--sm" type="button">${icon('columns', { size: 'sm' })}Overview columns${icon('chevron-down', { size: 'sm' })}</button>
          <span class="viewbar__sep" aria-hidden="true"></span>
          <div class="segmented segmented--icons" role="group" aria-label="Row density">
            <button class="segmented__item" type="button" data-density-choice="comfortable" aria-pressed="true" title="Comfortable rows">${icon('rows-comfortable', { size: 'sm' })}<span class="sr-only">Comfortable</span></button>
            <button class="segmented__item" type="button" data-density-choice="compact" aria-pressed="false" title="Compact rows">${icon('rows-compact', { size: 'sm' })}<span class="sr-only">Compact</span></button>
          </div>
          <button class="btn btn--secondary btn--sm btn--collapse" type="button" title="Export this view (Ctrl+E)">${icon('download', { size: 'sm' })}<span class="btn__label">Export</span></button>
        </div>
        <div class="grid-frame">
        <div class="grid-scroll">
          <table class="grid" role="grid" aria-label="URLs" aria-rowcount="${g.count + 1}" aria-colcount="${COLUMNS.length}" aria-multiselectable="true"${loading ? ' aria-busy="true"' : ''}>
            <colgroup>${COLUMNS.map(c => `<col style="width:${c.width}px">`).join('')}</colgroup>
            <thead>${headerRow()}</thead>
            <tbody${loading ? ' class="skeleton-appear"' : ''}>${body}</tbody>
          </table>
        </div>
        <span class="scroll-shadow-right" aria-hidden="true"></span>
        </div>
        <footer class="grid-foot">
          ${loading
            ? '<span class="loading-note">Loading the first rows</span>'
            : `<span class="grid-foot__hint">${kbd('↑', '↓')} Move</span><span class="grid-foot__hint">${kbd('Enter')} Inspect</span><span class="grid-foot__hint">${kbd('Space')} Select</span><span class="grid-foot__hint">${kbd('Ctrl', 'C')} Copy for Excel</span>`}
          <span class="grid-foot__end">${loading ? '' : `Row 12 of ${num(g.count)} selected`}</span>
        </footer>
      </section>
    </div>`;

  return page({
    title: loading ? 'URLs (loading), gardenfurnituresets.co.uk' : 'URLs, gardenfurnituresets.co.uk',
    file: loading ? 'urls-loading.html' : 'urls.html',
    section: 'urls', crawlState: 'done',
    crumbs: [
      { label: 'gardenfurnituresets.co.uk', favicon: 'gardenfurnituresets.co.uk', strong: true, href: 'live.html' },
      { label: dateTime(gardenCrawl.startedAt), href: 'live.html' },
      { label: 'URLs' },
    ],
    main, inspector: inspectorPanel(loading),
  });
}
