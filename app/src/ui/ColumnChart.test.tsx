import { createMemoryHistory, createRootRoute, createRouter, RouterProvider } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ColumnChart, type ChartColumn } from './ColumnChart';
import { fittingText, valueTexts } from './columnValues';

/** jsdom lays nothing out, so the test gives the widths a browser would: a column's room, and 7 px a character. */
function layOut(columnWidth: number) {
  return vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const width = this.classList.contains('si-columns__probe')
      ? this.textContent.length * 7
      : this.classList.contains('si-columns__item')
        ? columnWidth
        : 0;
    return { width, height: 0, top: 0, left: 0, right: width, bottom: 0, x: 0, y: 0, toJSON: () => ({}) };
  });
}

const DEEP = [1, 73, 1_740, 26_493, 312_876, 401_532, 98_211, 12_004, 3_310, 870, 1_204_998];

const columns = (values: readonly number[]): ChartColumn[] =>
  values.map((value, depth) => ({
    key: String(depth),
    label: String(depth),
    value,
    valueText: value.toLocaleString('en-GB'),
    name: `Depth ${depth}: ${value.toLocaleString('en-GB')} pages`,
    link: { to: '/' },
  }));

async function draw(chart: ChartColumn[]) {
  const rootRoute = createRootRoute({
    component: () => <ColumnChart label="Pages by depth" columns={chart} />,
  });
  const router = createRouter({ routeTree: rootRoute, history: createMemoryHistory({ initialEntries: ['/'] }) });
  render(<RouterProvider router={router} />);
  await act(() => router.load());
  return within(await screen.findByRole('list', { name: 'Pages by depth' }));
}

afterEach(() => vi.restoreAllMocks());

describe('a column chart’s numbers', () => {
  it('are written shorter where the whole number is wider than its column, and stay whole in its name and tooltip', async () => {
    // Eleven columns in half a 1280 px window: 38 px each. "312,876" is 49 px wide and ran into "401,532".
    layOut(38);
    const chart = await draw(columns(DEEP));
    const drawn = chart.getAllByRole('listitem').map((item) => item.querySelector('.si-columns__value')?.textContent);
    expect(drawn).toEqual(['1', '73', '1,740', '26.5k', '313k', '402k', '98.2k', '12k', '3,310', '870', '1.2M']);
    // A column too narrow for any way of writing it gets the narrowest, and of two as narrow the fuller.
    // The whole number is what the link is called, and a hover away, wherever the one drawn isn't it.
    const deep = chart.getByRole('link', { name: 'Depth 4: 312,876 pages' });
    expect(deep).toHaveAttribute('title', 'Depth 4: 312,876 pages');
    expect(chart.getByRole('link', { name: 'Depth 10: 1,204,998 pages' })).toHaveAttribute(
      'title',
      'Depth 10: 1,204,998 pages',
    );
    // A number that fits is drawn whole, and needs no tooltip to say it.
    expect(chart.getByRole('link', { name: 'Depth 2: 1,740 pages' })).not.toHaveAttribute('title');
  });

  it('are whole where there’s room, and where the room isn’t known', async () => {
    layOut(120);
    const wide = await draw(columns(DEEP));
    expect(
      wide.getByRole('link', { name: 'Depth 4: 312,876 pages' }).querySelector('.si-columns__value'),
    ).toHaveTextContent('312,876');
    expect(wide.getByRole('link', { name: 'Depth 4: 312,876 pages' })).not.toHaveAttribute('title');
  });
});

describe('the ways to write a column’s value', () => {
  it('are the whole number, then three figures, then two, each once', () => {
    expect(valueTexts({ value: 312_876, valueText: '312,876' })).toEqual(['312,876', '313k', '310k']);
    expect(valueTexts({ value: 26_493, valueText: '26,493' })).toEqual(['26,493', '26.5k', '26k']);
    expect(valueTexts({ value: 1_204_998, valueText: '1,204,998' })).toEqual(['1,204,998', '1.2M']);
    expect(valueTexts({ value: 870, valueText: '870' })).toEqual(['870']);
  });

  it('give the fullest that fits, the narrowest when none does, and the whole number when the room isn’t known', () => {
    const ways = [
      { text: '312,876', width: 49 },
      { text: '313k', width: 28 },
      { text: '310k', width: 28 },
    ];
    expect(fittingText(ways, 60)).toBe('312,876');
    expect(fittingText(ways, 49)).toBe('312,876');
    expect(fittingText(ways, 38)).toBe('313k');
    expect(fittingText(ways, 10)).toBe('313k');
    expect(
      fittingText(
        [
          { text: '26,493', width: 42 },
          { text: '26.5k', width: 35 },
          { text: '26k', width: 21 },
        ],
        10,
      ),
    ).toBe('26k');
    expect(fittingText(ways, 0)).toBe('312,876');
    expect(fittingText([], 38)).toBe('');
  });
});
