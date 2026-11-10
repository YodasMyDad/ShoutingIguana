import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatShownOf } from '../lib/format';
import { GridHeader, ResultCount } from './PageHeader';

describe('a grid screen’s header', () => {
  it('shows the count without making it a live region: it changes every second while a crawl runs', () => {
    const { rerender } = render(
      <GridHeader title="URLs" count={<ResultCount shown={264} total={4_521} noun="URL" />} />,
    );
    const header = screen.getByRole('banner');
    expect(header).toHaveTextContent('264 of 4,521 URLs');
    expect(header.querySelector('[aria-live], [role="status"], [role="alert"], [role="log"]')).toBeNull();
    // Nor while it isn't known yet: a region that's live when the count comes would read it out.
    rerender(<GridHeader title="URLs" count={null} />);
    expect(header.querySelector('[aria-live], [role="status"], [role="alert"], [role="log"]')).toBeNull();
  });

  it('reads as the words a screen announces for it', () => {
    const cases: [number, number | null, string][] = [
      [264, 4_521, '264 of 4,521 URLs'],
      [4_521, 4_521, '4,521 URLs'],
      [1, 1, '1 URL'],
      [1, 2, '1 of 2 URLs'],
      [0, 4_521, '0 of 4,521 URLs'],
      [12, null, '12 URLs'],
    ];
    for (const [shown, total, words] of cases) {
      const { container, unmount } = render(<ResultCount shown={shown} total={total} noun="URL" />);
      expect(container).toHaveTextContent(words);
      expect(formatShownOf(shown, total, 'URL')).toBe(words);
      unmount();
    }
  });
});
