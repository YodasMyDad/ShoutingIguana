import { describe, expect, it } from 'vitest';
import { readSettings } from '../settings/schema';
import {
  MAX_SAVED_VIEWS,
  currentSavedView,
  newViewId,
  readSavedViews,
  saveProblem,
  sameView,
  viewNameProblem,
  viewSearchOf,
  type SavedView,
} from './savedViews';

const broken: SavedView = {
  id: 'v-broken',
  grid: 'urls',
  name: 'Broken pages',
  search: {
    filter: [{ column: 'statusClass', op: 'in', values: ['4xx', '5xx'] }],
    sort: [{ column: 'depth', desc: false }],
  },
};

describe('a saved view', () => {
  it('is the route’s view without its defaults or the URL inspected', () => {
    expect(viewSearchOf({ filter: [], sort: [], cols: undefined, q: undefined })).toEqual({});
    expect(viewSearchOf({ q: 'shoes', cols: ['url'] })).toEqual({ q: 'shoes', cols: ['url'] });
  });

  it('is the same view whatever order its filters came in, and another for another sort order or columns', () => {
    const filters = [
      { column: 'depth', op: 'eq' as const, value: 2 },
      { column: 'statusClass', op: 'in' as const, values: ['5xx', '4xx'] },
    ];
    expect(sameView({ filter: filters }, { filter: [...filters].reverse() })).toBe(true);
    expect(
      sameView(
        {
          sort: [
            { column: 'a', desc: false },
            { column: 'b', desc: false },
          ],
        },
        {
          sort: [
            { column: 'b', desc: false },
            { column: 'a', desc: false },
          ],
        },
      ),
    ).toBe(false);
    expect(sameView({ cols: ['url', 'title'] }, { cols: ['title', 'url'] })).toBe(false);
    expect(sameView({ q: 'a' }, {})).toBe(false);
    expect(sameView({}, { filter: [], sort: [] })).toBe(true);
  });

  it('is the one the route is on when its view is exactly the route’s', () => {
    expect(currentSavedView([broken], 'urls', { ...broken.search })).toBe(broken);
    expect(currentSavedView([broken], 'urls', { ...broken.search, q: 'x' })).toBeNull();
  });

  it('needs a name of its own, at most 60 characters, whatever its case', () => {
    expect(viewNameProblem('  ', [broken], 'urls')).toBe('Give the view a name.');
    expect(viewNameProblem('x'.repeat(61), [broken], 'urls')).toMatch(/60 characters at most/);
    expect(viewNameProblem('broken PAGES', [broken], 'urls')).toBe(
      'A view is called “Broken pages” already. Choose another name.',
    );
    // Its own name, when it's renamed.
    expect(viewNameProblem('Broken pages', [broken], 'urls', broken.id)).toBeNull();
  });

  it('can’t be saved past the app’s limit, nor when it’s too long to keep: each says why', () => {
    const many = Array.from({ length: MAX_SAVED_VIEWS }, (_, index) => ({
      ...broken,
      id: `v-${index}`,
      name: `${index}`,
    }));
    expect(saveProblem(many, {})).toMatch(/^24 views are saved/);
    const long = Array.from({ length: 20 }, () => ({
      column: 'title',
      op: 'contains' as const,
      value: 'x'.repeat(100),
    }));
    expect(saveProblem([], { filter: long })).toMatch(/too long to keep/);
    expect(saveProblem([], broken.search)).toBeNull();
  });

  it('gets an id no other has', () => {
    const values = [0, 0, 0.5];
    const id = newViewId([{ ...broken, id: 'v-00000000' }], () => values.shift() ?? 0.25);
    expect(id).toMatch(/^v-[a-z0-9]{8}$/);
    expect(id).not.toBe('v-00000000');
  });
});

describe('saved views as stored', () => {
  it('are checked as a route is, and what can’t be read is dropped', () => {
    const views = readSavedViews([
      broken,
      { ...broken, id: 'v-dup', name: 'BROKEN PAGES' },
      { id: 'BAD ID', grid: 'urls', name: 'x', search: {} },
      { id: 'v-links', grid: 'links', name: 'Other grid', search: {} },
      {
        id: 'v-odd',
        grid: 'urls',
        name: ' Odd ',
        search: { sort: '-depth,url', filter: 'nonsense', cols: 'url,title', q: 7 },
      },
      'nonsense',
      null,
    ]);
    expect(views).toEqual([
      broken,
      {
        id: 'v-odd',
        grid: 'urls',
        name: 'Odd',
        search: {
          sort: [
            { column: 'depth', desc: true },
            { column: 'url', desc: false },
          ],
          cols: ['url', 'title'],
          q: '7',
        },
      },
    ]);
    expect(readSavedViews('nonsense')).toEqual([]);
  });

  it('are kept with the app’s preferences, with "Show full URLs"', () => {
    expect(readSettings({}).savedViews).toEqual([]);
    expect(readSettings({}).showFullUrls).toBe(false);
    const settings = readSettings({ savedViews: [broken], showFullUrls: true });
    expect(settings.savedViews).toEqual([broken]);
    expect(settings.showFullUrls).toBe(true);
    expect(readSettings({ showFullUrls: 'yes' }).showFullUrls).toBe(false);
  });
});
