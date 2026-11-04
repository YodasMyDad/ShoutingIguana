import * as TooltipPrimitive from '@radix-ui/react-tooltip';
import { createContext, useContext, type ReactElement, type ReactNode } from 'react';
import { formatChord, parseChord } from '../keyboard/keys';
import { releaseKey } from '../keyboard/layers';
import { useChord } from '../keyboard/registry';
import { isMac } from '../lib/platform';
import { Kbd } from './Kbd';

/** How long the pointer rests before a tooltip shows. Moving to the next one within the skip delay shows it at once. */
export const TOOLTIP_DELAY_MS = 500;
const TOOLTIP_SKIP_DELAY_MS = 300;
/** The gap between a tooltip and its trigger, in px (a 6 px step of the spacing scale). */
const TOOLTIP_OFFSET = 6;
const COLLISION_PADDING = 8;

const InProvider = createContext(false);

/**
 * Esc on an open tooltip closes it, and Radix takes the key for that. With nothing but the app under it, the key is
 * given back, so the same Esc does the app's Esc too (closes the inspector). Over a menu, a popover or a dialog it
 * isn't: those close innermost first, one Esc at a time.
 */
function giveEscapeBack(event: KeyboardEvent) {
  if (document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"]') === null) releaseKey(event);
}

/** One provider for the app, so moving from one tooltip to the next skips the delay. */
export function TooltipProvider({ children }: { children: ReactNode }) {
  return (
    <InProvider.Provider value>
      <TooltipPrimitive.Provider delayDuration={TOOLTIP_DELAY_MS} skipDelayDuration={TOOLTIP_SKIP_DELAY_MS}>
        {children}
      </TooltipPrimitive.Provider>
    </InProvider.Provider>
  );
}

export interface TooltipProps {
  /** What the control is or does, in a few words. Nothing: no tooltip, just the child. */
  content: ReactNode;
  /** A quieter second part: "Click to copy". */
  hint?: ReactNode | undefined;
  /** The command the control runs: its shortcut shows as keycaps, from the registry, so it can't be wrong. */
  command?: string | undefined;
  /** A chord to show when there's no command (`mod+c`). */
  chord?: string | undefined;
  side?: 'top' | 'right' | 'bottom' | 'left' | undefined;
  align?: 'start' | 'center' | 'end' | undefined;
  /** Controlled: "Copied" stays up after a click. */
  open?: boolean | undefined;
  onOpenChange?: ((open: boolean) => void) | undefined;
  /** One focusable element (a button, a link) or an element the pointer can rest on. */
  children: ReactElement;
}

/**
 * A tooltip: after a short rest of the pointer, or at once on keyboard focus, and gone with Esc (which still does the
 * app's Esc) or a click. Its text is the trigger's description for screen readers while it shows; the trigger's name
 * stays its own. Shortcuts come from the command registry, as `Button`'s `command` does.
 */
export function Tooltip({
  content,
  hint,
  command,
  chord,
  side = 'bottom',
  align = 'center',
  open,
  onOpenChange,
  children,
}: TooltipProps) {
  const commandChord = useChord(command ?? '');
  const inProvider = useContext(InProvider);
  const keys = chord ?? commandChord;
  if (content === undefined || content === null || content === '' || content === false) return children;

  const spoken =
    typeof content === 'string'
      ? [content, typeof hint === 'string' ? hint : null, keys ? formatChord(parseChord(keys), isMac) : null]
          .filter(Boolean)
          .join(', ')
      : undefined;
  const tooltip = (
    <TooltipPrimitive.Root {...(open === undefined ? {} : { open })} {...(onOpenChange ? { onOpenChange } : {})}>
      <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content
          side={side}
          align={align}
          sideOffset={TOOLTIP_OFFSET}
          collisionPadding={COLLISION_PADDING}
          className="si-tooltip"
          onEscapeKeyDown={giveEscapeBack}
          {...(spoken ? { 'aria-label': spoken } : {})}
        >
          <span className="si-tooltip__text">
            {content}
            {hint ? <span className="si-tooltip__hint"> {hint}</span> : null}
          </span>
          {keys ? <Kbd chord={keys} size="sm" /> : null}
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  );
  return inProvider ? tooltip : <TooltipProvider>{tooltip}</TooltipProvider>;
}
