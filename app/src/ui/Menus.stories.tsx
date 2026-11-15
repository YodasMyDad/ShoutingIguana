import type { Meta, StoryObj } from '@storybook/react-vite';
import { Copy, Download, ExternalLink, Eye, Filter, FilterX, MoreHorizontal, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { commands } from '../keyboard/registry';
import { StoryFrame } from '../test/StoryFrame';
import { Button } from './Button';
import { ConfirmDialog, Dialog } from './Dialog';
import { IconButton } from './IconButton';
import {
  ContextMenu,
  DropdownMenu,
  MenuAt,
  MenuCheckboxItem,
  MenuItem,
  MenuLabel,
  MenuRadioGroup,
  MenuRadioItem,
  MenuSeparator,
} from './Menu';
import { Popover } from './Popover';
import { TextField } from './TextField';

const meta = {
  title: 'Design system/Menus and overlays',
  // Menus, popovers and dialogs render in a portal, outside the story's root.
  parameters: { pseudo: { rootSelector: 'body' } },
} satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

function useGridKeys() {
  useEffect(
    () =>
      commands.register([
        { id: 'story.inspect', title: 'Inspect', group: 'View', keys: ['enter'], run: () => undefined },
        { id: 'story.copyUrl', title: 'Copy the URL', group: 'View', keys: ['mod+c'], run: () => undefined },
        { id: 'story.export', title: 'Export this view', group: 'View', keys: ['mod+e'], enabled: () => false },
      ]),
    [],
  );
}

function RowMenuItems() {
  const [fullUrls, setFullUrls] = useState(true);
  const [sort, setSort] = useState('issues');
  return (
    <>
      <MenuItem icon={Eye} command="story.inspect">
        Inspect
      </MenuItem>
      <MenuItem icon={ExternalLink}>Open in the browser</MenuItem>
      <MenuItem icon={Copy} command="story.copyUrl">
        Copy the URL
      </MenuItem>
      <MenuSeparator />
      <MenuItem icon={Filter}>Filter by “404”</MenuItem>
      <MenuItem icon={FilterX}>Exclude “404”</MenuItem>
      <MenuItem icon={Download} command="story.export">
        Export this view
      </MenuItem>
      <MenuSeparator />
      <MenuCheckboxItem checked={fullUrls} onCheckedChange={setFullUrls}>
        Show full URLs
      </MenuCheckboxItem>
      <MenuLabel>Sort by</MenuLabel>
      <MenuRadioGroup value={sort} onValueChange={setSort}>
        <MenuRadioItem value="issues">Issues, most first</MenuRadioItem>
        <MenuRadioItem value="url">URL</MenuRadioItem>
      </MenuRadioGroup>
      <MenuSeparator />
      <MenuItem icon={Trash2} destructive>
        Delete crawl file…
      </MenuItem>
    </>
  );
}

/**
 * A dropdown menu, open: icons, shortcuts on the right from the registry, a disabled item (its command can't run),
 * checkable and radio items, a destructive one. The first item is highlighted, as the keyboard leaves it.
 */
export const DropdownOpen: Story = {
  // The keyboard's highlight on the third item: the neutral fill and the darker text, as the palette's.
  play: async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
    document.querySelectorAll<HTMLElement>('[role="menuitem"]')[2]?.focus();
  },
  render: function DropdownOpen() {
    useGridKeys();
    return (
      <StoryFrame>
        <div className="h-(--dialog-width)">
          <DropdownMenu open trigger={<IconButton label="More actions" icon={MoreHorizontal} variant="secondary" />}>
            <RowMenuItems />
          </DropdownMenu>
        </div>
      </StoryFrame>
    );
  },
};

