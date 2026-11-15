import * as TabsPrimitive from '@radix-ui/react-tabs';
import clsx from 'clsx';
import { ChevronDown } from 'lucide-react';
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { formatCount } from '../lib/format';
import { Button } from './Button';
import { DropdownMenu, MenuItem } from './Menu';
import { tabsShown } from './tabsFit';

export interface TabItem<T extends string> {
  value: T;
  label: string;
  /** A count in a pill after the label: Inlinks 12. */
  count?: number | null | undefined;
  disabled?: boolean | undefined;
  content: ReactNode;
}

export interface TabsProps<T extends string> {
  /** What the tabs switch between: "Inspector sections". */
  label: string;
  tabs: readonly TabItem<T>[];
  value?: T | undefined;
  defaultValue?: T | undefined;
  onValueChange?: ((value: T) => void) | undefined;
  /** No side padding on the list, for tabs inside a padded panel. */
  flush?: boolean | undefined;
  className?: string | undefined;
  panelClassName?: string | undefined;
}

/** The "N more" button's width before it has been drawn once: about what "7 more" takes. */
const MORE_GUESS = 96;

/** An element's width with its side margins: what it takes in its row. */
function outerWidth(element: Element): number {
  const style = getComputedStyle(element);
  return (
    element.getBoundingClientRect().width + (parseFloat(style.marginLeft) || 0) + (parseFloat(style.marginRight) || 0)
  );
}

/**
 * The tabs the strip leaves to its menu, by value: it measures its room, the tabs (those in the menu too: they're
 * laid out, unseen, after the others) and the button, and keeps the chosen tab in the strip.
 */
function measureHidden(bar: HTMLElement, list: HTMLElement, chosen: string | undefined, moreWidth: number): string[] {
  const tabs = [...list.querySelectorAll<HTMLElement>('[role="tab"]')];
  const listStyle = getComputedStyle(list);
  const padding = (parseFloat(listStyle.paddingLeft) || 0) + (parseFloat(listStyle.paddingRight) || 0);
  const gap = parseFloat(listStyle.columnGap) || 0;
  const barGap = parseFloat(getComputedStyle(bar).columnGap) || 0;
  const room = bar.clientWidth - padding;
  const shown = tabsShown(
    tabs.map(outerWidth),
    tabs.findIndex((tab) => tab.dataset.value === chosen),
    room,
    room - moreWidth - barGap,
    gap,
  );
  return tabs.filter((_, index) => !shown[index]).map((tab) => tab.dataset.value ?? '');
}

const sameList = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((value, index) => value === b[index]);

/**
 * Tabs: the selected one has the accent underline (3:1). A strip wider than its room shows the tabs that fit whole,
 * in order, the chosen one always among them, and ends in a button that says how many it leaves out ("5 more") and
 * lists exactly those: a tab is in the strip or in the menu, never both and never cut (polish 2: the inspector's
 * Inlinks tab showed half beside "7 more", which listed it too; round 5's polish had made it a scrolling strip that
 * cut tabs at its ends). A tab chosen from the menu takes its place in the strip, in place of the last that fitted, and
 * takes focus. The strip never scrolls. The arrow keys move between the tabs in the strip; Tab moves on to the button,
 * then into the panel. The strip measures again whenever it or a tab changes size (counts arriving as the inspector
 * walks from URL to URL, a narrower panel) and whenever the chosen tab changes (a section remembered from the last
 * URL, a palette command), so the chosen tab is never out of sight.
 */
