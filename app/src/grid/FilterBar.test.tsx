import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { GridFilter } from '../api/grids';
import { FilterBar } from './FilterBar';
import type { FilterColumn } from './filterEditing';
import type { FilterWords } from './filterSentence';

const COLUMNS: FilterColumn[] = [
  { id: 'statusCode', name: 'Status', group: 'url', type: 'number', values: [] },
  { id: 'title', name: 'Title', group: 'url', type: 'text', values: [] },
];
const EDITOR = { columns: COLUMNS, groups: [{ id: 'url', title: 'The URL' }] };
const WORDS: FilterWords = { column: (id) => COLUMNS.find((column) => column.id === id)?.name ?? id };

const AT_LEAST_400: GridFilter = { column: 'statusCode', op: 'gte', value: 400 };
const TITLE_HAS_SALE: GridFilter = { column: 'title', op: 'contains', value: 'sale' };

describe('the filter bar, keeping its own editors', () => {
  it('opens a chip’s editor on its value, and closes it when another filter takes the chip’s place', async () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <FilterBar filters={[AT_LEAST_400]} onChange={onChange} words={WORDS} editor={EDITOR} />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Status is at least 400' }));
    const editing = await screen.findByRole('dialog', { name: 'Change the filter' });
    await waitFor(() => expect(within(editing).getByRole('textbox', { name: 'Number' })).toHaveFocus());
    // The same filter drawn again (a count moved, a column was chosen): its editor stays open.
    rerender(<FilterBar filters={[{ ...AT_LEAST_400 }]} onChange={onChange} words={WORDS} editor={EDITOR} />);
    expect(screen.getByRole('dialog', { name: 'Change the filter' })).toBeVisible();
    // The view changed under it (back, forward, a link): the first place is another filter's now, and nobody asked
    // for that one's editor.
    rerender(<FilterBar filters={[TITLE_HAS_SALE]} onChange={onChange} words={WORDS} editor={EDITOR} />);
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.getByRole('button', { name: 'Title contains sale' })).toBeVisible();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('says why a view takes no more filters, in place of the editor, and still edits the ones it has', async () => {
    const onChange = vi.fn();
    render(
      <FilterBar
        filters={[AT_LEAST_400, TITLE_HAS_SALE]}
        onChange={onChange}
        words={WORDS}
        editor={EDITOR}
        maxFilters={2}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Add filter' }));
    const adding = await screen.findByRole('dialog', { name: 'Add a filter' });
    expect(within(adding).getByText('A view takes at most 2 filters. Remove one to add another.')).toBeVisible();
    expect(within(adding).queryByRole('textbox')).toBeNull();
    await userEvent.keyboard('{Escape}');
    await userEvent.click(screen.getByRole('button', { name: 'Title contains sale' }));
    const text = within(await screen.findByRole('dialog', { name: 'Change the filter' })).getByRole('textbox', {
      name: 'Text',
    });
    await userEvent.clear(text);
    await userEvent.type(text, 'offer{Enter}');
    expect(onChange).toHaveBeenCalledWith([AT_LEAST_400, { column: 'title', op: 'contains', value: 'offer' }]);
  });

  it('shows what the engine said of the filter it refused, under its value, until the value is changed', async () => {
    const refused: GridFilter = { column: 'title', op: 'regex', value: '(?=sale)now' };
    const sentence = 'This pattern needs backtracking, which isn’t supported: rewrite it without.';
    function Bar() {
      return (
        <FilterBar
          filters={[AT_LEAST_400, refused]}
          onChange={() => undefined}
          words={WORDS}
          editor={EDITOR}
          editing={1}
          refused={{ index: 1, message: sentence }}
        />
      );
    }
    render(<Bar />);
    const editing = await screen.findByRole('dialog', { name: 'Change the filter' });
    const pattern = within(editing).getByRole('textbox', { name: 'Pattern' });
    expect(pattern).toBeInvalid();
    expect(pattern).toHaveAccessibleDescription(expect.stringContaining(sentence));
    await userEvent.type(pattern, 'x');
    expect(within(editing).queryByText(sentence)).toBeNull();
    expect(pattern).not.toBeInvalid();
  });
});
