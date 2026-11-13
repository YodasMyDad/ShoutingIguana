import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { installKeyboard } from '../keyboard/registry';
import { testCrawlItem } from '../test/fakeSidecar';
import { closeExportDialog, getExportDialog } from './exportFlow';
import { urlExportColumns, urlGridOffer } from './offers';
import { useExportCommand } from './useExportCommand';

const offer = urlGridOffer({
  item: testCrawlItem(),
  crawling: false,
  columns: urlExportColumns(['url']),
  filters: [],
  sort: [],
  search: undefined,
  rows: 3,
  viewName: null,
  linkEnds: false,
});

function Screen() {
  useExportCommand(() => offer, [], { group: 'URLs' });
  return <input aria-label="Search URLs and titles" />;
}

afterEach(() => closeExportDialog());

describe('Export (Ctrl+E)', () => {
  it('opens the dialog from a grid’s search field too, as every grid has one (the review’s finding 3)', () => {
    const uninstall = installKeyboard();
    render(<Screen />);
    const field = screen.getByRole('textbox', { name: 'Search URLs and titles' });
    field.focus();
    fireEvent.keyDown(field, { key: 'e', code: 'KeyE', ctrlKey: true });
    expect(getExportDialog()?.offer.title).toBe('Export URLs');
    uninstall();
  });
});
