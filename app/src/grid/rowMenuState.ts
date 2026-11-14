// The row menu's state and what its items do, for `RowMenu.tsx` and the screens that use it: which rows a menu is of,
// its words by how many, and copying the rows' URLs.
import { useCallback, useState } from 'react';
import { announce } from '../feedback/announce';
import { notifyError, notifyResult } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { formatCount, formatCountOf } from '../lib/format';
import type { GridMenuContext } from './GridCore';
import { MAX_COPY_ROWS } from './tsv';

/** A row's menu, open or just closed: the row it's of, the column it was opened in, where, and the rows it acts on. */
export interface RowMenuState<Row> {
  index: number;
  column: number;
  at: { x: number; y: number };
  menu: GridMenuContext<Row>;
  open: boolean;
  /**
   * Which opening this is, counted: each opening is a menu of its own (`RowMenu` keys on it), placed afresh at its
   * point. One menu kept open across two right-clicks (the second closes the first as it opens) kept the first's
   * place: the point it's anchored to moved, and nothing told the menu to look again.
   */
  opening: number;
}

/**
 * The state of a grid's row menu, and the handler `GridCore`'s `onContextMenu` takes. The state stays after the menu
 * closes (closed), so the keyboard can be given back to the row it was opened on.
 */
export function useRowMenu<Row>() {
  const [state, setState] = useState<RowMenuState<Row> | null>(null);
  const onContextMenu = useCallback(
    (index: number, column: number, at: { x: number; y: number }, menu: GridMenuContext<Row>) =>
      setState((current) => ({ index, column, at, menu, open: true, opening: (current?.opening ?? 0) + 1 })),
    [],
  );
  const onOpenChange = useCallback(
    (open: boolean) => setState((current) => (current ? { ...current, open } : current)),
    [],
  );
  return { state, onContextMenu, onOpenChange };
}

/** "the URL" or "3 URLs": what a row menu's copy takes, by how many rows it acts on. */
export function rowsWords(count: number, noun: [string, string]): string {
  return count === 1 ? `the ${noun[0]}` : `${formatCount(count)} ${noun[1]}`;
}

/** What a menu of rows is named, for a screen reader: "Menu for /about/", "Menu for 12 URLs". */
export function rowMenuLabel(count: number, one: string | undefined, noun: [string, string]): string {
  return count === 1 ? `Menu for ${one ?? `the ${noun[0]}`}` : `Menu for ${formatCount(count)} ${noun[1]}`;
}

/**
 * Copies the URLs of the rows a menu acts on, one per line, each once, in the rows' order, and says how many it
 * copied. Rows that couldn't be read are left out and said.
 */
export async function copyRowUrls<Row>(
  menu: GridMenuContext<Row>,
  urlOf: (row: Row) => string | null | undefined,
): Promise<void> {
  try {
    const rows = await menu.rows();
    const urls = [...new Set(rows.map((row) => (row === undefined ? null : urlOf(row))).filter((url) => !!url))];
    if (urls.length === 0) {
      notifyError(null, { title: 'The URLs couldn’t be copied.', description: 'None of the rows could be read.' });
      return;
    }
    if (!(await copyText(urls.join('\n')))) {
      notifyError(null, { title: 'The URLs couldn’t be copied.', description: 'The clipboard didn’t take it.' });
      return;
    }
    const unread = rows.filter((row) => row === undefined).length;
    const said = `Copied ${formatCountOf(urls.length, 'URL')}.${
      unread > 0 ? ` ${formatCountOf(unread, 'row')} couldn’t be read.` : ''
    }`;
    announce(said);
    // Of a selection bigger than a menu takes, only the first part is copied: said, never left to be found out.
    if (menu.truncated) {
      notifyResult(`Copied the first ${formatCount(MAX_COPY_ROWS)} rows’ URLs`, {
        description: 'A copy takes no more than that.',
      });
    }
  } catch (error) {
    notifyError(error, { title: 'The URLs couldn’t be copied.' });
  }
}
