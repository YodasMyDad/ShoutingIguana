// What changed between a piece of code as it is and as it should be, for CodeExample to mark: the parts of the old
// code that go, and the parts of the new code that are new. Small inputs (a tag, a header, a few lines), so the plain
// quadratic longest-common-subsequence is all it needs.
//
// Lines are matched first, so a line taken out or put in is marked whole. Lines that changed are then compared word
// by word: an attribute added mid-tag marks the attribute, `noindex` to `index` marks the word, and `<h1>…</h1>` to
// `<h2>…</h2>` marks both tags and leaves the heading's words alone.

export interface DiffPart {
  text: string;
  /** In the old code: this part goes. In the new code: this part is new. */
  changed: boolean;
}

export interface CodeDiff {
  before: DiffPart[];
  after: DiffPart[];
}

/** A run that is the same on both sides (`before` and `after` equal), or a change: what goes and what comes. */
interface Step {
  before: string[];
  after: string[];
  same: boolean;
}

type Side = 'before' | 'after';

/** The steps from one list to another by their longest common subsequence, runs joined. */
function steps(before: readonly string[], after: readonly string[]): Step[] {
  const columns = after.length + 1;
  // lengths[i * columns + j]: the longest common subsequence of before[i..] and after[j..].
  const lengths = new Uint32Array((before.length + 1) * columns);
  const longest = (i: number, j: number) => lengths[i * columns + j] ?? 0;
  for (let i = before.length - 1; i >= 0; i--) {
    for (let j = after.length - 1; j >= 0; j--) {
      lengths[i * columns + j] =
        before[i] === after[j] ? longest(i + 1, j + 1) + 1 : Math.max(longest(i + 1, j), longest(i, j + 1));
    }
  }
  const out: Step[] = [];
  const push = (same: boolean, old: string | undefined, fresh: string | undefined) => {
    let last = out.at(-1);
    if (last?.same !== same) {
      last = { before: [], after: [], same };
      out.push(last);
    }
    if (old !== undefined) last.before.push(old);
    if (fresh !== undefined) last.after.push(fresh);
  };
  let i = 0;
  let j = 0;
  while (i < before.length || j < after.length) {
    if (i < before.length && j < after.length && before[i] === after[j]) {
      push(true, before[i], after[j]);
      i++;
      j++;
    } else if (j < after.length && (i === before.length || longest(i, j + 1) > longest(i + 1, j))) {
      push(false, undefined, after[j]);
      j++;
    } else {
      push(false, before[i], undefined);
      i++;
    }
  }
  return out;
}

/** Words (letters, digits, hyphens and underscores, so `aria-label` and `en_GB` are one), runs of spaces, and single marks. */
const TOKEN = /[\p{L}\p{N}_-]+|\s+|[^\p{L}\p{N}_\s-]/gu;
const WORD = /[\p{L}\p{N}]/u;

const length = (tokens: readonly string[]) => tokens.reduce((sum, token) => sum + token.length, 0);
const weight = (step: Step) => Math.max(length(step.before), length(step.after));

/**
 * The first match between two changes that only confuses, as its place in the steps, or -1: one made of punctuation
 * and spaces alone (the quotes and equals signs every tag has), or one no longer than the changes on both its sides
 * (`png` between two rewritten halves of a URL).
 */
function confusingMatch(all: readonly Step[]): number {
  return all.findIndex((step, index) => {
    const previous = all[index - 1];
    const next = all[index + 1];
    if (!step.same || !previous || !next) return false;
    const size = length(step.before);
    return !step.before.some((token) => WORD.test(token)) || (size <= weight(previous) && size <= weight(next));
  });
}

const asOneChange = (all: readonly Step[]): Step => ({
  same: false,
  before: all.flatMap((step) => step.before),
  after: all.flatMap((step) => step.after),
});

/**
 * Word-level steps with the matches that only confuse taken out. What's left changes in at most two places a side, or
 * it's shown as one change from the first to the last: three scattered marks in one tag read as noise, not as a fix.
 */
