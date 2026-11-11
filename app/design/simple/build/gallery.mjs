// Copies the Simple mockups' screenshots into the gallery folder, named in reading order, and writes index.json:
// an ordered list of { file, section, title, note }.
//   node gallery.mjs <app/design/simple/screens> <_gallery/simple/shots>
import { copyFileSync, mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const [,, from, to] = process.argv;
mkdirSync(to, { recursive: true });

// [source base name, section, title, note]. Views with both themes get a light and a dark entry.
const views = [
  ['home-first', 'Simple: starting', 'Home on a first start', 'One field and three steps; the line under them says where Advanced is, and the switch is top right.'],
  ['home', 'Simple: starting', 'Home, with your sites', 'Each site shows its score, what needs fixing and when it was checked; one is being checked now and one was stopped early.'],
  ['new-simple', 'Simple: starting', 'Options before a check', 'The only four things Simple asks, each answerable by a non-expert; everything else is one link away in Advanced.'],
  ['checking', 'Simple: while it checks', 'Checking a site', 'Three steps, three figures and an honest floor on the time left, with Pause and Stop in the top bar.'],
  ['summary', 'Simple: the Summary', 'The Summary of the garden crawl', 'The score with its word and one sentence, five fixes in order, and the report as the main button.'],
  ['score', 'Simple: the Summary', 'How the score is worked out', 'Each line that cost points names its problem and its share of the site, then the sum and what the score covers.'],
  ['summary-perfect', 'Simple: the Summary', 'A site with nothing to fix', 'A score of 100, "Nothing to fix", and the three things worth a look kept apart from to-dos.'],
  ['summary-stopped', 'Simple: the Summary', 'A check that was stopped early', 'The banner says how far it got, the ring is dashed and says "so far", and Continue is offered twice.'],
  ['fix', 'Simple: what to fix', 'What to fix', 'Problems in plain words under Serious, Worth fixing and Worth a look, each with its count and share of the site.'],
  ['problem', 'Simple: what to fix', 'One problem, with its pages', 'What is wrong, how to fix it and who usually does, then each broken address with the page that links to it.'],
  ['problem-template', 'Simple: what to fix', 'A fault in the template, as one fix', 'The callout says one fix clears 264 of the 265 pages, and the list shows the one link behind them.'],
  ['report-screen', 'The report', 'The Report screen', 'The toolbar offers the full report or the summary page, the spreadsheet, Save, and Print or save as PDF.'],
  ['settings-simple', 'Settings and New crawl in each mode', 'Settings in Simple', 'Mode is the first setting, there are four sections, and a note says where the other four are.'],
  ['settings-advanced', 'Settings and New crawl in each mode', 'Settings in Advanced', 'The same screen with all eight sections, and Density beside Mode and Theme.'],
  ['new-advanced', 'Settings and New crawl in each mode', 'New crawl in Advanced', 'The same form with every setting, in folded sections that show their current values.'],
  ['advanced-overview', 'Advanced, with the switch', 'Overview in Advanced', 'The built Overview with the switch at the end of the top bar, the score beside the answer, and Report in the sidebar.'],
  ['advanced-live', 'Advanced, with the switch', 'The top bar at its fullest', 'A running crawl in Advanced: the status, Pause, Stop, search and the switch all fit, and the date has given way.'],
];
const singles = [
  ['report-print-light', 'The report', 'The report as it prints, pages 1 and 2', 'Page 1 is the whole summary: the score, five fixes and how the score is worked out; the detail starts on page 2.'],
  ['report-print-2-light', 'The report', 'The report as it prints, pages 3 and 4', 'Each problem has its number, plain and technical names, what is wrong, how to fix it and its first five pages.'],
  ['report-print-3-light', 'The report', 'The report as it prints, the last two pages', 'What was checked and found nothing wrong, as good news in plain words, then the facts of the check.'],
  ['shell-summary-1280x720-light', 'The shell at other sizes', 'Simple at 1280 by 720', 'The smallest window the app supports: the switch is where it is at every size.'],
  ['shell-summary-narrow-900-light', 'The shell at other sizes', 'Simple in a narrow window', 'At 900 px the sidebar is icons and the Home crumb has gone; the switch has not moved.'],
  ['shell-summary-zoom-200-light', 'The shell at other sizes', 'Simple at 200% zoom', 'A 1280 px window at 200% zoom: Back, where you are, the status, search and the switch remain.'],
  ['shell-summary-zoom-200-dark', 'The shell at other sizes', 'Simple at 200% zoom, dark', 'The same in the dark theme.'],
  ['shell-checking-1280x720-light', 'The shell at other sizes', 'Checking a site at 1280 by 720', 'With a check running the top bar also holds the status, Pause and Stop, in words.'],
  ['shell-checking-narrow-900-light', 'The shell at other sizes', 'Checking a site in a narrow window', 'Pause and Stop have become icons before anything was allowed to touch the switch.'],
  ['shell-checking-zoom-200-light', 'The shell at other sizes', 'Checking a site at 200% zoom', 'The tightest case in Simple: status, Pause, Stop, search and the switch in 584 px.'],
  ['shell-advanced-overview-1280x720-light', 'The shell at other sizes', 'Advanced at 1280 by 720', 'Advanced keeps its search field here, and the switch shows Advanced.'],
  ['shell-advanced-overview-narrow-900-light', 'The shell at other sizes', 'Advanced in a narrow window', 'Search has become its icon; the switch is unchanged.'],
  ['shell-advanced-overview-zoom-200-light', 'The shell at other sizes', 'Advanced at 200% zoom', 'The same order of giving way as Simple.'],
  ['shell-advanced-live-1280x720-light', 'The shell at other sizes', 'Advanced with a crawl running, at 1280 by 720', 'The fullest top bar the app has, at its smallest supported window.'],
  ['shell-advanced-live-narrow-900-light', 'The shell at other sizes', 'Advanced with a crawl running, narrow', 'Pause, Stop and search are icons; the switch is whole.'],
  ['shell-advanced-live-narrow-900-dark', 'The shell at other sizes', 'Advanced with a crawl running, narrow, dark', 'The same in the dark theme.'],
  ['shell-advanced-live-zoom-200-light', 'The shell at other sizes', 'Advanced with a crawl running, at 200% zoom', 'The worst case of all: everything that must stay still fits.'],
];

const index = [];
let n = 0;
const add = (source, section, title, note) => {
  const src = join(from, `${source}.png`);
  if (!existsSync(src)) throw new Error(`Missing screenshot: ${src}`);
  const file = `${String(++n).padStart(2, '0')}-${source}.png`;
  copyFileSync(src, join(to, file));
  index.push({ file, section, title, note });
};
const reportAfter = 'report-screen';
for (const [base, section, title, note] of views) {
  add(`${base}-light`, section, `${title} (light)`, note);
  add(`${base}-dark`, section, `${title} (dark)`, 'The same screen in the dark theme.');
  if (base === reportAfter) for (const s of singles.filter(x => x[1] === 'The report')) add(...s);
}
for (const s of singles.filter(x => x[1] !== 'The report')) add(...s);
writeFileSync(join(to, 'index.json'), JSON.stringify(index, null, 2) + '\n');
console.log(`${index.length} screenshots copied to ${to}`);
