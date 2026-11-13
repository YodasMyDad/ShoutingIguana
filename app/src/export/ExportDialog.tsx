import { useRef, useState } from 'react';
import { errorTitle } from '../api/errors';
import type { ExportFormat, ReportCut } from '../api/exports';
import { focusFallback } from '../app/overlays';
import { analyseCrawl } from '../crawl/actions';
import { isTauri } from '../lib/platform';
import { useMode } from '../mode/mode';
import { useSettings } from '../settings/settingsStore';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { ChoiceCards } from '../ui/ChoiceCards';
import { Dialog } from '../ui/Dialog';
import { SegmentedControl } from '../ui/SegmentedControl';
import { TextField } from '../ui/TextField';
import {
  beginExport,
  chooseSavePath,
  closeExportDialog,
  defaultPath,
  useExportDialog,
  type ExportDialogState,
} from './exportFlow';
import type { ExportChoice } from './exportOffer';
import { exportErrorWords, FORMAT_TITLE, remedyTitle, type ExportRemedy } from './exportWords';

/**
 * The export dialog, once for the app (AppShell): what to export of the screen's offer, and as what. In the shell the
 * native save dialog then asks where; in a browser, which has none, a field here does. A refusal the engine gives at
 * once (a folder it can't write to, a file another program has open, a network share) is said here, with the action
 * that helps; an export that starts closes the dialog and its toast takes over.
 */
export function ExportDialogHost() {
  const state = useExportDialog();
  return state ? <ExportDialog key={state.seq ?? 0} state={state} /> : null;
}

