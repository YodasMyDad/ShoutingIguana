// Builds the owner's gallery of the Simple-mode mockups from the design agent's shots.
// Usage: node build.cjs <shotsDir> <outDir>
// Writes <outDir>/simple-mode-mockups.html; the shots stay where they are (<outDir>/shots/).
// Prints the list of supporting files (relative to <outDir>) as JSON on the last line.
const fs = require('node:fs');
const path = require('node:path');

const [shotsDir, outDir] = process.argv.slice(2);
if (!shotsDir || !outDir) throw new Error('usage: node build.cjs <shotsDir> <outDir>');

const FACTS = {
  taken: '11 Nov 2026',
  shell: 'Design mockups: static pages, not the built app',
  page: '1440 × 900, and the shell at 1280 × 720, 900 px wide and 200% zoom',
  data: 'The garden furniture crawl of 31 Oct. Sites named .example are invented',
  branch: 'docs/v2-ui-simple.md on ui/step-3',
};

// One entry per screen, in the order someone meets them. `shots` name files without "-dark.png" / "-light.png";
// a name ending in ".png" is a single shot that exists in one theme only.
const SECTIONS = [
  {
    id: 'starting',
    title: 'Starting a check',
    blurb: 'Where the app opens on a first start: one field, in plain words. Options asks four questions and no more.',
    shots: [['01-home-first'], ['03-home'], ['05-new-simple']],
  },
  {
    id: 'checking',
    title: 'While it checks',
    blurb: 'What a non-expert needs while the crawl runs: it is working, how far it has got, and that the window can be closed.',
    shots: [['07-checking']],
  },
  {
    id: 'summary',
    title: 'The Summary',
    blurb: 'What a finished check opens on: the score, one sentence, five things to fix first, and the report as the main button.',
    shots: [['09-summary'], ['11-score'], ['13-summary-perfect'], ['15-summary-stopped']],
  },
  {
    id: 'fix',
    title: 'What to fix',
    blurb: 'Every problem in plain words, then one problem with what is wrong, how to fix it, who usually does, and the pages.',
    shots: [['17-fix'], ['19-problem'], ['21-problem-template']],
  },
  {
    id: 'report',
    title: 'The report',
    blurb: 'One document for the whole check, made by the engine. It prints, saves as a PDF, and has a spreadsheet beside it.',
    shots: [
      ['23-report-screen'],
      ['25-report-print-light.png', null, 'As printed'],
      ['26-report-print-2-light.png', null, 'As printed'],
      ['27-report-print-3-light.png', null, 'As printed'],
    ],
  },
  {
    id: 'settings',
    title: 'Settings and New crawl',
    blurb: 'The same screens in each mode: Simple shows what a non-expert can answer, Advanced shows everything.',
    shots: [['28-settings-simple'], ['30-settings-advanced'], ['32-new-advanced']],
  },
  {
    id: 'advanced',
    title: 'Advanced, with the switch',
    blurb: 'Everything already built stays as it is. It gains the switch, the score beside the answer, and Report.',
    shots: [['34-advanced-overview'], ['36-advanced-live']],
  },
  {
    id: 'sizes',
    title: 'The switch at other sizes',
    blurb: 'The switch never moves or disappears. The rest of the top bar gives way around it, in a fixed order.',
    shots: [
      ['38-shell-summary-1280x720-light.png', null, 'Light only'],
      ['39-shell-summary-narrow-900-light.png', null, 'Light only'],
      ['40-shell-summary-zoom-200'],
      ['42-shell-checking-1280x720-light.png', null, 'Light only'],
      ['43-shell-checking-narrow-900-light.png', null, 'Light only'],
      ['44-shell-checking-zoom-200-light.png', null, 'Light only'],
      ['45-shell-advanced-overview-1280x720-light.png', null, 'Light only'],
      ['46-shell-advanced-overview-narrow-900-light.png', null, 'Light only'],
      ['47-shell-advanced-overview-zoom-200-light.png', null, 'Light only'],
      ['48-shell-advanced-live-1280x720-light.png', null, 'Light only'],
      ['49-shell-advanced-live-narrow-900'],
      ['51-shell-advanced-live-zoom-200-light.png', null, 'Light only'],
    ],
  },
];

