// What a sort keeps when a column goes, and what Undo of a deleted saved view never does (review findings 4 and 8).
import { renderHook, act } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { sortShown } from '../links/presets';
import { getSettings, updateSettings } from '../../settings/settingsStore';
import { DEFAULT_SETTINGS } from '../../settings/schema';
import { MAX_SAVED_VIEWS, type SavedView } from '../../grid/savedViews';
import type * as Notify from '../../feedback/notify';

const undos: (() => void)[] = [];
vi.mock('../../feedback/notify', async (original) => ({
  ...(await original<typeof Notify>()),
  notifyUndo: (_title: string, onUndo: () => void) => {
    undos.push(onUndo);
  },
}));

const { useSavedViews } = await import('./useSavedViews');

describe('hiding one key of a sort of several', () => {
  it('keeps the other keys (UrlsScreen setColumns and the hiddenSort effect both use sortShown)', () => {
    const sort = [
      { column: 'depth', desc: false },
      { column: 'inlinks', desc: true },
      { column: 'responseMs', desc: false },
    ];
    // The user hides "Response time": depth and inlinks are still shown.
    expect(sortShown(sort, ['url', 'depth', 'inlinks'])).toEqual(sort.slice(0, 2));
  });
});

describe('Undo of a deleted saved view', () => {
  beforeEach(() => {
    undos.length = 0;
    const views: SavedView[] = Array.from({ length: MAX_SAVED_VIEWS }, (_, index) => ({
      id: `v-${String(index).padStart(8, '0')}`,
      grid: 'urls',
      name: `View ${index}`,
      search: { q: `q${index}` },
    }));
    updateSettings({ ...DEFAULT_SETTINGS, savedViews: views });
  });

  it('never silently drops another view (24 saved: delete one, save one, Undo)', () => {
    const { result, rerender } = renderHook(({ q }: { q: string }) => useSavedViews({ q }), {
      initialProps: { q: 'q0' },
    });
    act(() => result.current.remove(result.current.views[0]!));
    rerender({ q: 'fresh' });
    act(() => void result.current.save('Just saved'));
    expect(getSettings().savedViews.map((view) => view.name)).toContain('Just saved');
    act(() => undos[0]!());
    const names = getSettings().savedViews.map((view) => view.name);
    // Either the Undo is refused (and says so) or both are kept: the view just saved must not vanish.
    expect(names).toContain('Just saved');
  });
});
