// Which crawl a crawl is compared with (docs/v2-fix-loop.md, "Comparing with another crawl (Advanced)"): its previous
// crawl by default, another crawl of its site picked in Compare with…, or none ("Don't compare"). The app's
// preference, kept per crawl id as hidden checks are (`settings.compareWithByCrawl`).
import type { Settings } from './schema';
import { updateSettings, useSettings } from './settingsStore';

/** The crawl's previous crawl (the default), a crawl's id, or none. */
export type CompareChoice = 'previous' | 'none' | (string & {});

/** What a crawl is compared with, as the preference has it. */
export function compareChoiceOf(settings: Settings, crawlId: string): CompareChoice {
  return settings.compareWithByCrawl[crawlId] ?? 'previous';
}

/** The settings with a crawl's choice made: the previous crawl is no entry, and the crawl changed last goes last. */
export function withCompareChoice(
  settings: Settings,
  crawlId: string,
  choice: CompareChoice,
): Pick<Settings, 'compareWithByCrawl'> {
  const others = Object.fromEntries(Object.entries(settings.compareWithByCrawl).filter(([crawl]) => crawl !== crawlId));
  const kept = choice !== 'previous' && choice !== crawlId;
  return { compareWithByCrawl: kept ? { ...others, [crawlId]: choice } : others };
}

/** Compares a crawl with another crawl, with none, or with its previous crawl again. */
export function setCompareChoice(crawlId: string, choice: CompareChoice): void {
  updateSettings((settings) => withCompareChoice(settings, crawlId, choice));
}

/** What a crawl is compared with, as it changes. */
export function useCompareChoice(crawlId: string): CompareChoice {
  return useSettings((settings) => compareChoiceOf(settings, crawlId));
}