const SEEN = [
  'Things worth a look take no points off the score, so a site with nothing wrong can reach 100. The other way is a small cost, capped at 10 points between them.',
  'A fault of the whole site, such as no HTTPS or a robots.txt that can’t be read, takes 60 points, so that site scores 40 at best. The other way treats it as any serious problem, which leaves it near 90.',
  'The switch sits at the right of the top bar, because the sidebar becomes icons in a narrow window. The other place is the foot of the sidebar, beside Settings.',
  'Simple says Serious, Worth fixing and Worth a look where Advanced says Error, Warning and Notice. They map one to one, with the same marks.',
  'The report is one document the engine makes. Print saves it as a PDF, and the full lists of pages go in an Excel workbook beside it.',
];

const NOT_YET =
  'None of this is built yet. The next round builds Settings with the switch, New crawl in both modes, export and the ' +
  'report file. The round after builds the Simple screens, the plain words for every check and the Report screen. ' +
  'Anything you want changed goes into those rounds.';

const index = JSON.parse(fs.readFileSync(path.join(shotsDir, 'index.json'), 'utf8'));
// The design agent's index gives file, section, title and note. The gallery also needs each shot's size.
for (const entry of index) {
  const header = fs.readFileSync(path.join(shotsDir, entry.file)).subarray(16, 24);
  entry.width = header.readUInt32BE(0);
  entry.height = header.readUInt32BE(4);
  entry.shows = entry.note;
}
const byFile = new Map(index.map((entry) => [entry.file, entry]));
const used = [];

const escapeHtml = (text) =>
  String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

function shotFor(name) {
  const entry = byFile.get(name);
  if (!entry) throw new Error(`no such shot in index.json: ${name}`);
  used.push(name);
  return entry;
}

// A screen's dark shot carries the next number: "01-home-first-light.png" pairs with "02-home-first-dark.png".
const withoutNumber = (file) => file.replace(/^\d+-/, '');
function darkOf(name) {
  const wanted = `${withoutNumber(name)}-dark.png`;
  const entry = index.find((candidate) => withoutNumber(candidate.file) === wanted);
  if (!entry) throw new Error(`no dark shot for: ${name}`);
  return entry.file;
}

// A shot's title is the index's, without the theme it names.
const titleOf = (entry) => entry.title.replace(/ \((light|dark)\)$/, '').replace(/, dark$/, '');

function figure([name, givenTitle, tag], sectionTitle) {
  const single = name.endsWith('.png');
  const light = single ? null : shotFor(`${name}-light.png`);
  const dark = single ? null : shotFor(darkOf(name));
  const only = single ? shotFor(name) : null;
  const title = givenTitle ?? titleOf(only ?? light);
  const caption = (only ?? light).shows;
  const size = only ?? light;
  const label = `${sectionTitle}: ${title}`;
  const image = (entry, cls, alt) =>
    `<img class="${cls}" src="shots/${entry.file}" width="${entry.width}" height="${entry.height}" loading="lazy" alt="${escapeHtml(alt)}">`;
  const images = single
    ? image(only, 't-any', caption)
    : image(light, 't-light', caption) + image(dark, 't-dark', caption);
  const data = single
    ? `data-any="shots/${only.file}"`
    : `data-light="shots/${light.file}" data-dark="shots/${dark.file}"`;
  const ratio = `${size.width} / ${size.height}`;
  return `
      <figure class="shot" ${data} data-label="${escapeHtml(label)}">
        <button class="frame" type="button" style="aspect-ratio: ${ratio}" aria-label="Enlarge ${escapeHtml(label)}">${images}</button>
        <figcaption>
          <span class="shot-title">${escapeHtml(title)}${tag ? ` <span class="tag${tag === 'A defect' ? ' tag-warn' : ''}">${escapeHtml(tag)}</span>` : ''}</span>
          <span class="shot-caption">${escapeHtml(caption)}</span>
        </figcaption>
      </figure>`;
}

const sections = SECTIONS.map(
  (section) => `
  <section id="${section.id}" class="screen">
    <header class="screen-head">
      <h2>${escapeHtml(section.title)}</h2>
      <p>${escapeHtml(section.blurb)}</p>
    </header>
    <div class="shots">${section.shots.map((shot) => figure(shot, section.title)).join('')}
    </div>
  </section>`,
).join('\n');

const shotCount = SECTIONS.reduce((total, section) => total + section.shots.length, 0);
const nav = SECTIONS.map((section) => `<a href="#${section.id}">${escapeHtml(section.title)}</a>`).join('');

