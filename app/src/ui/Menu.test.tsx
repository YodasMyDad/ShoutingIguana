import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { commands } from '../keyboard/registry';
import { Button } from './Button';
import { ContextMenu, DropdownMenu, MenuCheckboxItem, MenuItem, MenuSeparator } from './Menu';

let unregister: (() => void) | undefined;
afterEach(() => unregister?.());

function RowMenu({ onCopy }: { onCopy: () => void }) {
  const [full, setFull] = useState(false);
  return (
    <>
      <MenuItem command="test.inspect">Inspect</MenuItem>
      <MenuItem shortcut="mod+c" onSelect={onCopy}>
        Copy the URL
      </MenuItem>
      <MenuItem command="test.export">Export this view</MenuItem>
      <MenuSeparator />
      <MenuCheckboxItem checked={full} onCheckedChange={setFull}>
        Show full URLs
      </MenuCheckboxItem>
    </>
  );
}

describe('menus', () => {
  it('open from the keyboard on the first item, list their keys, run what’s chosen and give focus back', async () => {
    const inspect = vi.fn();
    const copy = vi.fn();
    unregister = commands.register([
      { id: 'test.inspect', title: 'Inspect', group: 'View', keys: ['enter'], run: inspect },
      { id: 'test.export', title: 'Export this view', group: 'View', keys: ['mod+e'], enabled: () => false },
    ]);
    render(
      <DropdownMenu trigger={<Button>More</Button>}>
        <RowMenu onCopy={copy} />
      </DropdownMenu>,
    );
    const trigger = screen.getByRole('button', { name: 'More' });
    act(() => trigger.focus());
    await userEvent.keyboard('{Enter}');
    const menu = await screen.findByRole('menu');
    expect(menu).toBeVisible();
    const first = screen.getByRole('menuitem', { name: 'Inspect' });
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute('aria-keyshortcuts', 'Enter');
    expect(screen.getByRole('menuitem', { name: 'Copy the URL' })).toHaveAttribute('aria-keyshortcuts', 'Control+C');
    // A command that can't run now is listed, disabled.
    expect(screen.getByRole('menuitem', { name: 'Export this view' })).toHaveAttribute('aria-disabled', 'true');

    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect(copy).toHaveBeenCalledOnce();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(trigger).toHaveFocus();

    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard('{Enter}');
    expect(inspect).toHaveBeenCalledOnce();
  });

  it('check items stay open and flip', async () => {
    render(
      <DropdownMenu trigger={<Button>View</Button>}>
        <RowMenu onCopy={vi.fn()} />
      </DropdownMenu>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'View' }));
    const item = await screen.findByRole('menuitemcheckbox', { name: 'Show full URLs' });
    expect(item).toHaveAttribute('aria-checked', 'false');
    await userEvent.click(item);
    expect(screen.getByRole('menuitemcheckbox', { name: 'Show full URLs' })).toHaveAttribute('aria-checked', 'true');
  });

  it('closes with Esc', async () => {
    render(
      <DropdownMenu trigger={<Button>More</Button>}>
        <MenuItem>Inspect</MenuItem>
      </DropdownMenu>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'More' }));
    expect(await screen.findByRole('menu')).toBeVisible();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('open on a right-click, with the same items', async () => {
    const copy = vi.fn();
    render(
      <ContextMenu content={<MenuItem onSelect={copy}>Copy the URL</MenuItem>}>
        <div>/harpswell-metal-patio-set/</div>
      </ContextMenu>,
    );
    await userEvent.pointer({ keys: '[MouseRight]', target: screen.getByText('/harpswell-metal-patio-set/') });
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Copy the URL' }));
    expect(copy).toHaveBeenCalledOnce();
  });
});
