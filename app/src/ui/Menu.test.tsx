import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { commands } from '../keyboard/registry';
import { Settings } from 'lucide-react';
import { Button } from './Button';
import { IconButton } from './IconButton';
import { ContextMenu, DropdownMenu, MenuCheckboxItem, MenuItem, MenuSeparator } from './Menu';
import { Popover } from './Popover';

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

  it('give focus back to an icon button without popping its tooltip, so the next Esc isn’t a tooltip’s', async () => {
    render(
      <DropdownMenu trigger={<IconButton label="View options" icon={Settings} />}>
        <MenuItem onSelect={() => undefined}>Show full URLs</MenuItem>
      </DropdownMenu>,
    );
    const trigger = screen.getByRole('button', { name: 'View options' });
    act(() => trigger.focus());
    // Focus from the keyboard shows the tooltip.
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    await userEvent.keyboard('{Enter}');
    expect(await screen.findByRole('menu')).toBeVisible();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
    expect(trigger).toHaveFocus();
    expect(screen.queryByRole('tooltip')).toBeNull();
    // A focus of the user's own shows it again.
    act(() => trigger.blur());
    act(() => trigger.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
  });

  it('and a popover, give focus back to its button without popping the button’s tooltip', async () => {
    render(
      <Popover trigger={<IconButton label="Columns" icon={Settings} />} title="Columns">
        <button type="button">Show all</button>
      </Popover>,
    );
    const trigger = screen.getByRole('button', { name: 'Columns' });
    act(() => trigger.focus());
    await userEvent.keyboard('{Enter}');
    expect(await screen.findByRole('dialog', { name: 'Columns' })).toBeVisible();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(trigger).toHaveFocus();
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  // A menu gives focus back as the palette does, from a timer after it has closed, but its item acts first, with
  // the menu still open: Radix sees focus leave the menu and gives none back.
  it('leave focus where the chosen item’s command put it, a dropdown’s or a right-click menu’s', async () => {
    unregister = commands.register([
      {
        id: 'test.find',
        title: 'Find',
        group: 'View',
        run: () => screen.getByRole('textbox', { name: 'Find' }).focus(),
      },
    ]);
    render(
      <>
        <input aria-label="Find" />
        <DropdownMenu trigger={<Button>More</Button>}>
          <MenuItem command="test.find">Find in the page</MenuItem>
        </DropdownMenu>
        <ContextMenu content={<MenuItem command="test.find">Find in the page</MenuItem>}>
          <div tabIndex={-1}>/harpswell-metal-patio-set/</div>
        </ContextMenu>
      </>,
    );
    const field = screen.getByRole('textbox', { name: 'Find' });
    const closed = async () => {
      expect(screen.queryByRole('menu')).toBeNull();
      // Past the timer a closed menu gives focus back from.
      await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    };

    act(() => screen.getByRole('button', { name: 'More' }).focus());
    await userEvent.keyboard('{Enter}');
    await screen.findByRole('menu');
    await userEvent.keyboard('{Enter}');
    await closed();
    expect(field).toHaveFocus();

    const row = screen.getByText('/harpswell-metal-patio-set/');
    act(() => row.focus());
    await userEvent.pointer({ keys: '[MouseRight]', target: row });
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Find in the page' }));
    await closed();
    expect(field).toHaveFocus();
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
