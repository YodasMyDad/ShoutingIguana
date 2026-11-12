// A grid row's menu (docs/v2-ui-ia.md, Keyboard, "In grids": Shift+F10 or the menu key; and a right-click): what can
// be done with the row, or with the selection it's in. One piece for every grid: the grid says which rows (`GridCore`'s
// `onContextMenu`), this holds the menu open at the point, gives the keyboard back to the row when it closes, and has
// the items every grid of URLs shares (inspect, open in the browser, copy the URLs, copy the rows). A screen adds its
// own after them.
import { ClipboardList, Copy, ExternalLink, PanelRight } from 'lucide-react';
import type { ReactNode } from 'react';
import { openInBrowser } from '../shell/os';
import { MenuAt, MenuItem } from '../ui/Menu';
import { rowsWords, type RowMenuState } from './rowMenuState';

export interface RowMenuProps<Row> {
  state: RowMenuState<Row> | null;
  onOpenChange: (open: boolean) => void;
  /** Its accessible name: "Menu for /about/", "Menu for 3 URLs". */
  label: string;
  children: ReactNode;
}

/** The menu itself, at the point it was opened from; closing it gives the keyboard back to the grid, on the row. */
export function RowMenu<Row>({ state, onOpenChange, label, children }: RowMenuProps<Row>) {
  return (
    <MenuAt
      at={state?.at ?? null}
      open={state?.open ?? false}
      onOpenChange={onOpenChange}
      label={label}
      onCloseFocus={() => state?.menu.focus()}
    >
      {children}
    </MenuAt>
  );
}

export interface UrlRowItemsProps {
  /** How many rows the menu acts on: the selection's, or 1. */
  count: number;
  /** The URL of the row the menu was opened on: what it inspects and opens. Undefined while it isn't read. */
  url: string | undefined;
  /** Opens the inspector on the row the menu was opened on; left out where the grid has no inspector. */
  onInspect?: (() => void) | undefined;
  /** Copies the URLs of the rows the menu acts on. */
  onCopyUrls: () => void;
  /** Copies the rows as tab-separated text, as Ctrl+C does. */
  onCopyRows: () => void;
  /** What a row is called in "Copy 3 rows" ("row", "link"). */
  rowNoun?: [string, string] | undefined;
}

/**
 * The items every grid of URLs shares, in their order: inspect the URL (Enter), open it in the browser, copy the URL
 * or the selection's URLs, copy the row or rows as tab-separated text (Ctrl+C). A screen's own follow them.
 */
export function UrlRowItems({ count, url, onInspect, onCopyUrls, onCopyRows, rowNoun }: UrlRowItemsProps) {
  const noun = rowNoun ?? ['row', 'rows'];
  const opens = url !== undefined && /^https?:\/\//i.test(url);
  return (
    <>
      {onInspect ? (
        <MenuItem icon={PanelRight} shortcut="enter" disabled={url === undefined} onSelect={onInspect}>
          Inspect
        </MenuItem>
      ) : null}
      <MenuItem
        icon={ExternalLink}
        disabled={!opens}
        onSelect={() => {
          if (url !== undefined) void openInBrowser(url);
        }}
      >
        Open in the browser
      </MenuItem>
      <MenuItem icon={Copy} onSelect={onCopyUrls}>
        {`Copy ${rowsWords(count, ['URL', 'URLs'])}`}
      </MenuItem>
      <MenuItem icon={ClipboardList} shortcut="mod+c" onSelect={onCopyRows}>
        {`Copy ${rowsWords(count, noun)}`}
      </MenuItem>
    </>
  );
}
