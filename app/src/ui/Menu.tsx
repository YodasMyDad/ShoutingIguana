import * as ContextPrimitive from '@radix-ui/react-context-menu';
import * as DropdownPrimitive from '@radix-ui/react-dropdown-menu';
import clsx from 'clsx';
import { Check } from 'lucide-react';
import { createContext, useContext, type ReactElement, type ReactNode } from 'react';
import { ariaKeyShortcut, parseChord } from '../keyboard/keys';
import { commands, useChord, useRegistryVersion } from '../keyboard/registry';
import { isMac } from '../lib/platform';
import { Icon, IconSlot, type IconLike } from './Icon';
import { Kbd } from './Kbd';

/** The gap between a menu and its trigger, and the room it keeps from the window's edges, in px. */
const MENU_OFFSET = 4;
const COLLISION_PADDING = 8;

// Dropdown and context menus share their items: each item draws itself with the primitives of the menu it's in.
type MenuKind = 'dropdown' | 'context';
const KindContext = createContext<MenuKind>('dropdown');
const useParts = (): typeof DropdownPrimitive =>
  useContext(KindContext) === 'context' ? (ContextPrimitive as unknown as typeof DropdownPrimitive) : DropdownPrimitive;

export interface DropdownMenuProps {
  /** The button that opens it: a `Button` or an `IconButton`. */
  trigger: ReactElement;
  children: ReactNode;
  align?: 'start' | 'center' | 'end' | undefined;
  side?: 'top' | 'right' | 'bottom' | 'left' | undefined;
  open?: boolean | undefined;
  onOpenChange?: ((open: boolean) => void) | undefined;
  className?: string | undefined;
}

/**
 * A menu from a button: Enter, Space or the down arrow opens it on its first item, the arrows and typing move, Esc
 * closes it and gives focus back to the button. Items list their shortcuts on the right, from the registry.
 */
export function DropdownMenu({
  trigger,
  children,
  align = 'start',
  side = 'bottom',
  open,
  onOpenChange,
  className,
}: DropdownMenuProps) {
  return (
    // Not modal: the page isn't hidden from screen readers or locked while a menu is open; Esc, a click outside or a
    // choice closes it, and focus goes back to its button.
    <DropdownPrimitive.Root
      modal={false}
      {...(open === undefined ? {} : { open })}
      {...(onOpenChange ? { onOpenChange } : {})}
    >
      <DropdownPrimitive.Trigger asChild>{trigger}</DropdownPrimitive.Trigger>
      <DropdownPrimitive.Portal>
        <DropdownPrimitive.Content
          align={align}
          side={side}
          sideOffset={MENU_OFFSET}
          collisionPadding={COLLISION_PADDING}
          loop
          className={clsx('si-menu', className)}
        >
          <KindContext.Provider value="dropdown">{children}</KindContext.Provider>
        </DropdownPrimitive.Content>
      </DropdownPrimitive.Portal>
    </DropdownPrimitive.Root>
  );
}

export interface ContextMenuProps {
  /** What's right-clicked (or Shift+F10, or the menu key, on it): one element. */
  children: ReactElement;
  /** The menu's items. */
  content: ReactNode;
  onOpenChange?: ((open: boolean) => void) | undefined;
  disabled?: boolean | undefined;
  className?: string | undefined;
}

/** A right-click menu, with the same items as a dropdown: the grid's inspect, open, copy, filter by this value. */
export function ContextMenu({ children, content, onOpenChange, disabled = false, className }: ContextMenuProps) {
  return (
    <ContextPrimitive.Root modal={false} {...(onOpenChange ? { onOpenChange } : {})}>
      <ContextPrimitive.Trigger asChild disabled={disabled}>
        {children}
      </ContextPrimitive.Trigger>
      <ContextPrimitive.Portal>
        <ContextPrimitive.Content collisionPadding={COLLISION_PADDING} loop className={clsx('si-menu', className)}>
          <KindContext.Provider value="context">{content}</KindContext.Provider>
        </ContextPrimitive.Content>
      </ContextPrimitive.Portal>
    </ContextPrimitive.Root>
  );
}

interface ItemCommon {
  children: ReactNode;
  icon?: IconLike | undefined;
  /** A chord to show on the right (`mod+c`); `command` gives the registry's instead. */
  shortcut?: string | undefined;
  disabled?: boolean | undefined;
}

