// How the source viewer draws particular lines, as a CodeMirror extension: a tone for the line (what applies, what
// doesn't), its name set apart (a directive's), a comment, words after it, and a link after it that chooses the line.
// One line can be marked as chosen. A robots.txt file is shown this way (screens/robots): what each line is comes from
// the engine, so nothing is parsed here. The marks are decorations over the text, never part of it: copying the text
// copies the file as it is.
import { StateEffect, StateField, type EditorState, type Extension, type Range, type Text } from '@codemirror/state';
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view';

export interface SourceLineMark {
  /** From 1. */
  line: number;
  /** `strong`: part of what applies, marked with the accent's rule. `quiet`: part of what doesn't, muted. */
  tone?: 'strong' | 'quiet';
  /** The characters from the line's start that are its name ("Disallow"), set apart. */
  nameEnd?: number;
  /** The line is a comment. */
  comment?: boolean;
  /** Quiet words after the line's text: why it was ignored. */
  note?: string;
  /** A link after the line's text ("Blocked 12 URLs"): a click on it, or Enter with the caret on the line, chooses it. */
  action?: string;
  /** What the link does, as its tooltip. */
  actionTitle?: string;
}

export interface SourceLines {
  marks: readonly SourceLineMark[];
  /** The line marked as chosen, from 1, or null. */
  chosen: number | null;
  decorations: DecorationSet;
}

/** The attribute a line's link carries: its line's number. The viewer listens for presses on it. */
export const LINE_ACTION_ATTRIBUTE = 'data-source-line-action';

class NoteWidget extends WidgetType {
  constructor(readonly text: string) {
    super();
  }
  override eq(other: NoteWidget): boolean {
    return other.text === this.text;
  }
  override toDOM(): HTMLElement {
    const element = document.createElement('span');
    element.className = 'cm-si-note';
    element.textContent = this.text;
    return element;
  }
}

class ActionWidget extends WidgetType {
  constructor(
    readonly line: number,
    readonly label: string,
    readonly title: string | undefined,
  ) {
    super();
  }
  override eq(other: ActionWidget): boolean {
    return other.line === this.line && other.label === this.label && other.title === this.title;
  }
  override toDOM(): HTMLElement {
    const element = document.createElement('span');
    element.className = 'cm-si-action';
    element.textContent = this.label;
    element.setAttribute(LINE_ACTION_ATTRIBUTE, String(this.line));
    if (this.title !== undefined) element.title = this.title;
    return element;
  }
  /** The viewer handles the press: the editor mustn't take it for a click in the text. */
  override ignoreEvent(): boolean {
    return false;
  }
}

const nameMark = Decoration.mark({ class: 'cm-si-name' });

/** A line's classes: its tone, whether it's a comment, whether it's the chosen one. */
export function lineClasses(mark: SourceLineMark | undefined, chosen: boolean): string {
  return [
    mark?.tone === 'strong' ? 'cm-si-line-strong' : null,
    mark?.tone === 'quiet' ? 'cm-si-line-quiet' : null,
    mark?.comment ? 'cm-si-comment' : null,
    chosen ? 'cm-si-line-chosen' : null,
  ]
    .filter((name) => name !== null)
    .join(' ');
}

/** The decorations for the marks and the chosen line. Lines the text doesn't have are passed over. */
export function decorateLines(doc: Text, marks: readonly SourceLineMark[], chosen: number | null): DecorationSet {
  const ranges: Range<Decoration>[] = [];
  const marked = new Set<number>();
  const add = (mark: SourceLineMark | undefined, number: number) => {
    if (!Number.isInteger(number) || number < 1 || number > doc.lines || marked.has(number)) return;
    marked.add(number);
    const line = doc.line(number);
    const classes = lineClasses(mark, number === chosen);
    if (classes !== '') ranges.push(Decoration.line({ class: classes }).range(line.from));
    if (!mark) return;
    const nameEnd = Math.min(mark.nameEnd ?? 0, line.length);
    if (nameEnd > 0) ranges.push(nameMark.range(line.from, line.from + nameEnd));
    if (mark.action !== undefined) {
      const widget = new ActionWidget(number, mark.action, mark.actionTitle);
      ranges.push(Decoration.widget({ widget, side: 1 }).range(line.to));
    }
    if (mark.note !== undefined) {
      ranges.push(Decoration.widget({ widget: new NoteWidget(mark.note), side: 2 }).range(line.to));
    }
  };
  for (const mark of marks) add(mark, mark.line);
  if (chosen !== null) add(undefined, chosen);
  return Decoration.set(ranges, true);
}

/** Sets how the lines are drawn. */
export const setSourceLineMarks = StateEffect.define<readonly SourceLineMark[]>();
/** Marks a line as chosen, or none. */
export const setSourceChosenLine = StateEffect.define<number | null>();

const EMPTY: SourceLines = { marks: [], chosen: null, decorations: Decoration.none };

export const sourceLinesField = StateField.define<SourceLines>({
  create: () => EMPTY,
  update(value, transaction) {
    let { marks, chosen } = value;
    for (const effect of transaction.effects) {
      if (effect.is(setSourceLineMarks)) marks = effect.value;
      else if (effect.is(setSourceChosenLine)) chosen = effect.value;
    }
    if (marks === value.marks && chosen === value.chosen && !transaction.docChanged) return value;
    return { marks, chosen, decorations: decorateLines(transaction.state.doc, marks, chosen) };
  },
  provide: (field) => EditorView.decorations.from(field, (value) => value.decorations),
});

export function sourceLinesOf(state: EditorState): SourceLines {
  return state.field(sourceLinesField);
}

/** Whether a line has a link to choose it by. */
export function hasLineAction(state: EditorState, line: number): boolean {
  return sourceLinesOf(state).marks.some((mark) => mark.line === line && mark.action !== undefined);
}

export const sourceLines: Extension = sourceLinesField;
