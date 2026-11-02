import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ProblemError, SidecarUnreachableError } from '../api/errors';
import { ErrorState } from './ErrorState';

describe('the error state', () => {
  it('offers Try again where trying again could work', () => {
    render(<ErrorState error={new SidecarUnreachableError(new TypeError('x'))} onRetry={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('never for a problem the engine meant: the same query would fail the same way', () => {
    const invalid = new ProblemError({ type: 'invalid-query', title: 'This view’s query can’t be run.', status: 422 });
    render(<ErrorState error={invalid} onRetry={vi.fn()} actions={<button type="button">Clear the filters</button>} />);
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Clear the filters' })).toBeVisible();
    expect(screen.getByText('This view’s query can’t be run.')).toBeVisible();
  });

  it('offers it for a failure that may have been passing, such as a view that broke while drawing', () => {
    render(<ErrorState error={new TypeError('x is undefined')} onRetry={vi.fn()} retryLabel="Reload" />);
    expect(screen.getByRole('button', { name: 'Reload' })).toBeVisible();
  });
});
