import { ShortcutList } from '../../../keyboard/ShortcutSheet';
import { SectionPage } from '../SectionPage';

export const id = 'shortcuts';
export const title = 'Shortcuts';
export const order = 50;

/** Every shortcut, from the same registry the ? sheet, the menus and the tooltips read. */
export function Section() {
  return (
    <SectionPage
      title="Shortcuts"
      purpose="Every shortcut that works here, by where it works. Press ? on any screen for the same list."
    >
      <div className="si-card">
        <ShortcutList />
      </div>
    </SectionPage>
  );
}