function wordSteps(before: string, after: string): Step[] {
  const old = before.match(TOKEN) ?? [];
  const fresh = after.match(TOKEN) ?? [];
  let start = 0;
  while (start < old.length && start < fresh.length && old[start] === fresh[start]) start++;
  let end = 0;
  while (end < old.length - start && end < fresh.length - start && old.at(-1 - end) === fresh.at(-1 - end)) end++;

  let middle = steps(old.slice(start, old.length - end), fresh.slice(start, fresh.length - end));
  for (let at = confusingMatch(middle); at >= 0; at = confusingMatch(middle)) {
    middle.splice(at - 1, 3, asOneChange(middle.slice(at - 1, at + 2)));
  }
  const changes = (side: Side) => middle.filter((step) => !step.same && step[side].length > 0).length;
  if (changes('before') > 2 || changes('after') > 2) middle = [asOneChange(middle)];
  return [
    { same: true, before: old.slice(0, start), after: fresh.slice(0, start) },
    ...middle,
    { same: true, before: old.slice(old.length - end), after: fresh.slice(fresh.length - end) },
  ];
}

/** Parts in order, empty ones dropped and neighbours of a kind joined. */
function joined(parts: readonly DiffPart[]): DiffPart[] {
  const out: DiffPart[] = [];
  for (const part of parts) {
    if (part.text === '') continue;
    const last = out.at(-1);
    if (last?.changed === part.changed) last.text += part.text;
    else out.push({ ...part });
  }
  return out;
}

/**
 * A mark that hugs what changed: line breaks and the spaces at its ends are left outside it (a new attribute is
 * marked, not the space before it; a new line, not its indent), and a mark that opens a quoted value takes the
 * closing quote too when it's the next character.
 */
function tidied(parts: readonly DiffPart[]): DiffPart[] {
  const out: DiffPart[] = [];
  const all = joined(parts);
  all.forEach((part, index) => {
    if (!part.changed) {
      out.push(part);
      return;
    }
    let text = part.text;
    const next = all[index + 1];
    if ((text.match(/"/g) ?? []).length % 2 === 1 && next?.text.startsWith('"')) {
      text += '"';
      next.text = next.text.slice(1);
    }
    for (const piece of text.split(/(\n)/)) {
      const [, lead = '', body = '', tail = ''] = /^(\s*)(.*?)(\s*)$/s.exec(piece) ?? [];
      out.push({ text: lead, changed: false }, { text: body, changed: true }, { text: tail, changed: false });
    }
  });
  return joined(out);
}

/**
 * What goes from `before` and what's new in `after`. Each side's parts join back into its text exactly. Text that's
 * the same on both sides has no part marked.
 */
export function diffCode(before: string, after: string): CodeDiff {
  const old: DiffPart[] = [];
  const fresh: DiffPart[] = [];
  const lines = steps(before.split('\n'), after.split('\n'));
  lines.forEach((step, index) => {
    // The line break between this run of lines and the next belongs to neither's change.
    const breakAfter = (side: Side) =>
      step[side].length > 0 && lines.slice(index + 1).some((later) => later[side].length > 0) ? '\n' : '';
    const oldText = step.before.join('\n');
    const freshText = step.after.join('\n');
    if (step.same) {
      old.push({ text: oldText, changed: false });
      fresh.push({ text: freshText, changed: false });
    } else if (step.before.length === 0 || step.after.length === 0) {
      old.push({ text: oldText, changed: true });
      fresh.push({ text: freshText, changed: true });
    } else {
      for (const words of wordSteps(oldText, freshText)) {
        old.push({ text: words.before.join(''), changed: !words.same });
        fresh.push({ text: words.after.join(''), changed: !words.same });
      }
    }
    old.push({ text: breakAfter('before'), changed: false });
    fresh.push({ text: breakAfter('after'), changed: false });
  });
  return { before: tidied(old), after: tidied(fresh) };
}
