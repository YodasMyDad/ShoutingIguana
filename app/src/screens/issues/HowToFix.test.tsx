import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { Check } from '../../api/types';
import { copyText } from '../../lib/clipboard';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { HowToFix } from './HowToFix';

vi.mock('../../lib/clipboard', () => ({ copyText: vi.fn() }));

/** A check as the engine's catalog has it (the captured `GET /api/checks`). */
const check = (id: string): Check => {
  const found = SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id);
  if (!found) throw new Error(`No check ${id} in the captured catalog`);
  return found;
};

const EMPTY_LINK = check('empty-anchor-text');
const CASES = ['The link wraps an icon or an inline SVG', 'The link wraps an image', 'The link has nothing inside'];

/** The code in view: a lone example's, or the chosen case's. */
const shownCode = (container: HTMLElement) =>
  [...container.querySelectorAll('.si-code-example pre')]
    .filter((code) => !code.closest('[hidden]'))
    .map((code) => code.textContent);

describe('HowToFix', () => {
  it('is the heading and the fix’s words alone for a check without examples, as it always was', () => {
    const { container } = render(<HowToFix check={check('title-too-long')} />);
    expect(screen.getByRole('heading', { level: 3, name: 'How to fix it' })).toHaveClass(
      'mb-1.5',
      'text-13',
      'font-semibold',
    );
    expect(screen.getByText(check('title-too-long').fix)).toHaveClass('text-14', 'leading-body', 'text-text-secondary');
    expect(container.querySelector('.si-code-example, [role="tablist"]')).toBeNull();
    expect(container.querySelector('.si-how-to-fix')?.children).toHaveLength(2);
  });

  it('reads a check from a sidecar that sends no examples as one without', () => {
    const { container } = render(<HowToFix check={{ fix: 'Shorten the title.' }} />);
    expect(screen.getByText('Shorten the title.')).toBeInTheDocument();
    expect(container.querySelector('.si-code-example')).toBeNull();
  });

  it('shows nothing for a check with no fix at all', () => {
    const { container } = render(<HowToFix check={{ fix: ' ', fixExamples: [] }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows a check’s one example under its words, with no cases to choose', () => {
    const missing = check('description-missing');
    const { container } = render(<HowToFix check={missing} />);
    expect(screen.getByText(missing.fix)).toBeInTheDocument();
    expect(screen.queryByRole('tablist')).toBeNull();
    const figure = screen.getByRole('figure', { name: 'The code to add' });
    expect(within(figure).getByText('Add')).toBeInTheDocument();
    expect(figure).toHaveTextContent("in the page's <head>");
    expect(shownCode(container)).toEqual([missing.fixExamples?.[0]?.after]);
  });

  it('leads a lone example that is conditional with its condition', () => {
    const cached = check('redirect-temporary-cached');
    render(<HowToFix check={cached} />);
    expect(screen.getByText('The redirect really is temporary:')).toBeInTheDocument();
    expect(screen.queryByRole('tablist')).toBeNull();
    expect(screen.getByRole('figure', { name: 'The fix as code' })).toHaveTextContent('Cache-Control: no-store');
  });

  it('offers a check’s cases as tabs named by when they apply, the first showing', () => {
    const { container } = render(<HowToFix check={EMPTY_LINK} />);
    const tabs = within(screen.getByRole('tablist', { name: 'Which case is it' })).getAllByRole('tab');
    expect(tabs.map((tab) => tab.textContent)).toEqual(CASES);
    expect(tabs.map((tab) => tab.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(shownCode(container)).toEqual([EMPTY_LINK.fixExamples?.[0]?.before, EMPTY_LINK.fixExamples?.[0]?.after]);
    // No case is marked without something to go by.
    expect(screen.queryByText('On this site')).toBeNull();
  });

  it('shows the case the crawl points to first, and marks it in words', () => {
    const { container } = render(<HowToFix check={EMPTY_LINK} preferredCase="image-link" />);
    const chosen = screen.getByRole('tab', { selected: true });
    expect(chosen).toHaveTextContent('The link wraps an image');
    expect(within(chosen).getByText('On this site')).toBeInTheDocument();
    expect(screen.getAllByText('On this site')).toHaveLength(1);
    expect(shownCode(container)[1]).toBe('<a href="/"><img src="/img/logo.png" alt="Example Furniture"></a>');
  });

  it('falls back to the first case for one the check does not have', () => {
    render(<HowToFix check={EMPTY_LINK} preferredCase="no-such-case" />);
    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent(CASES[0]!);
    expect(screen.queryByText('On this site')).toBeNull();
  });

  it('switches case on a click and with the arrow keys, wrapping round, and keeps the marker where it was', async () => {
    const user = userEvent.setup();
    const { container } = render(<HowToFix check={EMPTY_LINK} preferredCase="icon-link" />);
    await user.click(screen.getByRole('tab', { name: /The link has nothing inside/ }));
    expect(shownCode(container)).toEqual(['<a href="/sale/"></a>', '<a href="/sale/">Sale</a>']);

    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent(CASES[0]!);
    expect(screen.getByRole('tab', { selected: true })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent(CASES[1]!);
    await user.keyboard('{ArrowLeft}{ArrowLeft}');
    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent(CASES[2]!);
    expect(
      within(screen.getByRole('tab', { name: /icon or an inline SVG/ })).getByText('On this site'),
    ).toBeInTheDocument();
  });

  it('copies the case that is showing', async () => {
    vi.mocked(copyText).mockReset().mockResolvedValue(true);
    const user = userEvent.setup();
    render(<HowToFix check={EMPTY_LINK} preferredCase="icon-link" />);
    await user.click(screen.getByRole('button', { name: 'Copy the fixed code' }));
    await user.click(screen.getByRole('tab', { name: /The link wraps an image/ }));
    await user.click(screen.getByRole('button', { name: 'Copy the fixed code' }));
    expect(vi.mocked(copyText).mock.calls).toEqual([
      ['<a href="/" aria-label="Example Furniture"><svg viewBox="0 0 120 32">…</svg></a>'],
      ['<a href="/"><img src="/img/logo.png" alt="Example Furniture"></a>'],
    ]);
  });

  it('shows the first example of every check in the catalog that has any', () => {
    const withExamples = SIMPLE_CHECKS.checks.filter((candidate) => (candidate.fixExamples ?? []).length > 0);
    expect(withExamples).toHaveLength(67);
    for (const candidate of withExamples) {
      const examples = candidate.fixExamples ?? [];
      const { container, unmount } = render(<HowToFix check={candidate} />);
      expect(container.querySelectorAll('[role="tab"]')).toHaveLength(examples.length > 1 ? examples.length : 0);
      expect(shownCode(container).at(-1)).toBe(examples[0]?.after);
      unmount();
    }
  });
});
