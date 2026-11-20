import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AnchorHTMLAttributes } from 'react';
import { PlainCheckRow } from './PlainCheckRow';
import { SeverityWord } from './SeverityWord';
import { outcomeWordIn, plainOutcomeWord, plainSeverityCount } from './severity';

/** A link whose words the row puts inside it. */
function Anchor({ children, ...props }: AnchorHTMLAttributes<HTMLAnchorElement>) {
  return <a {...props}>{children}</a>;
}

describe('the severity word', () => {
  it('says Simple’s words, one to one with Advanced’s', () => {
    expect(
      ['error', 'warning', 'notice', 'skipped', 'passed'].map((outcome) => plainOutcomeWord(outcome as never)),
    ).toEqual(['Serious', 'Worth fixing', 'Worth a look', 'Couldn’t check', 'Nothing wrong']);
    expect(outcomeWordIn('warning', 'advanced')).toBe('Warning');
    expect(outcomeWordIn('warning', 'simple')).toBe('Worth fixing');
  });

  it('counts problems in Simple’s words, never issues', () => {
    expect(plainSeverityCount('error', 2)).toBe('2 serious problems');
    expect(plainSeverityCount('error', 1)).toBe('1 serious problem');
    expect(plainSeverityCount('warning', 4)).toBe('4 problems worth fixing');
    expect(plainSeverityCount('warning', 1)).toBe('1 problem worth fixing');
    expect(plainSeverityCount('notice', 13)).toBe('13 things worth a look');
    expect(plainSeverityCount('notice', 1)).toBe('1 thing worth a look');
    expect(plainSeverityCount('notice', 12_345)).toBe('12,345 things worth a look');
  });

  it('names a mark alone by its Simple word, and hides it beside the word', () => {
    render(
      <>
        <SeverityWord severity="error" variant="mark" />
        <SeverityWord severity="warning" />
      </>,
    );
    expect(screen.getByRole('img', { name: 'Serious' })).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Warning' })).toBeNull();
    expect(screen.getByText('Worth fixing')).toBeInTheDocument();
  });
});

describe('the plain check row', () => {
  it('is one link whose name reads as a sentence: place, word, title, tag, summary, count', () => {
    render(
      <PlainCheckRow
        asChild
        number={2}
        outcome="notice"
        oneFix
        title="Links with nothing to read"
        summary="Some links have no words."
        count={
          <>
            <b>265</b> of 265 pages
          </>
        }
      >
        <Anchor href="#problem" />
      </PlainCheckRow>,
    );
    expect(screen.getByRole('link')).toHaveAccessibleName(
      // The tag read "One fix" (the UX audit of 2026-11-19, section 5: it reads as a count).
      '2 Worth a look Links with nothing to read Fix once Some links have no words. 265 of 265 pages',
    );
  });

  it('is a plain row, with no chevron, without a link', () => {
    const { container } = render(<PlainCheckRow outcome="passed" title="Every page has a title" />);
    expect(screen.queryByRole('link')).toBeNull();
    expect(container.querySelector('.si-plain-row__chevron')).toBeNull();
    expect(screen.getByRole('img', { name: 'Nothing wrong' })).toBeInTheDocument();
  });
});
