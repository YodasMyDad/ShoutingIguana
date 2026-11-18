import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { onAnnounce } from '../feedback/announce';
import { Url } from './Url';
import { ELLIPSIS } from './urlText';

const SITE = 'https://www.gardenfurnituresets.co.uk';
const LONG = `${SITE}/harpswell-metal-patio-set-with-6-chairs-and-parasol/?colour=graphite`;
const CHAR = 8;

/** Lays URLs out as a browser would: monospace text, 8 px a character, in a box `width` px wide. */
function layOut(width: number) {
  class FakeCanvas {
    getContext() {
      return { font: '', measureText: (text: string) => ({ width: text.length * CHAR }) };
    }
  }
  vi.stubGlobal('OffscreenCanvas', FakeCanvas);
  // eslint-disable-next-line @typescript-eslint/unbound-method -- called with its element below
  const real = Element.prototype.getBoundingClientRect;
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return this.classList.contains('si-url') ? ({ width } as DOMRect) : real.call(this);
  });
}

const shownText = (container: HTMLElement) => container.querySelector('.si-url__shown')?.textContent ?? '';

const writeText = vi.fn(() => Promise.resolve());

beforeEach(() => {
  writeText.mockClear();
  Object.assign(navigator, { clipboard: { writeText } });
});

afterEach(() => vi.useRealTimers());

describe('Url', () => {
  it('truncates in the middle to its width, keeping the host and the end, and is read in full', () => {
    // The open button's room is padding, which jsdom computes as 0: the text gets all 50 characters.
    layOut(50 * CHAR);
    const { container } = render(<Url url={LONG} />);
    const shown = shownText(container);
    expect(shown).toHaveLength(50);
    expect(shown.startsWith(SITE)).toBe(true);
    expect(shown.endsWith('graphite')).toBe(true);
    expect(shown).toContain(ELLIPSIS);
    // What's read and found is the whole URL.
    expect(screen.getByRole('button', { name: LONG })).toBeInTheDocument();
    expect(screen.getByText(LONG)).toBeInTheDocument();
  });

  it('shows the path of a URL on the crawl’s own origin, and other versions in full', () => {
    layOut(400);
    const { container, rerender } = render(<Url url={`${SITE}/contact-us/`} siteOrigin={SITE} />);
    expect(shownText(container)).toBe('/contact-us/');
    expect(screen.getByRole('button', { name: `${SITE}/contact-us/` })).toBeInTheDocument();
    rerender(<Url url="http://www.gardenfurnituresets.co.uk/contact-us/" siteOrigin={SITE} />);
    expect(shownText(container)).toBe('http://www.gardenfurnituresets.co.uk/contact-us/');
  });

  it('leaves a URL that fits whole, its host muted', () => {
    layOut(1_000);
    const { container } = render(<Url url={`${SITE}/login/`} />);
    expect(shownText(container)).toBe(`${SITE}/login/`);
    expect(container.querySelector('.si-url__origin')).toHaveTextContent(SITE);
  });

  it('names a file by its name, then its query or folder, muted, and is still read and copied in full', async () => {
    layOut(160);
    const image = `${SITE}/media/5265/harpswell-metal-patio-set-9.jpg?width=896`;
    const { container } = render(<Url url={image} siteOrigin={SITE} label="file" />);
    expect(container.querySelector('.si-url__file')).toHaveTextContent('harpswell-metal-patio-set-9.jpg');
    // The stem is what's cut where the name doesn't fit, never its extension (the stylesheet cuts the stem at its end).
    expect(container.querySelector('.si-url__stem')).toHaveTextContent(/^harpswell-metal-patio-set-9$/);
    expect(container.querySelector('.si-url__ext')).toHaveTextContent(/^\.jpg$/);
    expect(container.querySelector('.si-url__rest')).toHaveTextContent('?width=896');
    // Never cut in the middle: the name leads, and what doesn't fit is cut at its end.
    expect(shownText(container)).not.toContain(ELLIPSIS);
    expect(screen.getByRole('button', { name: image })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: image }));
    expect(writeText).toHaveBeenCalledWith(image);
  });

  it('copies the full URL on click, and says so', async () => {
    layOut(20 * CHAR);
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    try {
      render(<Url url={LONG} />);
      await userEvent.click(screen.getByRole('button', { name: LONG }));
      expect(writeText).toHaveBeenCalledWith(LONG);
      expect(heard).toContain('Copied the URL.');
      expect(await screen.findByRole('tooltip')).toHaveTextContent('Copied');
    } finally {
      stop();
    }
  });

  it('is named by the full URL, and its tooltip describes it by the hint alone', async () => {
    layOut(400);
    render(<Url url={`${SITE}/`} />);
    const copy = screen.getByRole('button', { name: `${SITE}/` });
    act(() => copy.focus());
    await screen.findByRole('tooltip');
    expect(copy).toHaveAccessibleDescription('Click to copy');
  });

  it('offers to open a web URL in the browser, and nothing else', () => {
    layOut(400);
    const { rerender } = render(<Url url={`${SITE}/`} />);
    expect(screen.getByRole('button', { name: `Open ${SITE}/ in your browser` })).toBeInTheDocument();
    rerender(<Url url="mailto:hello@example.com" />);
    expect(screen.queryByRole('button', { name: /in your browser/ })).toBeNull();
    rerender(<Url url={`${SITE}/`} openable={false} copyable={false} />);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('is a link that goes somewhere when given `onGo`: a click calls it, and copies nothing', async () => {
    layOut(400);
    const onGo = vi.fn();
    const { rerender } = render(<Url url={`${SITE}/login/`} siteOrigin={SITE} onGo={onGo} openable={false} />);
    const link = screen.getByRole('button', { name: `${SITE}/login/` });
    expect(link).not.toHaveAttribute('tabindex');
    await userEvent.click(link);
    expect(onGo).toHaveBeenCalledTimes(1);
    expect(writeText).not.toHaveBeenCalled();
    // In a grid's cell it stays out of the tab order: the grid is one Tab stop.
    rerender(<Url url={`${SITE}/login/`} siteOrigin={SITE} onGo={onGo} openable={false} tabbable={false} />);
    expect(screen.getByRole('button', { name: `${SITE}/login/` })).toHaveAttribute('tabindex', '-1');
  });

  it('fits again when its box changes size', () => {
    let resize: (() => void) | undefined;
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: () => void) {
          resize = callback;
        }
        observe() {
          // Sizes come from the test.
        }
        disconnect() {
          // Nothing to stop.
        }
      },
    );
    layOut(1_000);
    const { container } = render(<Url url={LONG} />);
    expect(shownText(container)).toBe(LONG);
    layOut(30 * CHAR);
    act(() => resize?.());
    expect(shownText(container)).toHaveLength(30);
  });
});
