import * as TabsPrimitive from '@radix-ui/react-tabs';
import clsx from 'clsx';
import { ChevronDown } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { formatCount } from '../lib/format';
import { revealOffset } from '../lib/scroll';
import { Button } from './Button';
import { DropdownMenu, MenuItem } from './Menu';

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

/** Brings the chosen tab into view in its strip, scrolling the strip alone and only as far as it must. */
function revealChosen(list: HTMLElement) {
  const active = list.querySelector<HTMLElement>('[role="tab"][data-state="active"]');
  if (!active) return;
  const left = active.getBoundingClientRect().left - list.getBoundingClientRect().left + list.scrollLeft;
  const next = revealOffset(left, active.getBoundingClientRect().width, list.scrollLeft, list.clientWidth);
  if (next !== null) list.scrollLeft = next;
}

/**
 * Marks the strip's ends that have more tabs past them (`data-more-start`, `data-more-end`), which the strip's CSS
 * fades out, and says which tabs aren't wholly in view, by value: the overflow menu lists them.
 */
function markEnds(list: HTMLElement): string[] {
  const before = list.scrollLeft > 0.5;
  const after = list.scrollLeft + list.clientWidth < list.scrollWidth - 0.5;
  list.toggleAttribute('data-more-start', before);
  list.toggleAttribute('data-more-end', after);
  if (!before && !after) return [];
  const room = list.getBoundingClientRect();
  return [...list.querySelectorAll<HTMLElement>('[role="tab"]')]
    .filter((tab) => {
      const box = tab.getBoundingClientRect();
      return box.left < room.left - 0.5 || box.right > room.right + 0.5;
    })
    .map((tab) => tab.dataset.value ?? '');
}

const sameList = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((value, index) => value === b[index]);

/**
 * Tabs: the selected one has the accent underline (3:1). The arrow keys move between tabs and show each as they go;
 * Tab moves on, to the overflow button when there is one, then into the panel. A strip wider than its room scrolls
 * sideways without a bar (one under a strip of tabs read as a broken layout in the real shell): the arrow keys, a
 * touchpad, Shift and the wheel, and the plain wheel too, which the strip turns sideways. An end with more tabs past it
 * fades out, and a button at the strip's end says how many aren't in view ("5 more") and lists them (round 5's polish:
 * the inspector showed 3½ of its 9 tabs at 1280 px, and no sign of the rest). A tab chosen there is scrolled into view
 * and takes focus. The chosen tab is scrolled into view whenever it changes (a section remembered from the last URL, a
 * palette command), and whenever the tabs change size (counts arriving as the inspector walks from URL to URL), so
 * it's never chosen out of sight.
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
  const listRef = useRef<HTMLDivElement>(null);
  // Uncontrolled, the strip keeps its own choice, so a tab chosen from the overflow menu is chosen.
  const [own, setOwn] = useState<T | undefined>(defaultValue ?? tabs[0]?.value);
  const current = value ?? own;
  const choose = (next: T) => {
    if (value === undefined) setOwn(next);
    onValueChange?.(next);
  };
  // The tabs not wholly in view, and the one just chosen from the menu, which takes focus as the menu closes.
  const [outOfView, setOutOfView] = useState<readonly string[]>([]);
  const chosenFromMenu = useRef<string | null>(null);
  useLayoutEffect(() => {
    const list = listRef.current;
    if (!list) return;
    revealChosen(list);
    const next = markEnds(list);
    setOutOfView((previous) => (sameList(previous, next) ? previous : next));
  }, [current]);
  useEffect(() => {
    const list = listRef.current;
    if (!list) return;
    const mark = () => {
      const next = markEnds(list);
      setOutOfView((previous) => (sameList(previous, next) ? previous : next));
    };
    // The strip or a tab changing size (a count arriving, a narrower panel): the chosen tab back in view, the ends
    // marked again.
    const resized = new ResizeObserver(() => {
      revealChosen(list);
      mark();
    });
    resized.observe(list);
    for (const tab of list.children) resized.observe(tab);
    const onScroll = () => mark();
    // A plain wheel scrolls the strip sideways when it's wider than its room; a wheel that already goes sideways (a
    // touchpad, Shift) is left as it is, and a strip that fits lets the wheel scroll the page.
    const onWheel = (event: WheelEvent) => {
      if (list.scrollWidth <= list.clientWidth || event.deltaX !== 0 || event.deltaY === 0) return;
      event.preventDefault();
      list.scrollLeft += event.deltaMode === WheelEvent.DOM_DELTA_LINE ? event.deltaY * 16 : event.deltaY;
    };
    list.addEventListener('scroll', onScroll, { passive: true });
    list.addEventListener('wheel', onWheel, { passive: false });
    return () => {
      resized.disconnect();
      list.removeEventListener('scroll', onScroll);
      list.removeEventListener('wheel', onWheel);
    };
  }, [tabs.length]);
  const hidden = tabs.filter((tab) => outOfView.includes(tab.value));
  return (
    <TabsPrimitive.Root
      {...(current === undefined ? {} : { value: current })}
      onValueChange={(next) => {
        const tab = tabs.find((candidate) => candidate.value === next);
        if (tab) choose(tab.value);
      }}
      className={className}
    >
      <div className={clsx('si-tabs-bar', flush && 'si-tabs-bar--flush')}>
        <TabsPrimitive.List ref={listRef} aria-label={label} className={clsx('si-tabs', flush && 'si-tabs--flush')}>
          {tabs.map((tab) => (
            <TabsPrimitive.Trigger
              key={tab.value}
              value={tab.value}
              disabled={tab.disabled}
              data-value={tab.value}
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
