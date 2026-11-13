// What the Report screen says, in each mode's words (docs/v2-ui-simple.md, "Simple's words"): Simple checks a site and
// works out its results where Advanced crawls and analyses it. The report's own words are the engine's, in the frame.
import type { OutOfDateReason } from '../../api/types';
import type { Mode } from '../../mode/mode';

export interface ReportWords {
  /** "checked 31 October 2026": the crawl's date, after the site. */
  checked: string;
  /** Never analysed: the state's title, its sentence and its action. */
  notAnalysedTitle: string;
  notAnalysedText: string;
  analyse: string;
  /** Never analysed while it runs: the report comes when it ends. */
  runningTitle: string;
  runningText: string;
  /** Being analysed: the banner over the report, or the state of a crawl with none yet. */
  analysing: string;
  /** The last analysis no longer stands: the banner, and its action. */
  outOfDate: (when: string) => string;
  analyseAgain: string;
  outOfDateReason: Record<OutOfDateReason, string>;
  outOfDateFallback: string;
}

const SIMPLE: ReportWords = {
  checked: 'checked',
  notAnalysedTitle: 'The results haven’t been worked out for this check',
  notAnalysedText:
    'The report is made from the results: what was found on each page, and the score. Working them out takes a few seconds for most sites.',
  analyse: 'Work out the results',
  runningTitle: 'The report comes when the check ends',
  runningText: 'Its results are worked out as soon as the site has been read, and the report shows here.',
  analysing: 'Working out the results. The report shows here when they’re ready.',
  outOfDate: (when) => `These results are out of date: they were worked out ${when}. The report says when.`,
  analyseAgain: 'Work out the results again',
  outOfDateReason: {
    crawlChanged: 'The site check went on after its results were worked out.',
    engineChanged: 'Shouting Iguana has changed how it checks a site since then.',
    settingsChanged: 'The checks’ limits have changed since then.',
  },
  outOfDateFallback: 'The site check changed after its results were worked out.',
};

const ADVANCED: ReportWords = {
  checked: 'crawled',
  notAnalysedTitle: 'This crawl hasn’t been analysed yet',
  notAnalysedText:
    'The report is made from the analysis: every check’s issues, and the score. The analysis takes a few seconds for most sites.',
  analyse: 'Analyse it',
  runningTitle: 'The report comes when the crawl finishes',
  runningText: 'It’s analysed as soon as it’s done, and the report shows here.',
  analysing: 'The crawl is being analysed. The report shows here when it’s done.',
  outOfDate: (when) => `This report is out of date: it’s from the analysis ${when}, as the report says.`,
  analyseAgain: 'Analyse again',
  outOfDateReason: {
    crawlChanged: 'The crawl was continued after it was analysed.',
    engineChanged: 'This version of Shouting Iguana checks differently from the one that analysed it.',
    settingsChanged: 'The analysis settings have changed since it was analysed.',
  },
  outOfDateFallback: 'The crawl changed after it was analysed.',
};

export function reportWords(mode: Mode): ReportWords {
  return mode === 'simple' ? SIMPLE : ADVANCED;
}

/** Why the results no longer stand, in the mode's words. */
export function outOfDateText(words: ReportWords, reasons: readonly OutOfDateReason[]): string {
  const said = reasons.map((reason) => words.outOfDateReason[reason]).filter(Boolean);
  return said.length > 0 ? said.join(' ') : words.outOfDateFallback;
}
