import { render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { onAnnounce } from './announce';
import { useAnnounceResults } from './useAnnounceResults';

function Count({ scope = 'crawl', view, words }: { scope?: string; view: string; words: string | null }) {
  useAnnounceResults(scope, view, words);
  return null;
}

let heard: string[] = [];
let stop: () => void = () => undefined;
beforeEach(() => {
  heard = [];
  stop = onAnnounce((message) => heard.push(message));
});
afterEach(() => stop());

describe('a view’s result count', () => {
  it('isn’t said on arrival, nor as it moves under the same view: a running crawl changes it every second', () => {
    const { rerender } = render(<Count view="all" words={null} />);
    rerender(<Count view="all" words="4,521 URLs" />);
    for (let tick = 1; tick <= 5; tick += 1) rerender(<Count view="all" words={`${4_521 + tick} URLs`} />);
    expect(heard).toEqual([]);
  });

  it('is said once when the user changes the view, as soon as it’s known for it, and not as it moves after', () => {
    const { rerender } = render(<Count view="all" words="4,521 URLs" />);
    // A search: the count isn't known until its rows come.
    rerender(<Count view="q=patio" words={null} />);
    expect(heard).toEqual([]);
    rerender(<Count view="q=patio" words="264 of 4,521 URLs" />);
    expect(heard).toEqual(['264 of 4,521 URLs']);
    // The crawl goes on finding URLs.
    rerender(<Count view="q=patio" words="265 of 4,530 URLs" />);
    rerender(<Count view="q=patio" words="266 of 4,538 URLs" />);
    expect(heard).toEqual(['264 of 4,521 URLs']);
    // The search cleared: back to all of them, known at once.
    rerender(<Count view="all" words="4,538 URLs" />);
    expect(heard).toEqual(['264 of 4,521 URLs', '4,538 URLs']);
  });

  it('isn’t said for somewhere else: another crawl, another URL’s links', () => {
    const { rerender } = render(<Count scope="crawl a" view="q=patio" words="264 of 4,521 URLs" />);
    rerender(<Count scope="crawl b" view="all" words={null} />);
    rerender(<Count scope="crawl b" view="all" words="12 URLs" />);
    expect(heard).toEqual([]);
    // A choice made there is.
    rerender(<Count scope="crawl b" view="q=sofa" words="3 of 12 URLs" />);
    expect(heard).toEqual(['3 of 12 URLs']);
  });
});
