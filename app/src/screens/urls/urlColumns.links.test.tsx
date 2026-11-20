// What a click in the URL grid's cells opens (rules 1 and 5 of the UX round of 2026-11-19): a URL's text is a link to
// the inspector, another URL a row names is a link to that URL, and a count that stands for a list opens the list.
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { UrlRow } from '../../api/grids';
import { URL_COLUMNS } from '../../api/mock/urlColumns';
import { urlGridColumns, type UrlColumnContext } from './urlColumns';

const SITE = 'https://example.com';
const ROW: UrlRow = {
  id: 7,
  url: `${SITE}/old-page/`,
  type: 'page',
  statusCode: 301,
  redirectTarget: `${SITE}/new-page/`,
  canonicalUrl: `${SITE}/old-page/`,
  finalUrl: `${SITE}/new-page/`,
  inlinks: 3,
  outlinks: 0,
  issues: 2,
  depth: 4,
};

function cell(id: string, more: Partial<UrlColumnContext> = {}, row: UrlRow = ROW) {
  const context: UrlColumnContext = { siteOrigin: SITE, fullUrls: false, sort: [], crawling: false, ...more };
  const column = urlGridColumns([id], URL_COLUMNS, context)[0]!;
  return render(<>{column.cell(row)}</>);
}

describe('the URL grid’s links', () => {
  it('makes the URL’s text a link that inspects the row, on one click', async () => {
    const onInspect = vi.fn();
    cell('url', { onInspect });
    await userEvent.click(screen.getByRole('button', { name: ROW.url! }));
    expect(onInspect).toHaveBeenCalledWith(ROW);
  });

  it('leaves the URL as text, with no copy cursor’s button, where nothing inspects', () => {
    const { container } = cell('url');
    expect(container.querySelector('.si-url__go')).toBeNull();
    expect(container.querySelector('button.si-url__copy')).toBeNull();
  });

  it('makes where a redirect goes, and where it ends, links to those URLs', async () => {
    const onGoUrl = vi.fn();
    const target = cell('redirectTarget', { onGoUrl });
    await userEvent.click(screen.getByRole('button', { name: ROW.redirectTarget! }));
    expect(onGoUrl).toHaveBeenLastCalledWith(ROW.redirectTarget);
    target.unmount();
    cell('finalUrl', { onGoUrl });
    await userEvent.click(screen.getByRole('button', { name: ROW.finalUrl! }));
    expect(onGoUrl).toHaveBeenCalledTimes(2);
  });

  it('says “Self” for a canonical to the page itself: nowhere else to go', () => {
    const { container } = cell('canonicalUrl', { onGoUrl: vi.fn() });
    expect(container).toHaveTextContent('Self');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('makes a count that stands for a list a link to it, and leaves none and a plain number as text', async () => {
    const onCount = vi.fn();
    const inlinks = cell('inlinks', { onCount });
    const link = screen.getByRole('button', { name: '3: Show its 3 inlinks' });
    expect(link).toHaveAttribute('tabindex', '-1');
    await userEvent.click(link);
    expect(onCount).toHaveBeenCalledWith(ROW, 'inlinks');
    inlinks.unmount();

    const issues = cell('issues', { onCount });
    await userEvent.click(screen.getByRole('button', { name: '2: Show its 2 issues' }));
    expect(onCount).toHaveBeenLastCalledWith(ROW, 'issues');
    issues.unmount();

    // Zero has nothing to open.
    const none = cell('outlinks', { onCount });
    expect(screen.queryByRole('button')).toBeNull();
    expect(none.container).toHaveTextContent('0');
    none.unmount();

    // A depth is only a number.
    cell('depth', { onCount });
    expect(screen.queryByRole('button')).toBeNull();
  });
});