/** The right-click menu: the same items, on a row. Right-click the row (or Shift+F10 on it). */
export const ContextMenuOnARow: Story = {
  play: ({ canvasElement }) => {
    const row = canvasElement.querySelector<HTMLElement>('[data-story-row]');
    const box = row?.getBoundingClientRect();
    row?.dispatchEvent(
      new MouseEvent('contextmenu', {
        bubbles: true,
        clientX: (box?.left ?? 0) + 120,
        clientY: (box?.top ?? 0) + 16,
      }),
    );
  },
  render: function ContextMenuOnARow() {
    useGridKeys();
    return (
      <StoryFrame>
        <div className="h-(--dialog-width)">
          <ContextMenu content={<RowMenuItems />}>
            <div data-story-row className="si-card flex h-(--control-height) items-center px-3 font-mono text-12">
              /harpswell-metal-patio-set/
            </div>
          </ContextMenu>
        </div>
      </StoryFrame>
    );
  },
};

/**
 * A menu at a point, opened by something other than a button of its own: a grid row's menu from Shift+F10 or the menu
 * key, at the corner of the cursor's cell. Esc closes it and the keyboard goes back to the grid.
 */
export const MenuAtAPoint: Story = {
  render: function MenuAtAPoint() {
    useGridKeys();
    const [open, setOpen] = useState(true);
    return (
      <StoryFrame>
        <div className="h-(--dialog-width)">
          <div className="si-card flex h-(--control-height) items-center px-3 font-mono text-12">
            /harpswell-metal-patio-set/
          </div>
          <MenuAt at={{ x: 48, y: 64 }} open={open} onOpenChange={setOpen} label="Menu for /harpswell-metal-patio-set/">
            <RowMenuItems />
          </MenuAt>
        </div>
      </StoryFrame>
    );
  },
};

/** A popover with a title and its close button: a filter's editor. */
export const PopoverOpen: Story = {
  render: () => (
    <StoryFrame>
      <div className="h-(--dialog-width)">
        <Popover open title="Status is" trigger={<Button>Status is 4xx</Button>}>
          <div className="flex flex-col gap-3">
            <TextField label="Status codes" defaultValue="4xx" mono description="A code, a class (4xx) or a range." />
            <div className="flex justify-end gap-2">
              <Button size="sm">Cancel</Button>
              <Button size="sm" variant="primary">
                Apply
              </Button>
            </div>
          </div>
        </Popover>
      </div>
    </StoryFrame>
  ),
};

/** A dialog with a body and actions. */
export const DialogOpen: Story = {
  render: () => (
    <Dialog
      open
      onOpenChange={() => undefined}
      title="Export this view"
      description="264 URLs with the 9 columns shown, as they're sorted and filtered now."
      footer={
        <>
          <Button>Cancel</Button>
          <Button variant="primary" icon={Download}>
            Export
          </Button>
        </>
      }
    >
      <TextField label="File name" defaultValue="gardenfurnituresets-urls-2026-11-01.xlsx" mono />
    </Dialog>
  ),
};

/** Confirming what can't be undone: it names what will be lost, Cancel has focus, the action is in its own words. */
export const ConfirmDestructive: Story = {
  render: () => (
    <ConfirmDialog
      open
      onOpenChange={() => undefined}
      title="Delete the crawl file of gardenfurnituresets.co.uk?"
      description="The file and its 4,521 URLs are deleted from the disk. This can’t be undone. To keep the file, remove the crawl from the library instead."
      confirmLabel="Delete crawl file"
      destructive
      onConfirm={() => undefined}
    />
  ),
};

/**
 * The action failed: the dialog stays open and says why in it, the plain cause first and the engine's words behind
 * Details; the button tries again.
 */
export const ConfirmFailed: Story = {
  play: async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
    [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
      .find((button) => button.textContent === 'Delete crawl file')
      ?.click();
  },
  render: () => (
    <ConfirmDialog
      open
      onOpenChange={() => undefined}
      title="Delete the crawl file of gardenfurnituresets.co.uk?"
      description="The file and its 4,521 URLs are deleted from the disk. This can’t be undone."
      confirmLabel="Delete crawl file"
      destructive
      errorTitle="The crawl file couldn’t be deleted."
      onConfirm={() =>
        Promise.reject(new Error('EBUSY: resource busy or locked, unlink gardenfurnituresets.co.uk.sicrawl'))
      }
    />
  ),
};
