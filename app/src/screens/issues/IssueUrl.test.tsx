import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { IssueUrl } from './IssueUrl';

describe('a URL in an Issues table', () => {
  it('gives its table the keyboard when its link is the first thing clicked in it, so the arrows walk the rows', () => {
    const inspect = vi.fn();
    render(
      <div role="grid" tabIndex={0} aria-label="Rows">
        <IssueUrl url="https://example.com/old/" siteOrigin="https://example.com" onInspect={inspect} />
      </div>,
    );
    const grid = screen.getByRole('grid', { name: 'Rows' });
    expect(grid).not.toHaveFocus();
    fireEvent.click(screen.getByRole('button', { name: 'https://example.com/old/' }));
    expect(inspect).toHaveBeenCalledTimes(1);
    expect(grid).toHaveFocus();
  });

  it('leaves focus where it is in a table already in use', () => {
    render(
      <div role="grid" tabIndex={0} aria-label="Rows">
        <button type="button">A cell’s own control</button>
        <IssueUrl url="https://example.com/old/" siteOrigin="https://example.com" onInspect={() => undefined} />
      </div>,
    );
    const inside = screen.getByRole('button', { name: 'A cell’s own control' });
    inside.focus();
    fireEvent.click(screen.getByRole('button', { name: 'https://example.com/old/' }));
    expect(inside).toHaveFocus();
  });
});