export function ExportDialog({ state }: { state: ExportDialogState }) {
  const { offer } = state;
  const simple = useMode() === 'simple';
  const rememberedFormat = useSettings((settings) => settings.exportFormat);
  const first =
    offer.choices.find((choice) => choice.id === (state.choiceId ?? offer.initialChoice)) ?? offer.choices[0];
  const [choiceId, setChoiceId] = useState(first?.id ?? '');
  const choice = offer.choices.find((candidate) => candidate.id === choiceId) ?? first;
  const formatFor = (target: ExportChoice | undefined): ExportFormat =>
    target?.formats.includes(state.format ?? rememberedFormat)
      ? (state.format ?? rememberedFormat)
      : (target?.formats[0] ?? 'csv');
  const [format, setFormat] = useState<ExportFormat>(formatFor(first));
  const [cut, setCut] = useState<ReportCut>(state.cut ?? 'full');
  const [path, setPath] = useState(state.path ?? (first ? defaultPath(offer, first, formatFor(first), cut) : ''));
  const [pathEdited, setPathEdited] = useState(state.path !== undefined);
  const [working, setWorking] = useState(false);
  const [refusal, setRefusal] = useState(state.refusal ?? null);
  const [crawlOrder, setCrawlOrder] = useState(state.crawlOrder ?? false);
  const primary = useRef<HTMLButtonElement>(null);

  // The browser's field follows the choice and the format until the user types in it.
  const syncPath = (nextChoice: ExportChoice | undefined, nextFormat: ExportFormat, nextCut: ReportCut) => {
    if (!pathEdited && nextChoice) setPath(defaultPath(offer, nextChoice, nextFormat, nextCut));
  };
  const choose = (id: string) => {
    const next = offer.choices.find((candidate) => candidate.id === id);
    const nextFormat = formatFor(next);
    setChoiceId(id);
    setFormat(nextFormat);
    setRefusal(null);
    syncPath(next, nextFormat, cut);
  };

  const close = () => {
    if (!working) closeExportDialog();
  };

  const start = async (options: { samePath?: boolean; crawlOrder?: boolean; format?: ExportFormat } = {}) => {
    if (!choice || choice.unavailable) return;
    const useFormat = options.format ?? format;
    const order = options.crawlOrder ?? crawlOrder;
    setWorking(true);
    setRefusal(null);
    try {
      let target: string | null = path.trim();
      if (isTauri && !options.samePath) target = await chooseSavePath(offer, choice, useFormat, cut);
      if (!target) {
        // Cancelled in the save dialog: nothing happens, and the dialog stays for another go.
        setWorking(false);
        primary.current?.focus();
        return;
      }
      setPath(target);
      await beginExport({ offer, choiceId: choice.id, format: useFormat, cut, path: target, crawlOrder: order });
      closeExportDialog();
    } catch (error) {
      setWorking(false);
      const words = exportErrorWords(error, { path, format: useFormat, simple });
      setRefusal(words ?? { sentence: errorTitle(error), remedies: [] });
    }
  };

  const remedy = (which: ExportRemedy) => {
    switch (which) {
      case 'analyse':
        closeExportDialog();
        void analyseCrawl(offer.crawlId);
        return;
      case 'asCsv':
        setFormat('csv');
        syncPath(choice, 'csv', cut);
        void start({ format: 'csv' });
        return;
      case 'crawlOrder':
        setCrawlOrder(true);
        void start({ crawlOrder: true, samePath: true });
        return;
      case 'tryAgain':
      case 'exportAgain':
        void start({ samePath: true });
        return;
      case 'chooseAnother':
        void start();
        return;
    }
  };

  const fieldError = !isTauri && refusal?.remedies.includes('chooseAnother') ? refusal.sentence : null;
  const unavailable = choice?.unavailable ?? null;

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) close();
      }}
      title={offer.title}
      description={
        isTauri
          ? 'Choose what to export and as what. Then choose where to save it.'
          : 'Choose what to export, as what and where.'
      }
      onOpenAutoFocus={(event) => {
        // Straight to the action: Ctrl+E then Enter exports the view as it was last exported.
        event.preventDefault();
        primary.current?.focus();
      }}
      onCloseAutoFocus={(event) => {
        event.preventDefault();
        // Where focus was as it opened (the grid, the Export button), once the export has started or it was closed.
        const target = state.returnTo;
        if (target instanceof HTMLElement && target.isConnected && target !== document.body) {
          target.focus({ preventScroll: true });
        } else {
          focusFallback();
        }
      }}
      footer={
        <>
          <Button disabled={working} onClick={close}>
            Cancel
          </Button>
          {unavailable && choice?.remedy === 'analyse' ? (
            <Button ref={primary} variant="primary" onClick={() => remedy('analyse')}>
              {remedyTitle('analyse', simple)}
            </Button>
          ) : (
            <Button
              ref={primary}
              variant="primary"
              loading={working}
              disabled={!choice || unavailable !== null}
              onClick={() => void start()}
            >
              {isTauri ? 'Export…' : 'Export'}
            </Button>
          )}
        </>
      }
    >
      <div className="flex flex-col gap-5">
        {offer.note ? (
          <Callout tone="info" role="note">
            {offer.note}
          </Callout>
        ) : null}
        {offer.choices.length > 1 ? (
          <ChoiceCards
            legend="What to export"
            options={offer.choices.map((candidate) => ({
              value: candidate.id,
              title: candidate.title,
              ...(candidate.description ? { text: candidate.description } : {}),
            }))}
            value={choiceId}
            onValueChange={choose}
            columns={2}
          />
        ) : choice?.description ? (
          <p className="text-13 text-text-secondary">
            <span className="font-semibold text-text">{choice.title}: </span>
            {choice.description}
          </p>
        ) : null}
        {unavailable ? (
          <Callout tone="info" role="status">
            {unavailable}
          </Callout>
        ) : (
          <>
            {choice && choice.formats.length > 1 ? (
              <SegmentedControl
                label="Format"
                options={choice.formats.map((value) => ({
                  value,
                  label: FORMAT_TITLE[value],
                  description:
                    value === 'csv'
                      ? 'Text any spreadsheet opens, with no limit on rows'
                      : 'A workbook, its header row frozen and filtered',
                }))}
                value={format}
                onValueChange={(next) => {
                  setFormat(next);
                  setRefusal(null);
                  syncPath(choice, next, cut);
                }}
              />
            ) : null}
            {choice?.cuts ? (
              <SegmentedControl
                label="Report"
                options={[
                  {
                    value: 'full',
                    label: 'Full report',
                    description: 'Every problem, how to fix it, and its first pages',
                  },
                  {
                    value: 'summary',
                    label: 'Summary page',
                    description: 'The score, the counts and what to fix first',
                  },
                ]}
                value={cut}
                onValueChange={(next) => {
                  setCut(next);
                  syncPath(choice, format, next);
                }}
              />
            ) : null}
            {choice?.sortedWhileCrawling && !crawlOrder ? (
              <p className="text-12 text-text-secondary">
                While the crawl runs, an export is in crawl order or by URL. This view’s sort is by something the crawl
                changes, so the engine will say no to it: export it in crawl order, or once the crawl has ended.
              </p>
            ) : null}
            {isTauri ? null : (
              <TextField
                label="Save as"
                description="The file’s full path on this computer. The app’s window has a save dialog for this."
                mono
                value={path}
                error={fieldError}
                onChange={(event) => {
                  setPath(event.target.value);
                  setPathEdited(true);
                  setRefusal(null);
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    void start({ samePath: true });
                  }
                }}
              />
            )}
          </>
        )}
        {refusal && !fieldError ? (
          <Callout
            tone="error"
            role="alert"
            title="The export couldn’t start"
            actions={
              refusal.remedies.length > 0 ? (
                <div className="flex flex-wrap gap-1.5">
                  {refusal.remedies.map((which) => (
                    <Button key={which} size="sm" disabled={working} onClick={() => remedy(which)}>
                      {remedyTitle(which, simple)}
                    </Button>
                  ))}
                </div>
              ) : null
            }
          >
            {refusal.sentence}
          </Callout>
        ) : null}
      </div>
    </Dialog>
  );
}
