import * as TabsPrimitive from '@radix-ui/react-tabs';
import clsx from 'clsx';
import { useLayoutEffect, useRef, type ReactNode } from 'react';
import { formatCount } from '../lib/format';
import { revealOffset } from '../lib/scroll';

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

/**
 * Tabs: the selected one has the accent underline (3:1). The arrow keys move between tabs and show each as they go;
 * Tab moves into the panel. A strip wider than its room scrolls sideways without a bar, and the chosen tab is
 * scrolled into view whenever it changes (a section remembered from the last URL, a palette command), so it's never
 * chosen out of sight.
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
  useLayoutEffect(() => {
    const list = listRef.current;
    const active = list?.querySelector<HTMLElement>('[role="tab"][data-state="active"]');
    if (!list || !active) return;
    const left = active.getBoundingClientRect().left - list.getBoundingClientRect().left + list.scrollLeft;
    const next = revealOffset(left, active.getBoundingClientRect().width, list.scrollLeft, list.clientWidth);
    if (next !== null) list.scrollLeft = next;
  }, [value]);
  return (
    <TabsPrimitive.Root
      {...(value === undefined ? {} : { value })}
      defaultValue={defaultValue ?? tabs[0]?.value}
      onValueChange={(next) => {
        const tab = tabs.find((candidate) => candidate.value === next);
        if (tab) onValueChange?.(tab.value);
      }}
      className={className}
    >
      <TabsPrimitive.List ref={listRef} aria-label={label} className={clsx('si-tabs', flush && 'si-tabs--flush')}>
        {tabs.map((tab) => (
          <TabsPrimitive.Trigger key={tab.value} value={tab.value} disabled={tab.disabled} className="si-tab">
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
      {tabs.map((tab) => (
        <TabsPrimitive.Content key={tab.value} value={tab.value} className={clsx('si-tab-panel', panelClassName)}>
          {tab.content}
        </TabsPrimitive.Content>
      ))}
    </TabsPrimitive.Root>
  );
}
