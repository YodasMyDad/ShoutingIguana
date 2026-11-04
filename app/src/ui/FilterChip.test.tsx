import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AddFilterChip, FilterChip } from './FilterChip';
import { StatusCode } from './StatusCode';

describe('FilterChip', () => {
  it('reads as its sentence, opens its editor, and goes with its ✕', async () => {
    const onEdit = vi.fn();
    const onRemove = vi.fn();
    render(<FilterChip field="Status" value="4xx" onEdit={onEdit} onRemove={onRemove} />);
    await userEvent.click(screen.getByRole('button', { name: 'Status is 4xx' }));
    expect(onEdit).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'Remove the filter Status is 4xx' }));
    expect(onRemove).toHaveBeenCalledOnce();
  });

  it('is reached with Tab and removed with Delete or Backspace', async () => {
    const onRemove = vi.fn();
    render(
      <>
        <FilterChip field="Indexable" value="yes" onEdit={() => undefined} onRemove={onRemove} />
        <FilterChip field="Depth" op="is at least" value="4" onEdit={() => undefined} onRemove={onRemove} />
      </>,
    );
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Indexable is yes' })).toHaveFocus();
    await userEvent.keyboard('{Delete}');
    expect(onRemove).toHaveBeenCalledTimes(1);
    // The remove button is next, then the second chip.
    await userEvent.tab();
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Depth is at least 4' })).toHaveFocus();
    await userEvent.keyboard('{Backspace}');
    expect(onRemove).toHaveBeenCalledTimes(2);
  });

  it('names a value drawn with more than text by its words', () => {
    render(
      <FilterChip
        field="Status"
        value={<StatusCode code={404} />}
        valueText="404"
        onEdit={() => undefined}
        onRemove={() => undefined}
      />,
    );
    expect(screen.getByRole('button', { name: 'Status is 404' })).toBeInTheDocument();
  });

  it('leaves a preview chip out of the tab order, and unremovable', () => {
    render(
      <FilterChip field="Check" value="Internal links without anchor text" variant="preview" onRemove={vi.fn()} />,
    );
    expect(screen.queryByRole('button')).toBeNull();
    expect(screen.getByText('Internal links without anchor text')).toBeVisible();
  });

  it('without an editor, is text with its ✕', () => {
    const onRemove = vi.fn();
    render(<FilterChip field="Linked URL" value="/" onRemove={onRemove} />);
    expect(screen.getAllByRole('button')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Remove the filter Linked URL is /' }));
    expect(onRemove).toHaveBeenCalledOnce();
  });
});

describe('AddFilterChip', () => {
  it('is a button that says what it does', async () => {
    const onClick = vi.fn();
    render(<AddFilterChip onClick={onClick} />);
    await userEvent.click(screen.getByRole('button', { name: 'Add filter' }));
    expect(onClick).toHaveBeenCalledOnce();
  });
});
