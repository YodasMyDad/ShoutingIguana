import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
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
    // The remove button is next, then the second chip.
    await userEvent.tab();
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Depth is at least 4' })).toHaveFocus();
    await userEvent.keyboard('{Backspace}');
    expect(onRemove).toHaveBeenCalledTimes(1);
    await userEvent.keyboard('{Delete}');
    expect(onRemove).toHaveBeenCalledTimes(2);
  });

  it('leaves focus on the next chip once one is removed, else the one before, else Add filter', async () => {
    function Chips() {
      const [chips, setChips] = useState(['4xx', '5xx', '3xx']);
      return (
        <div role="group" aria-label="Filters">
          {chips.map((chip) => (
            <FilterChip
              key={chip}
              field="Status"
              value={chip}
              onEdit={() => undefined}
              onRemove={() => setChips((all) => all.filter((other) => other !== chip))}
            />
          ))}
          <AddFilterChip />
        </div>
      );
    }
    render(<Chips />);
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Status is 4xx' })).toHaveFocus();
    await userEvent.keyboard('{Delete}');
    expect(screen.getByRole('button', { name: 'Status is 5xx' })).toHaveFocus();
    // The last chip's ✕: the one before it.
    await userEvent.click(screen.getByRole('button', { name: 'Remove the filter Status is 3xx' }));
    expect(screen.getByRole('button', { name: 'Status is 5xx' })).toHaveFocus();
    // Focus moved by the app opens no tooltip.
    expect(screen.queryByRole('tooltip')).toBeNull();
    await userEvent.keyboard('{Backspace}');
    expect(screen.getByRole('button', { name: 'Add filter' })).toHaveFocus();
  });

  it('describes its buttons by what their names lack: the chip by its hint, the ✕ by nothing', async () => {
    render(<FilterChip field="Status" value="4xx" onEdit={() => undefined} onRemove={() => undefined} />);
    const chip = screen.getByRole('button', { name: 'Status is 4xx' });
    act(() => chip.focus());
    await screen.findByRole('tooltip');
    expect(chip).toHaveAccessibleDescription('Edit the filter, Delete removes it');
    const remove = screen.getByRole('button', { name: 'Remove the filter Status is 4xx' });
    act(() => remove.focus());
    await screen.findByRole('tooltip');
    expect(remove).toHaveAccessibleDescription('');
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
