import clsx from 'clsx';
import { restoreFocus, setOverlay, useOverlay } from '../app/overlays';
import { Dialog } from '../ui/Dialog';
import { Kbd } from '../ui/Kbd';
import { COMMAND_GROUPS, commands, useRegistryVersion } from './registry';

/** The `?` sheet: every shortcut in the registry, so it can never list a key that does nothing. */
export function ShortcutSheet() {
  const open = useOverlay() === 'shortcuts';
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => setOverlay(next ? 'shortcuts' : null)}
      title="Keyboard shortcuts"
      description="Every shortcut in the app, by where it works."
      size="wide"
      flush
      // Opened with `?`, there's no trigger for Radix to give focus back to: put it where it was.
      onCloseAutoFocus={(event) => {
        event.preventDefault();
        restoreFocus();
      }}
    >
      <ShortcutList scrollable />
    </Dialog>
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
