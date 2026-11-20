import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Check } from '../../api/types';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { HowToFix } from './HowToFix';

/** A check as the engine's catalog has it (the captured `GET /api/checks`). */
const check = (id: string): Check => {
  const found = SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id);
  if (!found) throw new Error(`No check ${id} in the captured catalog`);
  return found;
};

describe('HowToFix', () => {
  it('is the heading and the fix’s words', () => {
    const { container } = render(<HowToFix check={check('title-too-long')} />);
    expect(screen.getByRole('heading', { level: 3, name: 'How to fix it' })).toHaveClass(
      'mb-1.5',
      'text-13',
      'font-semibold',
    );
    expect(screen.getByText(check('title-too-long').fix)).toHaveClass('text-14', 'leading-body', 'text-text-secondary');
    expect(container.querySelector('.si-how-to-fix')?.children).toHaveLength(2);
    expect(container.querySelector('[role="tablist"], figure')).toBeNull();
  });

  it('shows nothing for a check with no fix at all', () => {
    const { container } = render(<HowToFix check={{ fix: ' ' }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('keeps the words of a check that used to show its fix as code, and no sample', () => {
    const { container } = render(<HowToFix check={check('empty-anchor-text')} />);
    expect(screen.getByText(check('empty-anchor-text').fix)).toBeInTheDocument();
    expect(screen.queryByRole('tablist')).toBeNull();
    expect(screen.queryByText('On this site')).toBeNull();
    expect(container.querySelector('figure, pre')).toBeNull();
  });
});
