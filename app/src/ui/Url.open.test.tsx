// The open-in-browser button sits straight after a URL's text, in the text's own line (never at the far edge of its
// cell), and can open another address than the one shown: the page at the words of the fault.
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type * as Os from '../shell/os';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Url } from './Url';
import { ELLIPSIS } from './urlText';

const opened = vi.hoisted(() => [] as string[]);
vi.mock('../shell/os', async (original) => ({
  ...(await original<typeof Os>()),
  openInBrowser: (url: string) => {
    opened.push(url);
    return Promise.resolve(true);
  },
}));

const SITE = 'https://www.gardenfurnituresets.co.uk';
const PAGE = `${SITE}/blog/best-rattan-patio-sets/`;
const CHAR = 8;
const BUTTON = 24;

/** Lays a URL out in a box `width` px wide: 8 px a character, and a 24 px open button. */
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
    if (this.classList.contains('si-url__open')) return { width: BUTTON } as DOMRect;
    return this.classList.contains('si-url') ? ({ width } as DOMRect) : real.call(this);
  });
}

afterEach(() => {
  opened.splice(0);
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Url’s open-in-browser button', () => {
  it('follows the text: the text is cut to the room the button leaves, not laid under it', () => {
    layOut(40 * CHAR);
    const { container } = render(<Url url={`${PAGE}?colour=graphite&size=large`} />);
    const root = container.querySelector('.si-url')!;
    // The text, then the button: two children in one line.
    expect(root.children).toHaveLength(2);
    expect(root.firstElementChild).toHaveClass('si-url__copy');
    expect(root.lastElementChild).toHaveClass('si-url__open');
    const shown = container.querySelector('.si-url__shown')?.textContent ?? '';
    expect(shown).toContain(ELLIPSIS);
    expect(shown).toHaveLength((40 * CHAR - BUTTON) / CHAR);
  });

  it('opens the URL shown, unless told another: the page at the words of the fault, and says so', async () => {
    layOut(800);
    const atWords = `${PAGE}#:~:text=Buy%20Now`;
    const { rerender } = render(<Url url={PAGE} />);
    await userEvent.click(screen.getByRole('button', { name: `Open ${PAGE} in your browser` }));
    expect(opened).toEqual([PAGE]);

    rerender(<Url url={PAGE} openUrl={atWords} openHint="Open the page at this link" />);
    // Still named by the page it opens; the tooltip says where on it.
    const open = screen.getByRole('button', { name: `Open ${PAGE} in your browser` });
    expect(open).toHaveAttribute('data-open-url', atWords);
    act(() => {
      open.blur();
      open.focus();
    });
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Open the page at this link');
    await userEvent.click(open);
    expect(opened).toEqual([PAGE, atWords]);
  });

  it('is plain text, not a control, where a click does nothing', () => {
    layOut(800);
    const { container } = render(<Url url={PAGE} copyable={false} openable={false} />);
    expect(screen.queryByRole('button')).toBeNull();
    expect(container.querySelector('span.si-url__copy')).not.toBeNull();
  });
});

describe('the URL styles', () => {
  const css = readFileSync(resolve(__dirname, '../styles/components.css'), 'utf8');
  const rule = (selector: string) => {
    const at = css.indexOf(`\n  ${selector} {`);
    expect(at, selector).toBeGreaterThan(-1);
    return css.slice(at, css.indexOf('}', at));
  };

  it('never put the open button at the far edge of the cell', () => {
    expect(rule('.si-url__open')).not.toMatch(/position:\s*absolute/);
    expect(rule('.si-url')).toMatch(/display:\s*flex/);
  });

  it('show the button always, muted, and in a big grid on the row pointed at or under the cursor', () => {
    expect(rule('.si-url__open')).toMatch(/opacity:\s*0\.6/);
    expect(rule('.si-open-on-row .si-grid-row .si-url__open')).toMatch(/opacity:\s*0;/);
    expect(css).toContain('.si-open-on-row .si-grid-row:has(> [data-active]) .si-url__open');
  });

  it('keep the copy cursor for the URL that copies on a click', () => {
    expect(rule('.si-url__copy')).not.toMatch(/cursor:\s*copy/);
    expect(rule('button.si-url__copy')).toMatch(/cursor:\s*copy/);
  });
});
