// The URL grid's saved views as the screen uses them: the views, the one the route is on, why the view can't be saved
// when it can't, and saving, renaming and deleting one (each with Undo where it's reversible).
import { notifyResult, notifyUndo } from '../../feedback/notify';
import {
  MAX_SAVED_VIEWS,
  MAX_VIEWS_BYTES,
  currentSavedView,
  jsonBytes,
  newViewId,
  saveProblem,
  viewLayoutOf,
  viewNameProblem,
  viewSearchOf,
  type SavedView,
  type SavedViewSearch,
} from '../../grid/savedViews';
import { getSettings, updateSettings, useSettings } from '../../settings/settingsStore';

/** The URL grid's saved views, the one the route is on, and what can be done with them. */
export function useSavedViews(search: SavedViewSearch) {
  const views = useSettings((settings) => settings.savedViews);
  const layout = useSettings((settings) => settings.gridColumns.urls);
  const current = currentSavedView(views, 'urls', viewSearchOf(search));
  const problem = saveProblem(views, search, layout);
  const save = (name: string) => {
    // The columns' widths and pins go with it: opening it lays them out so again.
    const kept = viewLayoutOf(getSettings().gridColumns.urls);
    const view: SavedView = {
      id: newViewId(getSettings().savedViews),
      grid: 'urls',
      name: name.trim(),
      search,
      ...(kept ? { layout: kept } : {}),
    };
    updateSettings((settings) => ({ savedViews: [...settings.savedViews, { ...view, search: viewSearchOf(search) }] }));
    return view;
  };
  const rename = (view: SavedView, name: string) => {
    const before = view.name;
    updateSettings((settings) => ({
      savedViews: settings.savedViews.map((other) => (other.id === view.id ? { ...other, name: name.trim() } : other)),
    }));
    notifyUndo(`Renamed “${before}” to “${name.trim()}”`, () =>
      updateSettings((settings) => ({
        savedViews: settings.savedViews.map((other) => (other.id === view.id ? { ...other, name: before } : other)),
      })),
    );
  };
  const remove = (view: SavedView) => {
    const at = getSettings().savedViews.findIndex((other) => other.id === view.id);
    updateSettings((settings) => ({ savedViews: settings.savedViews.filter((other) => other.id !== view.id) }));
    notifyUndo(`Deleted the view “${view.name}”`, () => {
      // Back where it was, unless that would cost another view: one of its name was saved meanwhile, or as many as
      // the app keeps were. Then it says so, rather than drop either.
      const views = getSettings().savedViews;
      const taken = viewNameProblem(view.name, views, 'urls') !== null;
      const full = views.length >= MAX_SAVED_VIEWS || jsonBytes([...views, view]) > MAX_VIEWS_BYTES;
      if (taken || full) {
        notifyResult(`“${view.name}” wasn’t put back`, {
          id: 'urls.undoView',
          description: taken
            ? 'A view of that name was saved since. Rename it, then save this one again.'
            : `${MAX_SAVED_VIEWS} views are saved, or all the room for them is taken. Delete one to save this one again.`,
        });
        return;
      }
      updateSettings((settings) => {
        const next = [...settings.savedViews];
        next.splice(Math.min(at, next.length), 0, view);
        return { savedViews: next };
      });
    });
  };
  return { views, current, problem, save, rename, remove };
}
