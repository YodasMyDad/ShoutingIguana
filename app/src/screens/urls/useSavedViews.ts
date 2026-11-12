// The URL grid's saved views as the screen uses them: the views, the one the route is on, why the view can't be saved
// when it can't, and saving, renaming and deleting one (each with Undo where it's reversible).
import { notifyUndo } from '../../feedback/notify';
import {
  currentSavedView,
  newViewId,
  saveProblem,
  viewNameProblem,
  viewSearchOf,
  type SavedView,
  type SavedViewSearch,
} from '../../grid/savedViews';
import { getSettings, updateSettings, useSettings } from '../../settings/settingsStore';

/** The URL grid's saved views, the one the route is on, and what can be done with them. */
export function useSavedViews(search: SavedViewSearch) {
  const views = useSettings((settings) => settings.savedViews);
  const current = currentSavedView(views, 'urls', viewSearchOf(search));
  const problem = saveProblem(views, search);
  const save = (name: string) => {
    const view: SavedView = { id: newViewId(getSettings().savedViews), grid: 'urls', name: name.trim(), search };
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
    notifyUndo(`Deleted the view “${view.name}”`, () =>
      updateSettings((settings) => {
        // Back where it was, unless a view of the same name was saved meanwhile.
        if (viewNameProblem(view.name, settings.savedViews, 'urls') !== null) return {};
        const next = [...settings.savedViews];
        next.splice(Math.min(at, next.length), 0, view);
        return { savedViews: next };
      }),
    );
  };
  return { views, current, problem, save, rename, remove };
}