export function Tabs<T extends string>({
  label,
  tabs,
  value,
  defaultValue,
  onValueChange,
  flush = false,
  className,
  panelClassName,
}: TabsProps<T>) {
  const barRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  // Uncontrolled, the strip keeps its own choice, so a tab chosen from the overflow menu is chosen.
  const [own, setOwn] = useState<T | undefined>(defaultValue ?? tabs[0]?.value);
  const current = value ?? own;
  const choose = (next: T) => {
    if (value === undefined) setOwn(next);
    onValueChange?.(next);
  };
  // The tabs left to the menu, and the one just chosen there, which takes focus as the menu closes.
  const [outOfStrip, setOutOfStrip] = useState<readonly string[]>([]);
  const chosenFromMenu = useRef<string | null>(null);
  const moreWidth = useRef<number | null>(null);
  const measure = useCallback(() => {
    const bar = barRef.current;
    const list = listRef.current;
    if (!bar || !list) return;
    const more = bar.querySelector('.si-tabs-bar__more');
    if (more) moreWidth.current = outerWidth(more);
    const next = measureHidden(bar, list, current, moreWidth.current ?? MORE_GUESS);
    setOutOfStrip((previous) => (sameList(previous, next) ? previous : next));
  }, [current]);
  // Before the strip is painted: when the chosen tab changes, the tabs change, or the button comes or goes (its own
  // width then known).
  const labels = tabs.map((tab) => `${tab.value} ${tab.label} ${tab.count ?? ''}`).join('|');
  useLayoutEffect(measure, [measure, labels, outOfStrip.length > 0]);
  useEffect(() => {
    const bar = barRef.current;
    const list = listRef.current;
    if (!bar || !list) return;
    const resized = new ResizeObserver(() => measure());
    resized.observe(bar);
    for (const tab of list.children) resized.observe(tab);
    return () => resized.disconnect();
  }, [measure, tabs.length]);
  // While its menu is open, the list it was opened on: counts arriving can move tabs in or out of the strip, and items
  // coming and going under the keyboard lost its place in the menu (seen in the release shell).
  const [listed, setListed] = useState<readonly string[] | null>(null);
  const hidden = tabs.filter((tab) => (listed ?? outOfStrip).includes(tab.value));
  return (
    <TabsPrimitive.Root
      {...(current === undefined ? {} : { value: current })}
      onValueChange={(next) => {
        const tab = tabs.find((candidate) => candidate.value === next);
        if (tab) choose(tab.value);
      }}
      className={className}
    >
      <div ref={barRef} className={clsx('si-tabs-bar', flush && 'si-tabs-bar--flush')}>
        <TabsPrimitive.List ref={listRef} aria-label={label} className={clsx('si-tabs', flush && 'si-tabs--flush')}>
          {tabs.map((tab) => (
            <TabsPrimitive.Trigger
              key={tab.value}
              value={tab.value}
              disabled={tab.disabled}
              data-value={tab.value}
              // Left to the menu: laid out after the others, unseen and out of the keyboard's way, so it's measured.
              data-overflow={outOfStrip.includes(tab.value) ? '' : undefined}
              className="si-tab"
            >
              {tab.label}
              {tab.count === null || tab.count === undefined ? null : (
                <>
                  {' '}
                  <span className="si-tab__count">{formatCount(tab.count)}</span>
                </>
              )}
            </TabsPrimitive.Trigger>
          ))}
        </TabsPrimitive.List>
        {hidden.length > 0 ? (
          <DropdownMenu
            align="end"
            onOpenChange={(open) => setListed(open ? outOfStrip : null)}
            closeFocus={() => {
              const chosen = chosenFromMenu.current;
              chosenFromMenu.current = null;
              if (chosen === null) return null;
              return (
                listRef.current?.querySelector<HTMLElement>(`[role="tab"][data-value="${CSS.escape(chosen)}"]`) ?? null
              );
            }}
            trigger={
              <Button
                size="sm"
                variant="ghost"
                icon={ChevronDown}
                aria-label={`${formatCount(hidden.length)} more: ${hidden.map((tab) => tab.label).join(', ')}`}
                className="si-tabs-bar__more"
              >
                {formatCount(hidden.length)} more
              </Button>
            }
          >
            {hidden.map((tab) => (
              <MenuItem
                key={tab.value}
                disabled={tab.disabled}
                detail={tab.count === null || tab.count === undefined ? undefined : formatCount(tab.count)}
                onSelect={() => {
                  chosenFromMenu.current = tab.value;
                  choose(tab.value);
                }}
              >
                {tab.label}
              </MenuItem>
            ))}
          </DropdownMenu>
        ) : null}
      </div>
      {tabs.map((tab) => (
        <TabsPrimitive.Content key={tab.value} value={tab.value} className={clsx('si-tab-panel', panelClassName)}>
          {tab.content}
        </TabsPrimitive.Content>
      ))}
    </TabsPrimitive.Root>
  );
}
