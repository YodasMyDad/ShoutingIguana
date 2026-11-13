import { useCommands, type Command, type CommandGroup } from '../keyboard/registry';
import { openExportDialog } from './exportFlow';
import type { ExportOffer } from './exportOffer';

/** The command every screen registers for Export (Ctrl+E): one id, so the key, the button and the palette agree. */
export const EXPORT_COMMAND = 'export.view';

/** The palette's "The report…" and "Spreadsheet of everything…", on Overview and Issues: the dialog on that choice. */
export const CRAWL_FILE_COMMANDS = [
  {
    id: 'export.report',
    title: 'Save the report…',
    choiceId: 'report',
    keywords: ['report', 'html', 'pdf', 'print', 'summary'],
  },
  {
    id: 'export.workbook',
    title: 'Save the spreadsheet of everything…',
    choiceId: 'workbook',
    keywords: ['workbook', 'excel', 'xlsx', 'every check', 'spreadsheet'],
  },
] as const;

/**
 * Registers Export (Ctrl+E) for the screen on show: it opens the export dialog on what the screen offers now, read as
 * the key is pressed, so the file is of the view as it then is. `offer` is read lazily, and `deps` are what it reads;
 * null offers nothing (the view's read hasn't come yet). `choices` are commands of the same offer opened on one of its
 * choices ("Save the report…"), for the palette.
 */
export function useExportCommand(
  offer: () => ExportOffer | null,
  deps: readonly unknown[],
  options: {
    group: CommandGroup;
    title?: string | undefined;
    /** Commands for single choices of the offer, each opening the dialog on it. */
    choices?: readonly { id: string; title: string; choiceId: string; keywords?: readonly string[] }[] | undefined;
  },
): void {
  useCommands(
    (): Command[] => [
      {
        id: EXPORT_COMMAND,
        title: options.title ?? 'Export this view…',
        group: options.group,
        keys: ['mod+e'],
        keywords: ['export', 'csv', 'excel', 'xlsx', 'download', 'save', 'spreadsheet'],
        enabled: () => offer() !== null,
        run: () => {
          const current = offer();
          if (current) openExportDialog(current);
        },
      },
      ...(options.choices ?? []).map((choice): Command => ({
        id: choice.id,
        title: choice.title,
        group: options.group,
        keywords: ['export', 'save', ...(choice.keywords ?? [])],
        enabled: () => offer()?.choices.some((each) => each.id === choice.choiceId) === true,
        run: () => {
          const current = offer();
          if (current) openExportDialog(current, { choiceId: choice.choiceId });
        },
      })),
    ],
    [options.group, options.title, options.choices, ...deps],
  );
}