/** The keys on the right of an item: shown as keycaps, given to screen readers as `aria-keyshortcuts`. */
function useKeys(shortcut: string | undefined, command: string | undefined) {
  const chord = useChord(command ?? '');
  const keys = shortcut ?? chord;
  return {
    keys,
    aria: keys ? ariaKeyShortcut(parseChord(keys), isMac) : undefined,
  };
}

function ItemEnd({ keys }: { keys: string | null | undefined }) {
  return keys ? (
    <span className="si-menu-item__end" aria-hidden="true">
      <Kbd chord={keys} size="sm" />
    </span>
  ) : null;
}

export interface MenuItemProps extends ItemCommon {
  /** Runs this registry command when chosen: its title stays the caller's, its keys and whether it can run, the registry's. */
  command?: string | undefined;
  onSelect?: (() => void) | undefined;
  /** Loses data ("Delete crawl file…"): in the destructive colour. */
  destructive?: boolean | undefined;
}

export function MenuItem({
  children,
  icon,
  shortcut,
  disabled,
  command,
  onSelect,
  destructive = false,
}: MenuItemProps) {
  const Parts = useParts();
  useRegistryVersion();
  const { keys, aria } = useKeys(shortcut, command);
  const registered = command ? commands.get(command) : undefined;
  const unavailable = registered ? !commands.isEnabled(registered) : false;
  return (
    <Parts.Item
      disabled={disabled === true || unavailable}
      aria-keyshortcuts={aria}
      className={clsx('si-menu-item', destructive && 'si-menu-item--danger')}
      onSelect={() => {
        onSelect?.();
        if (command) commands.run(command);
      }}
    >
      <span className="si-menu-item__icon">
        <IconSlot icon={icon} size="md" />
      </span>
      <span className="si-menu-item__label">{children}</span>
      <ItemEnd keys={keys} />
    </Parts.Item>
  );
}

export interface MenuCheckboxItemProps extends ItemCommon {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
}

/** An item that's on or off ("Show full URLs"): a tick where the icon goes while it's on. */
export function MenuCheckboxItem({ children, shortcut, disabled, checked, onCheckedChange }: MenuCheckboxItemProps) {
  const Parts = useParts();
  const { keys, aria } = useKeys(shortcut, undefined);
  return (
    <Parts.CheckboxItem
      checked={checked}
      onCheckedChange={onCheckedChange}
      disabled={disabled}
      aria-keyshortcuts={aria}
      className="si-menu-item"
      onSelect={(event) => event.preventDefault()}
    >
      <span className="si-menu-item__icon si-menu-item__check">
        <Parts.ItemIndicator>
          <Icon icon={Check} size="md" />
        </Parts.ItemIndicator>
      </span>
      <span className="si-menu-item__label">{children}</span>
      <ItemEnd keys={keys} />
    </Parts.CheckboxItem>
  );
}

export function MenuRadioGroup({
  value,
  onValueChange,
  children,
}: {
  value: string;
  onValueChange: (value: string) => void;
  children: ReactNode;
}) {
  const Parts = useParts();
  return (
    <Parts.RadioGroup value={value} onValueChange={onValueChange}>
      {children}
    </Parts.RadioGroup>
  );
}

/** One of a few ("Newest first"): a tick where the icon goes on the chosen one. */
export function MenuRadioItem({ value, children, shortcut, disabled }: ItemCommon & { value: string }) {
  const Parts = useParts();
  const { keys, aria } = useKeys(shortcut, undefined);
  return (
    <Parts.RadioItem value={value} disabled={disabled} aria-keyshortcuts={aria} className="si-menu-item">
      <span className="si-menu-item__icon si-menu-item__check">
        <Parts.ItemIndicator>
          <Icon icon={Check} size="md" />
        </Parts.ItemIndicator>
      </span>
      <span className="si-menu-item__label">{children}</span>
      <ItemEnd keys={keys} />
    </Parts.RadioItem>
  );
}

export function MenuSeparator() {
  const Parts = useParts();
  return <Parts.Separator className="si-menu-separator" />;
}

/** A heading over a group of items. Not focusable. */
export function MenuLabel({ children }: { children: ReactNode }) {
  const Parts = useParts();
  return <Parts.Label className="si-menu-label">{children}</Parts.Label>;
}

export function MenuGroup({ children }: { children: ReactNode }) {
  const Parts = useParts();
  return <Parts.Group>{children}</Parts.Group>;
}
