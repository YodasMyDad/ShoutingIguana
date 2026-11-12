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

// A shot's title is the index's, without the theme it names.
const titleOf = (entry) => entry.title.replace(/ \((light|dark)\)$/, '').replace(/, dark$/, '');

function figure([name, givenTitle, tag], sectionTitle) {
  const single = name.endsWith('.png');
  const light = single ? null : shotFor(`${name}-light.png`);
  const dark = single ? null : shotFor(`${name}-dark.png`);
  const only = single ? shotFor(name) : null;
  const title = givenTitle ?? titleOf(only ?? light);