const html = `<title>Simple Mode Mockups</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&amp;family=JetBrains+Mono:wght@400;500&amp;display=swap">
<style>
  /* Layout: a quiet contact sheet. A sticky bar of screens and the light/dark switch; under it each screen is a
     heading and a two-up grid of framed window shots. The app's own tokens (app/src/styles/tokens.css) dress it. */
  :root {
    --bg: #f4f4f6;
    --surface: #ffffff;
    --text: #18181b;
    --text-2: #46464d;
    --muted: #68686f;
    --border: #e3e3e7;
    --border-strong: #cfcfd5;
    --accent-text: #0b6bd3;
    --accent-fill: #0b6bd3;
    --accent-subtle: #ebf4ff;
    --accent-border: #a8cfff;
    --on-accent: #ffffff;
    --warn-text: #7a4d00;
    --warn-bg: #fff1cc;
    --scrim: rgba(12, 12, 16, 0.86);
    --viewer-text: #ededef;
    --viewer-muted: #b4b4bb;
    --shadow: 0 1px 2px rgba(24, 24, 27, 0.06), 0 8px 24px rgba(24, 24, 27, 0.08);
    --font-sans: 'Inter', ui-sans-serif, system-ui, 'Segoe UI', Roboto, sans-serif;
    --font-mono: 'JetBrains Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme='light']) {
      --bg: #0b0b0d;
      --surface: #121214;
      --text: #ededef;
      --text-2: #b4b4bb;
      --muted: #8e8e96;
      --border: #28282d;
      --border-strong: #3a3a40;
      --accent-text: #4fa6ff;
      --accent-fill: #1a6ed3;
      --accent-subtle: #132538;
      --accent-border: #1f4c7c;
      --on-accent: #ffffff;
      --warn-text: #f5c25b;
      --warn-bg: #33270a;
      --shadow: 0 1px 2px rgba(0, 0, 0, 0.5), 0 8px 24px rgba(0, 0, 0, 0.45);
      color-scheme: dark;
    }
  }
  :root[data-theme='dark'] {
    --bg: #0b0b0d;
    --surface: #121214;
    --text: #ededef;
    --text-2: #b4b4bb;
    --muted: #8e8e96;
    --border: #28282d;
    --border-strong: #3a3a40;
    --accent-text: #4fa6ff;
    --accent-fill: #1a6ed3;
    --accent-subtle: #132538;
    --accent-border: #1f4c7c;
    --on-accent: #ffffff;
    --warn-text: #f5c25b;
    --warn-bg: #33270a;
    --shadow: 0 1px 2px rgba(0, 0, 0, 0.5), 0 8px 24px rgba(0, 0, 0, 0.45);
    color-scheme: dark;
  }

  body {
    background: var(--bg);
    color: var(--text);
    font-family: var(--font-sans);
    font-size: 15px;
    line-height: 1.55;
    -webkit-font-smoothing: antialiased;
  }
  body::before {
    content: '';
    position: fixed;
    inset: 0 0 auto;
    height: env(safe-area-inset-top, 0px);
    z-index: 10;
    background: var(--bg);
  }
  .wrap {
    max-width: 1320px;
    margin-inline: auto;
    padding-left: max(16px, env(safe-area-inset-left, 0px));
    padding-right: max(16px, env(safe-area-inset-right, 0px));
  }
  a { color: var(--accent-text); }
  :focus-visible { outline: 2px solid var(--accent-text); outline-offset: 2px; }

  /* Masthead */
  .masthead { padding-block: 40px 28px; display: grid; gap: 14px; }
  .eyebrow {
    margin: 0;
    font-family: var(--font-mono);
    font-size: 12px;
    letter-spacing: 0.04em;
    color: var(--muted);
  }
  h1 {
    margin: 0;
    font-size: clamp(28px, 4vw, 40px);
    line-height: 1.1;
    font-weight: 600;
    letter-spacing: -0.02em;
    text-wrap: balance;
  }
  .lede { margin: 0; max-width: 64ch; color: var(--text-2); font-size: 16px; }
  .facts {
    margin: 10px 0 0;
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 12px 28px;
    padding-block: 16px;
    border-block: 1px solid var(--border);
  }
  .facts div { display: grid; gap: 2px; min-width: 0; align-content: start; }
  .facts dt { font-size: 12px; color: var(--muted); }
  .facts dd { margin: 0; font-size: 13px; color: var(--text-2); overflow-wrap: anywhere; }
  .facts dd.mono { font-family: var(--font-mono); font-size: 12.5px; }

  /* The sticky bar */
  .bar {
    position: sticky;
    top: env(safe-area-inset-top, 0px);
    z-index: 5;
    background: var(--bg);
    border-bottom: 1px solid var(--border);
  }
  .bar-inner { display: flex; align-items: center; gap: 16px; padding-block: 10px; }
  .bar nav { display: flex; gap: 4px; overflow-x: auto; min-width: 0; flex: 1; scrollbar-width: thin; }
  .bar nav a {
    flex: none;
    padding: 5px 10px;
    border-radius: 6px;
    font-size: 13px;
    font-weight: 500;
    color: var(--text-2);
    text-decoration: none;
    white-space: nowrap;
  }
  .bar nav a:hover { background: var(--surface); color: var(--text); }
  .switch { flex: none; display: flex; align-items: center; gap: 8px; }
  .switch-label { font-size: 12px; color: var(--muted); }
  .segmented {
    display: inline-flex;
    padding: 2px;
    border: 1px solid var(--border-strong);
    border-radius: 7px;
    background: var(--surface);
  }
  .segmented button {
    font: inherit;
    font-size: 13px;
    font-weight: 500;
    padding: 3px 12px;
    border: 0;
    border-radius: 5px;
    background: transparent;
    color: var(--text-2);
    cursor: pointer;
  }
  .segmented button[aria-pressed='true'] { background: var(--accent-fill); color: var(--on-accent); }

  /* Screens */
  .screen { padding-block: 36px 8px; scroll-margin-top: 56px; }
  .screen-head { display: grid; gap: 4px; margin-bottom: 18px; }
  h2 { margin: 0; font-size: 20px; line-height: 1.25; font-weight: 600; letter-spacing: -0.01em; }
  .screen-head p { margin: 0; max-width: 70ch; color: var(--text-2); }
  .shots { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 28px 24px; }
  .shot { margin: 0; display: grid; gap: 10px; align-content: start; min-width: 0; }
  .frame {
    display: block;
    width: 100%;
    max-width: 100%;
    padding: 0;
    border: 1px solid var(--border-strong);
    border-radius: 8px;
    overflow: hidden;
    background: var(--surface);
    box-shadow: var(--shadow);
    cursor: zoom-in;
  }
  .frame img { display: block; width: 100%; height: 100%; object-fit: contain; }
  .shots-light .t-dark, .shots-dark .t-light { display: none; }
  figcaption { display: grid; gap: 2px; }
  .shot-title { font-weight: 600; font-size: 14px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .shot-caption { color: var(--text-2); font-size: 13.5px; max-width: 66ch; }
  .tag {
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    padding: 1px 6px;
    border-radius: 4px;
    border: 1px solid var(--border-strong);
    color: var(--muted);
  }
  .tag-warn { border-color: transparent; background: var(--warn-bg); color: var(--warn-text); }

  /* Notes */
  .notes { padding-block: 40px 56px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 28px 40px; }
  .notes > div { min-width: 0; }
  .notes h2 { margin-bottom: 10px; }
  .notes ul { margin: 0; padding-left: 18px; display: grid; gap: 8px; color: var(--text-2); }
  .notes p { margin: 0; color: var(--text-2); max-width: 62ch; }

  /* The viewer */
  dialog.viewer {
    width: 100%;
    height: 100%;
    max-width: none;
    max-height: none;
    margin: 0;
    padding: 0;
    border: 0;
    background: var(--scrim);
    color: var(--viewer-text);
  }
  dialog.viewer::backdrop { background: var(--scrim); }
  .viewer-inner {
    box-sizing: border-box;
    height: 100%;
    display: grid;
    grid-template-rows: auto minmax(0, 1fr) auto;
    gap: 12px;
    padding: max(12px, env(safe-area-inset-top, 0px)) max(16px, env(safe-area-inset-right, 0px))
      max(14px, env(safe-area-inset-bottom, 0px)) max(16px, env(safe-area-inset-left, 0px));
  }
  .viewer-top { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  .viewer-count { font-family: var(--font-mono); font-size: 12px; color: var(--viewer-muted); margin-right: auto; }
  .viewer button {
    font: inherit;
    font-size: 13px;
    font-weight: 500;
    padding: 5px 12px;
    border-radius: 6px;
    border: 1px solid rgba(255, 255, 255, 0.28);
    background: rgba(255, 255, 255, 0.08);
    color: var(--viewer-text);
    cursor: pointer;
  }
  .viewer button:hover { background: rgba(255, 255, 255, 0.16); }
  .viewer button:focus-visible { outline: 2px solid #4fa6ff; outline-offset: 2px; }
  .viewer button[disabled] { opacity: 0.4; cursor: default; }
  .viewer-stage { min-height: 0; display: grid; place-items: center; }
  .viewer-stage img { max-width: 100%; max-height: 100%; width: auto; height: auto; object-fit: contain; border-radius: 6px; }
  .viewer-text { display: grid; gap: 2px; justify-items: center; text-align: center; }
  .viewer-title { margin: 0; font-weight: 600; font-size: 14px; }
  .viewer-caption { margin: 0; font-size: 13.5px; color: var(--viewer-muted); max-width: 90ch; }
  kbd {
    font-family: var(--font-mono);
    font-size: 11px;
    padding: 0 4px;
    border: 1px solid rgba(255, 255, 255, 0.28);
    border-radius: 4px;
    margin-left: 6px;
  }

  @media (max-width: 860px) {
    .shots, .notes { grid-template-columns: minmax(0, 1fr); }
    .masthead { padding-block: 28px 20px; }
    .switch-label { display: none; }
    kbd { display: none; }
  }
</style>

<div id="gallery" class="shots-light">
  <div class="wrap">
    <header class="masthead">
      <p class="eyebrow">Shouting Iguana v2 · step 3, Simple mode</p>
      <h1>Simple mode, as designed</h1>
      <p class="lede">
        How the app will look when it opens in Simple, drawn on a real crawl before any of it is built, so it can be
        changed cheaply. ${shotCount} views, most in both themes. Select any shot to see it full size.
      </p>
      <dl class="facts">
        <div><dt>Drawn</dt><dd>${escapeHtml(FACTS.taken)}</dd></div>
        <div><dt>What these are</dt><dd>${escapeHtml(FACTS.shell)}</dd></div>
        <div><dt>Sizes</dt><dd>${escapeHtml(FACTS.page)}</dd></div>
        <div><dt>Data</dt><dd>${escapeHtml(FACTS.data)}</dd></div>
        <div><dt>The design in full</dt><dd class="mono">${escapeHtml(FACTS.branch)}</dd></div>
      </dl>
    </header>
  </div>

  <div class="bar">
    <div class="wrap bar-inner">
      <nav aria-label="Screens">${nav}<a href="#notes">Notes</a></nav>
      <div class="switch">
        <span class="switch-label" id="shots-label">Shots</span>
        <div class="segmented" role="group" aria-labelledby="shots-label">
          <button type="button" id="shots-light" data-shots="light" aria-pressed="true">Light</button>
          <button type="button" id="shots-dark" data-shots="dark" aria-pressed="false">Dark</button>
        </div>
      </div>
    </div>
  </div>

  <main class="wrap">
${sections}

  <section id="notes" class="notes">
    <div>
      <h2>Decided, and yours to overturn</h2>
      <ul>
        ${SEEN.map((line) => `<li>${escapeHtml(line)}</li>`).join('\n        ')}
      </ul>
    </div>
    <div>
      <h2>What happens next</h2>
      <p>${escapeHtml(NOT_YET)}</p>
    </div>
  </section>
  </main>
</div>

<dialog class="viewer" id="viewer" aria-label="Screenshot">
  <div class="viewer-inner">
    <div class="viewer-top">
      <span class="viewer-count" id="viewer-count"></span>
      <button type="button" id="viewer-prev">Previous<kbd>←</kbd></button>
      <button type="button" id="viewer-next">Next<kbd>→</kbd></button>
      <button type="button" id="viewer-theme">Dark<kbd>T</kbd></button>
      <button type="button" id="viewer-close">Close<kbd>Esc</kbd></button>
    </div>
    <div class="viewer-stage"><img id="viewer-image" alt=""></div>
    <div class="viewer-text">
      <p class="viewer-title" id="viewer-title"></p>
      <p class="viewer-caption" id="viewer-caption"></p>
    </div>
  </div>
</dialog>

<script>
  (function () {
    var gallery = document.getElementById('gallery');
    var root = document.documentElement;
    var buttons = { light: document.getElementById('shots-light'), dark: document.getElementById('shots-dark') };
    var media = window.matchMedia('(prefers-color-scheme: dark)');
    var chosen = null;
    try { chosen = window.localStorage.getItem('shots'); } catch (error) { chosen = null; }
    if (chosen !== 'light' && chosen !== 'dark') chosen = null;

    function pageTheme() {
      var explicit = root.getAttribute('data-theme');
      if (explicit === 'light' || explicit === 'dark') return explicit;
      return media.matches ? 'dark' : 'light';
    }
    function shots() { return chosen || pageTheme(); }
    function apply() {
      var theme = shots();
      gallery.className = 'shots-' + theme;
      buttons.light.setAttribute('aria-pressed', String(theme === 'light'));
      buttons.dark.setAttribute('aria-pressed', String(theme === 'dark'));
      if (viewer.open) show(current);
    }
    function choose(theme) {
      chosen = theme;
      try { window.localStorage.setItem('shots', theme); } catch (error) { /* stays for this visit only */ }
      apply();
    }

    var figures = Array.prototype.slice.call(document.querySelectorAll('.shot'));
    var viewer = document.getElementById('viewer');
    var image = document.getElementById('viewer-image');
    var count = document.getElementById('viewer-count');
    var title = document.getElementById('viewer-title');
    var caption = document.getElementById('viewer-caption');
    var prev = document.getElementById('viewer-prev');
    var next = document.getElementById('viewer-next');
    var themeButton = document.getElementById('viewer-theme');
    var current = 0;

    function show(index) {
      current = Math.max(0, Math.min(figures.length - 1, index));
      var figure = figures[current];
      var single = figure.getAttribute('data-any');
      var theme = shots();
      image.src = single || figure.getAttribute('data-' + theme);
      image.alt = figure.querySelector('.shot-caption').textContent;
      count.textContent = current + 1 + ' of ' + figures.length;
      title.textContent = figure.getAttribute('data-label');
      caption.textContent = figure.querySelector('.shot-caption').textContent;
      prev.disabled = current === 0;
      next.disabled = current === figures.length - 1;
      themeButton.disabled = Boolean(single);
      themeButton.firstChild.textContent = theme === 'dark' ? 'Light' : 'Dark';
    }
    function open(index) {
      show(index);
      if (!viewer.open) viewer.showModal();
    }

    figures.forEach(function (figure, index) {
      figure.querySelector('.frame').addEventListener('click', function () { open(index); });
    });
    buttons.light.addEventListener('click', function () { choose('light'); });
    buttons.dark.addEventListener('click', function () { choose('dark'); });
    prev.addEventListener('click', function () { show(current - 1); });
    next.addEventListener('click', function () { show(current + 1); });
    themeButton.addEventListener('click', function () { choose(shots() === 'dark' ? 'light' : 'dark'); });
    document.getElementById('viewer-close').addEventListener('click', function () { viewer.close(); });
    viewer.addEventListener('click', function (event) {
      if (event.target === viewer || event.target.classList.contains('viewer-stage')) viewer.close();
    });
    viewer.addEventListener('keydown', function (event) {
      if (event.key === 'ArrowLeft') { show(current - 1); event.preventDefault(); }
      else if (event.key === 'ArrowRight') { show(current + 1); event.preventDefault(); }
      else if ((event.key === 't' || event.key === 'T') && !themeButton.disabled) { themeButton.click(); }
    });
    viewer.addEventListener('close', function () {
      var frame = figures[current].querySelector('.frame');
      frame.focus();
      frame.scrollIntoView({ block: 'nearest' });
    });

    Array.prototype.forEach.call(document.querySelectorAll('.bar nav a'), function (link) {
      link.addEventListener('click', function (event) {
        var target = document.getElementById(link.getAttribute('href').slice(1));
        if (!target) return;
        event.preventDefault();
        target.scrollIntoView();
      });
    });

    if (media.addEventListener) media.addEventListener('change', apply);
    new MutationObserver(apply).observe(root, { attributes: true, attributeFilter: ['data-theme'] });
    apply();
  })();
</script>
`;

fs.mkdirSync(path.join(outDir, 'shots'), { recursive: true });
if (path.resolve(shotsDir) !== path.resolve(outDir, 'shots')) {
  for (const file of used) fs.copyFileSync(path.join(shotsDir, file), path.join(outDir, 'shots', file));
}
fs.writeFileSync(path.join(outDir, 'simple-mode-mockups.html'), html);

const unused = index.map((entry) => entry.file).filter((file) => !used.includes(file));
console.log(`views: ${shotCount}, files: ${used.length}, left out: ${unused.join(', ') || 'none'}`);
console.log(JSON.stringify(used.map((file) => `shots/${file}`)));
