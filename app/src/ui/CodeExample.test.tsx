import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { onAnnounce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { CodeExample } from './CodeExample';

// The clipboard itself is copyText's business (and user-event keeps its own): here, what is copied and what is said.
vi.mock('../lib/clipboard', () => ({ copyText: vi.fn() }));

const BEFORE = '<a href="/"><svg viewBox="0 0 120 32">…</svg></a>';
const AFTER = '<a href="/" aria-label="Example Furniture"><svg viewBox="0 0 120 32">…</svg></a>';

beforeEach(() => {
  vi.mocked(copyText).mockReset().mockResolvedValue(true);
});

afterEach(() => vi.useRealTimers());

const blocks = (container: HTMLElement) =>
  [...container.querySelectorAll<HTMLElement>('.si-code-example__block')].map((block) => ({
    label: block.querySelector('.si-code-example__label')?.textContent,
    head: block.querySelector('.si-code-example__head')?.textContent,
    code: block.querySelector('pre')?.textContent,
    block,
  }));

describe('CodeExample', () => {
  it('shows the code as it is under "Now" and the corrected code under "Fixed", each whole', () => {
    const { container } = render(<CodeExample language="html" before={BEFORE} after={AFTER} />);
    const [now, fixed] = blocks(container);
    expect(now).toMatchObject({ label: 'Now', code: BEFORE });
    expect(fixed).toMatchObject({ label: 'Fixed', code: AFTER });
    expect(screen.getByRole('figure', { name: 'The fix as code' })).toBeInTheDocument();
    // What the code is written in, said once, on the first block.
    expect(now?.head).toContain('HTML');
    expect(fixed?.head).not.toContain('HTML');
  });

  it('marks the part that is new as an insertion, and says it in words for a screen reader', () => {
    const { container } = render(<CodeExample language="html" before={BEFORE} after={AFTER} />);
    const added = container.querySelectorAll('ins');
    expect([...added].map((mark) => mark.textContent)).toEqual(['aria-label="Example Furniture"']);
    expect(container.querySelector('del')).toBeNull();
    expect(container.querySelector('figcaption')).toHaveTextContent('Added: aria-label="Example Furniture".');
  });

  it('strikes the part that goes as a deletion in "Now"', () => {
    const { container } = render(
      <CodeExample
        language="html"
        before={'<meta name="robots" content="noindex, follow">'}
        after={'<meta name="robots" content="index, follow">'}
      />,
    );
    const [now, fixed] = blocks(container);
    expect(now?.block.querySelector('del')).toHaveTextContent('noindex');
    expect(fixed?.block.querySelector('ins')).toHaveTextContent('index');
    expect(fixed?.block.querySelector('del')).toBeNull();
    expect(container.querySelector('figcaption')).toHaveTextContent('Taken out: noindex. Added: index.');
  });

  it('shows something to add as one block, "Add", with where it goes beside the label', () => {
    const { container } = render(
      <CodeExample
        language="html"
        after={'<meta name="description" content="Solid oak">'}
        where="in the page’s <head>"
      />,
    );
    const shown = blocks(container);
    expect(shown).toHaveLength(1);
    expect(shown[0]?.label).toBe('Add');
    expect(shown[0]?.head).toContain('in the page’s <head>');
    expect(shown[0]?.head).toContain('HTML');
    expect(container.querySelector('ins, del, figcaption')).toBeNull();
    expect(screen.getByRole('figure', { name: 'The code to add' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy the code to add' })).toBeInTheDocument();
  });

  it('says where a fix goes beside "Now"', () => {
    const { container } = render(
      <CodeExample
        language="httpHeader"
        before="Cache-Control: max-age=604800"
        after="Cache-Control: no-store"
        where="in the redirect’s response headers"
      />,
    );
    const [now, fixed] = blocks(container);
    expect(now?.head).toContain('in the redirect’s response headers');
    expect(now?.head).toContain('HTTP header');
    expect(fixed?.head).not.toContain('response headers');
  });

  it('colours tags, attribute names and values, a header’s name, and leaves an unknown language plain', () => {
    const { container, rerender } = render(<CodeExample language="html" before={BEFORE} after={AFTER} />);
    const fixed = () => container.querySelector('.si-code-example__block--fixed pre')!;
    expect(fixed().querySelector('.si-code-example__tag')).toHaveTextContent('<a');
    expect([...fixed().querySelectorAll('.si-code-example__attribute')].map((name) => name.textContent)).toEqual([
      'href',
      'aria-label',
      'viewBox',
    ]);
    // A mark keeps the colours of the code inside it.
    expect(fixed().querySelector('ins .si-code-example__attribute')).toHaveTextContent('aria-label');
    expect(fixed().querySelector('ins .si-code-example__string')).toHaveTextContent('"Example Furniture"');

    rerender(<CodeExample language="httpHeader" after="X-Content-Type-Options: nosniff" />);
    expect(fixed().querySelector('.si-code-example__attribute')).toHaveTextContent('X-Content-Type-Options');

    rerender(<CodeExample language="nginx" after="add_header X-Frame-Options SAMEORIGIN;" />);
    expect(fixed().querySelector('span span')).toBeNull();
    expect(fixed()).toHaveTextContent('add_header X-Frame-Options SAMEORIGIN;');
    expect(container.querySelector('.si-code-example__language')).toBeNull();
  });

  it('copies the corrected code, says so on the button and to screen readers, then offers it again', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    render(<CodeExample language="html" before={BEFORE} after={AFTER} />);

    await user.click(screen.getByRole('button', { name: 'Copy the fixed code' }));
    expect(copyText).toHaveBeenCalledWith(AFTER);
    const copied = await screen.findByRole('button', { name: 'Copied the fixed code' });
    expect(copied).toHaveTextContent('Copied');
    expect(heard).toEqual(['Copied the fixed code.']);

    act(() => void vi.advanceTimersByTime(2000));
    expect(screen.getByRole('button', { name: 'Copy the fixed code' })).toHaveTextContent('Copy');
    stop();
  });

  it('is reached and worked by the keyboard', async () => {
    const user = userEvent.setup();
    render(<CodeExample language="html" before={BEFORE} after={AFTER} />);
    await user.tab();
    expect(screen.getByRole('button', { name: 'Copy the fixed code' })).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(copyText).toHaveBeenCalledWith(AFTER);
  });

  it('says when the code could not be copied, and leaves the button as it was', async () => {
    vi.mocked(copyText).mockResolvedValue(false);
    const user = userEvent.setup();
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    render(<CodeExample language="html" after="<title>Oak dining tables</title>" />);
    const figure = screen.getByRole('figure');
    await user.click(within(figure).getByRole('button', { name: 'Copy the code to add' }));
    expect(heard).toEqual(['The code couldn’t be copied.']);
    expect(within(figure).getByRole('button', { name: 'Copy the code to add' })).toHaveTextContent('Copy');
    stop();
  });
});
