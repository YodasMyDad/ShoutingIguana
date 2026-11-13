// The URL grid's saved views: a menu that opens one, saves the view as it is, and renames or deletes the one the grid
// is on (with Undo), and the dialog that names one. The views are preferences (`settings.savedViews`), kept with the
// app's other preferences; opening one puts its filters, search, sort and columns in the route.
import { Bookmark, BookmarkPlus, PencilLine, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { MAX_VIEW_NAME, viewNameProblem, type SavedView } from '../../grid/savedViews';
import { Button } from '../../ui/Button';
import { Dialog } from '../../ui/Dialog';
import { DropdownMenu, MenuItem, MenuLabel, MenuRadioGroup, MenuRadioItem, MenuSeparator } from '../../ui/Menu';
import { TextField } from '../../ui/TextField';

export interface SavedViewsMenuProps {
  views: readonly SavedView[];
  current: SavedView | null;
  /** Why the view can't be saved, when it can't. */
  problem: string | null;
  onOpen: (view: SavedView) => void;
  onSave: () => void;
  onRename: (view: SavedView) => void;
  onDelete: (view: SavedView) => void;
}

/**
 * The menu: the saved views (the one the grid is on ticked), then save this view, and rename or delete the one the
 * grid is on. Its button names that view, so what's on screen says which it is.
 */
export function SavedViewsMenu({ views, current, problem, onOpen, onSave, onRename, onDelete }: SavedViewsMenuProps) {
  return (
    <DropdownMenu
      align="end"
      trigger={
        <Button variant="ghost" size="sm" icon={Bookmark}>
          {current ? <span className="max-w-(--search-width-filter) truncate">{current.name}</span> : 'Saved views'}
        </Button>
      }
    >
      {views.length === 0 ? (
        <MenuLabel>No saved views yet</MenuLabel>
      ) : (
        <>
          <MenuLabel>Saved views</MenuLabel>
          <MenuRadioGroup
            value={current?.id ?? ''}
            onValueChange={(id) => {
              const view = views.find((candidate) => candidate.id === id);
              if (view) onOpen(view);
            }}
          >
            {views.map((view) => (
              <MenuRadioItem key={view.id} value={view.id}>
                {view.name}
              </MenuRadioItem>
            ))}
          </MenuRadioGroup>
        </>
      )}
      <MenuSeparator />
      {current ? null : (
        <MenuItem icon={BookmarkPlus} command="urls.saveView" disabled={problem !== null} onSelect={onSave}>
          Save this view…
        </MenuItem>
      )}
      {problem && !current ? <MenuLabel>{problem}</MenuLabel> : null}
      {current ? (
        <>
          <MenuItem icon={PencilLine} onSelect={() => onRename(current)}>
            {`Rename “${current.name}”…`}
          </MenuItem>
          <MenuItem icon={Trash2} onSelect={() => onDelete(current)}>
            {`Delete “${current.name}”`}
          </MenuItem>
        </>
      ) : null}
    </DropdownMenu>
  );
}

export interface ViewNameDialogProps {
  /** `save` names a new view; `rename` gives the view another name. Null: closed. */
  mode: { kind: 'save' } | { kind: 'rename'; view: SavedView } | null;
  views: readonly SavedView[];
  onClose: () => void;
  onSubmit: (name: string) => void;
  /** Where the keyboard goes once it closes: the menu item or command that opened it has gone. */
  onCloseFocus?: (() => void) | undefined;
}

/** Names a view: saved, or renamed. What's wrong with a name is said under the field, never in a toast. */
export function ViewNameDialog({ mode, views, onClose, onSubmit, onCloseFocus }: ViewNameDialogProps) {
  const [name, setName] = useState('');
  const [shownFor, setShownFor] = useState<ViewNameDialogProps['mode']>(null);
  const [tried, setTried] = useState(false);
  if (shownFor !== mode) {
    // Opened again: the field starts with the view's own name, or empty for a new one.
    setShownFor(mode);
    setName(mode?.kind === 'rename' ? mode.view.name : '');
    setTried(false);
  }
  const problem = viewNameProblem(name, views, 'urls', mode?.kind === 'rename' ? mode.view.id : undefined);
  const submit = () => {
    setTried(true);
    if (problem === null) onSubmit(name);
  };
  return (
    <Dialog
      open={mode !== null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      {...(onCloseFocus
        ? {
            onCloseAutoFocus: (event: Event) => {
              event.preventDefault();
              onCloseFocus();
            },
          }
        : {})}
      title={mode?.kind === 'rename' ? 'Rename the view' : 'Save this view'}
      description={
        mode?.kind === 'rename'
          ? undefined
          : 'Its filters, search, sort and columns, under a name: it opens from Saved views on any crawl.'
      }
      footer={
        <>
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" onClick={submit}>
            {mode?.kind === 'rename' ? 'Rename' : 'Save'}
          </Button>
        </>
      }
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <TextField
          label="Name"
          value={name}
          maxLength={MAX_VIEW_NAME}
          error={tried ? problem : null}
          onChange={(event) => setName(event.target.value)}
        />
      </form>
    </Dialog>
  );
}
