import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { SearchField } from './SearchField';
import { TextField } from './TextField';

describe('SearchField', () => {
  it('is a search box named by its label, clearing with its button or Esc while it has text', async () => {
    const onClear = vi.fn();
    function Search() {
      const [value, setValue] = useState('patio');
      return (
        <SearchField
          label="Search the library"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          onClear={() => {
            setValue('');
            onClear();
          }}
        />
      );
    }
    render(<Search />);
    const box = screen.getByRole('searchbox', { name: 'Search the library' });
    await userEvent.click(screen.getByRole('button', { name: 'Clear the search' }));
    expect(box).toHaveValue('');
    expect(box).toHaveFocus();
    expect(screen.queryByRole('button', { name: 'Clear the search' })).toBeNull();

    await userEvent.type(box, 'chair');
    await userEvent.keyboard('{Escape}');
    expect(box).toHaveValue('');
    expect(onClear).toHaveBeenCalledTimes(2);
  });

  it('leaves Esc to what’s around it when it’s empty, and keeps its clear button out of the tab order', async () => {
    const around = vi.fn();
    render(
      // A parent that closes something on Esc, as the inspector does.
      // eslint-disable-next-line jsx-a11y-x/no-static-element-interactions
      <div
        onKeyDown={(event) => {
          if (event.key === 'Escape') around();
        }}
      >
        <SearchField label="Search URLs" defaultValue="a" />
        <button type="button">Next</button>
      </div>,
    );
    const box = screen.getByRole('searchbox', { name: 'Search URLs' });
    box.focus();
    await userEvent.keyboard('{Escape}');
    expect(box).toHaveValue('');
    expect(around).not.toHaveBeenCalled();
    await userEvent.keyboard('{Escape}');
    expect(around).toHaveBeenCalledOnce();
    await userEvent.type(box, 'b');
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Next' })).toHaveFocus();
  });

  it('shows the key that focuses it while it’s empty', () => {
    render(<SearchField label="Search URLs" shortcut="mod+f" />);
    expect(screen.getByText('F')).toBeInTheDocument();
  });
});

describe('TextField', () => {
  it('says what’s wrong inline, read with the field', () => {
    render(<TextField label="Start URL" error="That isn’t a web address." description="Include https://." />);
    const field = screen.getByRole('textbox', { name: 'Start URL' });
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveAccessibleDescription('That isn’t a web address. Include https://.');
  });

  it('can be read-only or disabled', () => {
    const { rerender } = render(<TextField label="Data folder" defaultValue="C:\\data" readOnly />);
    expect(screen.getByRole('textbox', { name: 'Data folder' })).toHaveAttribute('readonly');
    rerender(<TextField label="Data folder" defaultValue="C:\\data" disabled />);
    const field = screen.getByRole('textbox', { name: 'Data folder' });
    expect(field).toBeDisabled();
    fireEvent.change(field, { target: { value: 'x' } });
  });
});
