// What to fix's parts, by their place in its address (`#serious`): where the Summary's counts land. A count opens
// exactly what it counts, so a part that folds (Worth a look past its first three, Nothing wrong) opens when it's
// landed on.
import type { CheckOutcome } from '../ui/severity';

export const FIX_SECTIONS: Record<CheckOutcome, string> = {
  error: 'serious',
  warning: 'worth-fixing',
  notice: 'worth-a-look',
  skipped: 'couldnt-check',
  passed: 'nothing-wrong',
};

/** The part an address's hash names, or null. */
export function fixSectionOf(hash: string): CheckOutcome | null {
  const name = hash.replace(/^#/, '');
  const found = Object.entries(FIX_SECTIONS).find(([, id]) => id === name);
  return found ? (found[0] as CheckOutcome) : null;
}
