// The grouped Links grid's column widths (step 3b's polish): the target's address takes what it needs to show whole
// while the grid has room for that, as the flat grid's URLs do (`fittedLinkWidths`), and the anchor text and where
// the links sit share the rest by what theirs take.
import type { LinkGroup } from '../../api/grids';
import { urlDisplay } from '../../ui/urlText';
import { type LinkFace } from './linkColumns';
import { otherTextsWords, positionsWords } from './linkGroups';

/** The columns that share the room by what their values take; the counts and statuses keep their own widths. */
export const FLEXIBLE_GROUP_COLUMNS = ['target', 'text', 'where'] as const;
type Flexible = (typeof FLEXIBLE_GROUP_COLUMNS)[number];

/** The least each flexible column is given, short of room. */
export const GROUP_LEAST: Readonly<Record<Flexible, number>> = { target: 220, text: 140, where: 152 };
/** The most each asks for: a longer value is cut, its whole a hover away. */
const GROUP_MOST: Readonly<Record<Flexible, number>> = { target: 720, text: 360, where: 400 };

/**
 * A cell's padding on both sides (`--grid-cell-x`), and what a target's cell holds beside its address, as drawn: the
 * tree's toggle with its gap, and the open button with the address's gap before it.
 */
const CELL_PADDING = 24;
const TOGGLE_ROOM = 34;
const URL_BUTTON_ROOM = 26;
const THUMB_ROOM = 40;
/** The gap between a text and its "+2 other texts", and between the template badge and the places. */
const TEXT_GAP = 6;
const BADGE_GAP = 8;
/** "Likely template" in its badge: the words with its icon and padding. */
const BADGE_ROOM = 40;

/**
 * The groups measured: about what a tall window shows of the view's first, so one long address further down (a page's
 * many-parametered link) doesn't take the room from the anchor text and the places of every row in sight.
 */
export const MEASURED_GROUPS = 30;

const isFlexible = (id: string): id is Flexible => (FLEXIBLE_GROUP_COLUMNS as readonly string[]).includes(id);

/**
 * What the target, its anchor text and where its links sit take to show whole, for the view's first
 * `MEASURED_GROUPS` groups: the widest of
 * each, as `measure` draws it, with the cell's padding and what sits beside it (the tree's toggle and the open button
 * beside a target's address, an image's thumbnail). An address is measured as it shows: its path on the crawl's own
 * origin. Columns with nothing in view are left out.
 */
export function groupColumnNeeds(
  groups: readonly LinkGroup[],
  images: boolean,
  siteOrigin: string | null,
  measure: (text: string, face: LinkFace) => number,
): Partial<Record<Flexible, number>> {
  let target = 0;
  let text = 0;
  let where = 0;
  for (const group of groups.slice(0, MEASURED_GROUPS)) {
    target = Math.max(target, measure(urlDisplay(group.url, siteOrigin).shown, 'url'));
    const others = otherTextsWords(group);
    text = Math.max(text, measure(group.text ?? 'None', 'text') + (others ? TEXT_GAP + measure(others, 'text') : 0));
    const places = positionsWords(group.positions);
    const badge = group.likelyTemplate ? measure('Likely template', 'text') + BADGE_ROOM : 0;
    where = Math.max(where, badge + (badge && places ? BADGE_GAP : 0) + (places ? measure(places, 'text') : 0));
  }
  const needs: Partial<Record<Flexible, number>> = {};
  if (target > 0) {
    needs.target = Math.ceil(target + CELL_PADDING + TOGGLE_ROOM + URL_BUTTON_ROOM + (images ? THUMB_ROOM : 0));
  }
  if (text > 0) needs.text = Math.ceil(text + CELL_PADDING);
  if (where > 0) needs.where = Math.ceil(where + CELL_PADDING);
  return needs;
}

/**
 * Each shown column's width in `available` px. The counts and statuses keep `widths`. With room for every flexible
 * value whole, each takes what it needs and the target takes the rest. Short of that, the target's address comes
 * first: it takes what it needs while the anchor text and the places keep their least, so an address is never cut in
 * the middle while the grid has room for it, and those two share what's left by what each wants past its least. With
 * less room still, each gets its least, and the grid scrolls. A column with nothing in view wants its default width.
 */
export function fittedGroupWidths(
  shown: readonly string[],
  widths: Readonly<Record<string, number>>,
  available: number,
  needs: Readonly<Partial<Record<Flexible, number>>>,
): Record<string, number> {
  const flexible = shown.filter(isFlexible);
  const fixed = shown.filter((id) => !isFlexible(id));
  const room = available - fixed.reduce((sum, id) => sum + (widths[id] ?? 120), 0);
  const wanted = (id: Flexible) =>
    Math.max(GROUP_LEAST[id], Math.min(GROUP_MOST[id], needs[id] ?? widths[id] ?? GROUP_LEAST[id]));
  const sum = (ids: readonly Flexible[], part: (id: Flexible) => number) =>
    ids.reduce((total, id) => total + part(id), 0);
  const others = flexible.filter((id) => id !== 'target');
  const out: Record<string, number> = Object.fromEntries(fixed.map((id) => [id, widths[id] ?? 120]));
  if (sum(flexible, wanted) <= room) {
    const taker = flexible.includes('target') ? 'target' : flexible[0];
    for (const id of flexible) out[id] = wanted(id) + (id === taker ? room - sum(flexible, wanted) : 0);
  } else if (room <= sum(flexible, (id) => GROUP_LEAST[id])) {
    for (const id of flexible) out[id] = GROUP_LEAST[id];
  } else {
    // The address first, then the others by what each wants past its least.
    const target = flexible.includes('target')
      ? Math.min(wanted('target'), room - sum(others, (id) => GROUP_LEAST[id]))
      : 0;
    if (flexible.includes('target')) out.target = target;
    const spare = room - target - sum(others, (id) => GROUP_LEAST[id]);
    const over = sum(others, (id) => wanted(id) - GROUP_LEAST[id]);
    for (const id of others) {
      out[id] = GROUP_LEAST[id] + (over > 0 ? (spare * (wanted(id) - GROUP_LEAST[id])) / over : spare / others.length);
    }
  }
  return Object.fromEntries(shown.map((id) => [id, Math.floor(out[id] ?? widths[id] ?? 120)]));
}
