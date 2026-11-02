import * as Dialog from '@radix-ui/react-dialog';
import clsx from 'clsx';
import { restoreFocus, setOverlay, useOverlay } from '../app/overlays';
import { Kbd } from '../ui/Kbd';
import { COMMAND_GROUPS, commands, useRegistryVersion } from './registry';

/** The `?` sheet: every shortcut in the registry, so it can never list a key that does nothing. */
export function ShortcutSheet() {
  const open = useOverlay() === 'shortcuts';
  return (
    <Dialog.Root open={open} onOpenChange={(next) => setOverlay(next ? 'shortcuts' : null)}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <Dialog.Content
          // Opened with `?`, there's no trigger for Radix to give focus back to: put it where it was.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocus();
          }}
          className="fixed top-1/2 left-1/2 z-40 flex max-h-(--dialog-max-height) w-(--palette-width) max-w-full -translate-1/2 flex-col rounded-xl border border-border bg-surface-overlay shadow-4"
        >
          <div className="flex items-center border-b border-border-subtle px-5 py-3">
            <Dialog.Title className="text-16 font-semibold">Keyboard shortcuts</Dialog.Title>
            <Dialog.Close className="ml-auto rounded-sm px-2 text-text-muted hover:bg-surface-hover" aria-label="Close">
              ✕
            </Dialog.Close>
          </div>
          <Dialog.Description className="sr-only">Every shortcut in the app, by where it works.</Dialog.Description>
          <ShortcutList scrollable />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/**
 * The list itself, also shown in Settings > Shortcuts. In the sheet it scrolls, so it's a named region that takes
 * focus: keyboard users can scroll it with the arrows.
 */
export function ShortcutList({ scrollable = false }: { scrollable?: boolean }) {
  useRegistryVersion();
  const listed = commands.list().filter((command) => (command.keys?.length ?? 0) > 0);
  return (
    <div
      {...(scrollable ? { role: 'region', 'aria-label': 'Shortcuts', tabIndex: 0 } : {})}
      className={clsx('grid gap-6 p-5', scrollable && 'overflow-auto')}
    >
      {COMMAND_GROUPS.map((group) => {
        const inGroup = listed.filter((command) => command.group === group);
        return inGroup.length === 0 ? null : (
          <section key={group} aria-labelledby={`shortcuts-${group}`}>
            <h3 id={`shortcuts-${group}`} className="pb-2 text-12 font-medium text-text-muted">
              {group}
            </h3>
            <dl className="flex flex-col gap-1.5">
              {inGroup.map((command) => (
                <div key={command.id} className="flex items-center gap-3 text-13">
                  <dt className="text-text-secondary">{command.title}</dt>
                  <dd className="ml-auto flex items-center gap-1.5">
                    {(command.keys ?? []).map((key, index) => (
                      <span key={key} className="flex items-center gap-1.5">
                        {index > 0 ? <span className="text-12 text-text-muted">or</span> : null}
                        <Kbd chord={key} />
                      </span>
                    ))}
                  </dd>
                </div>
              ))}
            </dl>
          </section>
        );
      })}
    </div>
  );
}
