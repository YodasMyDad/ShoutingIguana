import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CrawlListItem } from '../api/types';
import { CrawlFavicon } from './CrawlFavicon';
import { faviconRequests, resetFavicons } from './favicons';

vi.mock('../api/client', () => ({ api: { favicon: vi.fn() } }));
const { api } = await import('../api/client');

const item = (id: string, hasFavicon: boolean) =>
  ({ id, site: 'example.com', fileName: 'x.sicrawl', hasFavicon }) as CrawlListItem;

describe('a crawl’s icon', () => {
  beforeEach(() => {
    resetFavicons();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:icon', revokeObjectURL: () => undefined }));
  });
  afterEach(() => vi.mocked(api.favicon).mockReset());

  it('is asked for once a session however many places show it, and shown once read', async () => {
    vi.mocked(api.favicon).mockResolvedValue(new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' }));
    const { container } = render(
      <>
        <CrawlFavicon item={item('a', true)} />
        <CrawlFavicon item={item('a', true)} size="lg" />
        <CrawlFavicon item={item('a', true)} size="sm" />
      </>,
    );
    await waitFor(() => expect(container.querySelectorAll('img')).toHaveLength(3));
    expect(faviconRequests('a')).toBe(1);
    expect(api.favicon).toHaveBeenCalledTimes(1);
  });

  it('isn’t asked for when the crawl has none, and a 204 or a failure leaves the letter', async () => {
    render(<CrawlFavicon item={item('none', false)} />);
    await Promise.resolve();
    expect(api.favicon).not.toHaveBeenCalled();
    expect(screen.getByText('E')).toBeInTheDocument();

    vi.mocked(api.favicon).mockResolvedValue(null);
    const { container } = render(<CrawlFavicon item={item('empty', true)} />);
    await waitFor(() => expect(faviconRequests('empty')).toBe(1));
    await Promise.resolve();
    expect(container.querySelector('img')).toBeNull();
  });

  it('shows the letter in its place when the image can’t be drawn, and doesn’t ask again', async () => {
    vi.mocked(api.favicon).mockResolvedValue(new Blob([new Uint8Array([9])], { type: 'image/png' }));
    const first = render(<CrawlFavicon item={item('broken', true)} />);
    const image = await waitFor(() => {
      const found = first.container.querySelector('img');
      if (!found) throw new Error('not yet');
      return found;
    });
    fireEvent.error(image);
    expect(first.container.querySelector('img')).toBeNull();
    expect(first.container.textContent).toBe('E');
    first.unmount();
    render(<CrawlFavicon item={item('broken', true)} />);
    await Promise.resolve();
    expect(faviconRequests('broken')).toBe(1);
  });
});
