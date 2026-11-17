import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { TransportRequest } from '../../api/transport';
import { setTransport } from '../../api/transport';
import type { AnalysisLimit, AnalysisSettings } from '../../api/types';
import { App } from '../../app/App';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';
import { onAnnounce } from '../../feedback/announce';
import { replaceSettings } from '../../settings/settingsStore';
import { fakeSidecar, json, problem, standardRoutes } from '../../test/fakeSidecar';
import {
  approximateCharacters,
  changeOf,
  charactersHint,
  draftOf,
  limitName,
  rangeWords,
  sameAsSaved,
  splitErrors,
  unitWords,
  valueWords,
} from './analysisLimits';

const LIMITS: AnalysisLimit[] = [
  {
    id: 'titleMinPixels',
    value: 200,
    default: 200,
    unit: 'pixels',
    min: 0,
    max: 100_000,
    description: 'A title narrower than this is flagged as too short.',
    pixelsPerCharacter: 9.4,
  },
  {
    id: 'titleMaxPixels',
    value: 580,
    default: 580,
    unit: 'pixels',
    min: 1,
    max: 100_000,
    description: 'A title wider than this in search results is flagged as too long.',
    pixelsPerCharacter: 9.4,
  },
  {
    id: 'largeImageBytes',
    value: 204_800,
    default: 204_800,
    unit: 'bytes',
    min: 1,
    max: 10_737_418_240,
    description: 'An image larger than this is flagged as too large.',
  },
];

describe('the limits’ words', () => {
  it('names each limit, and its range and the engine’s value in its unit', () => {
    expect(limitName('titleMaxPixels')).toBe('Widest title');
    expect(limitName('titleMinPixels')).toBe('Narrowest title');
    expect(limitName('descriptionMinPixels')).toBe('Narrowest meta description');
    expect(limitName('descriptionMaxPixels')).toBe('Widest meta description');
    expect(limitName('someNewLimit')).toBe('Some new limit');
    expect(rangeWords(LIMITS[1]!)).toBe('From 1 to 100,000 px. The engine’s is 580 px (about 60 characters).');
    expect(rangeWords(LIMITS[0]!)).toBe('From 0 to 100,000 px. The engine’s is 200 px (about 20 characters).');
    expect(rangeWords(LIMITS[2]!)).toBe('From 1 to 10,737,418,240 bytes (10 GB). The engine’s is 200 KB.');
    expect(valueWords(1, 'words')).toBe('1 word');
    expect(valueWords(580, 'pixels')).toBe('580 px');
    expect(unitWords('pixels')).toBe('px');
  });

  it('says a width in pixels as the ordinary characters it holds, rounded as the engine’s plain words round it', () => {
    // To the nearest 5 from 20 on, to the nearest whole below: 580 px of title is about 60, 985 of description 160.
    expect(approximateCharacters(580, 9.4)).toBe(60);
    expect(approximateCharacters(200, 9.4)).toBe(20);
    expect(approximateCharacters(985, 6.2)).toBe(160);
    expect(approximateCharacters(400, 6.2)).toBe(65);
    expect(approximateCharacters(185, 9.4)).toBe(20);
    expect(approximateCharacters(150, 9.4)).toBe(16);
    expect(approximateCharacters(9, 9.4)).toBe(1);
    const widest = LIMITS[1]!;
    expect(charactersHint(widest, '580')).toBe('About 60 characters of ordinary text.');
    expect(charactersHint(widest, '1,000')).toBe('About 105 characters of ordinary text.');
    expect(charactersHint(widest, ' 700 ')).toBe('About 75 characters of ordinary text.');
    expect(charactersHint(widest, '9')).toBe('About 1 character of ordinary text.');
    // Nothing for an empty or invalid field, or a limit that isn't a width.
    expect(charactersHint(widest, '')).toBeNull();
    expect(charactersHint(widest, 'abc')).toBeNull();
    expect(charactersHint(widest, '7.5')).toBeNull();
    expect(charactersHint(widest, '0')).toBeNull();
    expect(charactersHint(widest, '100001')).toBeNull();
    expect(charactersHint(LIMITS[2]!, '1000')).toBeNull();
  });

  it('sends what’s typed as typed, a whole number as a number and an empty field as the engine’s', () => {
    expect(changeOf({ a: '70', b: '1,000', c: '', d: '7.5', e: 'abc' })).toEqual({
      a: 70,
      b: 1_000,
      c: null,
      d: '7.5',
      e: 'abc',
    });
    expect(sameAsSaved(draftOf(LIMITS), LIMITS)).toBe(true);
    expect(sameAsSaved({ ...draftOf(LIMITS), titleMaxPixels: '581' }, LIMITS)).toBe(false);
  });

  it('puts a limit’s error beside it, and the whole set’s and a reset’s above the form', () => {
    expect(
      splitErrors({ titleMaxPixels: ['Too narrow.'], settings: ['Together, no.'], 'limits[0]': ['No such limit.'] }, [
        'titleMaxPixels',
      ]),
    ).toEqual({ byLimit: { titleMaxPixels: 'Too narrow.' }, whole: ['Together, no.', 'No such limit.'] });
  });
});

function settingsOf(values: Record<string, number>): AnalysisSettings {
  const limits = LIMITS.map((limit) => ({ ...limit, value: values[limit.id] ?? limit.default }));
  return {
    settings: Object.fromEntries(limits.map((limit) => [limit.id, limit.value])),
    limits,
    isDefault: limits.every((limit) => limit.value === limit.default),
  };
}

/** The engine's limits, kept as a save leaves them; a widest title not above the narrowest is refused. */
function sidecar() {
  let saved: Record<string, number> = {};
  const errorsOf = (body: Record<string, unknown>) => {
    const max = body.titleMaxPixels ?? 580;
    const min = body.titleMinPixels ?? 200;
    return typeof max === 'number' && typeof min === 'number' && max <= min
      ? { titleMaxPixels: [`The widest title allowed must be more than the narrowest (${min}).`] }
      : {};
  };
  const transport = fakeSidecar([
    ...standardRoutes(),
    ['GET', /^\/api\/analysis-settings$/, () => json(settingsOf(saved))],
    [
      'POST',
      /^\/api\/analysis-settings\/validate$/,
      (request: TransportRequest) => json({ errors: errorsOf(request.body as Record<string, unknown>) }),
    ],
    [
      'PUT',
      /^\/api\/analysis-settings$/,
      (request: TransportRequest) => {
        const body = request.body as Record<string, number>;
        const errors = errorsOf(body);
        if (Object.keys(errors).length > 0) {
          return problem({ type: 'invalid-settings', title: 'Those limits can’t be saved.', status: 422, errors });
        }
        saved = Object.fromEntries(Object.entries(body).filter(([, value]) => typeof value === 'number'));
        return json(settingsOf(saved));
      },
    ],
    [
      'POST',
      /^\/api\/analysis-settings\/reset$/,
      (request: TransportRequest) => {
        const names = (request.body as { limits?: string[] } | undefined)?.limits;
        saved = names ? Object.fromEntries(Object.entries(saved).filter(([id]) => !names.includes(id))) : {};
        return json(settingsOf(saved));
      },
    ],
  ]);
  setTransport(transport);
  return transport;
}

async function open() {
  replaceSettings({ mode: 'advanced' });
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: ['/settings/analysis'] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return queryClient;
}

afterEach(() => setTransport(fakeSidecar([])));

describe('Settings, Analysis', () => {
  it('lists every limit with what it does, its unit and its range', async () => {
    sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    expect(field).toHaveValue('580');
    expect(field).toHaveAccessibleDescription(
      /A title wider than this in search results is flagged as too long\. From 1 to 100,000 px\. The engine’s is 580 px \(about 60 characters\)\./,
    );
    expect(screen.getByRole('textbox', { name: 'Largest image' })).toHaveValue('204800');
    expect(screen.getByRole('textbox', { name: 'Largest image' })).not.toHaveAccessibleDescription(/ordinary text/);
  });

  it('says a width as the characters it holds, from what’s typed, and nothing while it isn’t a width', async () => {
    sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    expect(field).toHaveAccessibleDescription(/About 60 characters of ordinary text\.$/);
    // Its unit beside it.
    expect(screen.getAllByText('px', { exact: true }).length).toBeGreaterThan(0);
    fireEvent.change(field, { target: { value: '700' } });
    expect(field).toHaveAccessibleDescription(/About 75 characters of ordinary text\.$/);
    fireEvent.change(field, { target: { value: 'abc' } });
    expect(field).not.toHaveAccessibleDescription(/ordinary text/);
    fireEvent.change(field, { target: { value: '' } });
    expect(field).not.toHaveAccessibleDescription(/ordinary text/);
  });

  it('says what the engine refuses beside the limit, and saves nothing until it’s put right', async () => {
    const transport = sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    fireEvent.change(field, { target: { value: '150' } });
    expect(
      await screen.findByText(
        'The widest title allowed must be more than the narrowest (200).',
        {},
        { timeout: 2_000 },
      ),
    ).toBeVisible();
    expect(field).toHaveAttribute('aria-invalid', 'true');
    await new Promise((resolve) => setTimeout(resolve, 800));
    expect(transport.requests.some((request) => request.method === 'PUT')).toBe(false);
  });

  it('saves a change a moment after it’s made, and reads every crawl again, which may now be out of date', async () => {
    const transport = sidecar();
    const queryClient = await open();
    const invalidated: unknown[] = [];
    const invalidate = queryClient.invalidateQueries.bind(queryClient);
    queryClient.invalidateQueries = (filters, options) => {
      invalidated.push(filters);
      return invalidate(filters, options);
    };
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    fireEvent.change(field, { target: { value: '700' } });
    await waitFor(
      () =>
        expect(transport.requests.find((request) => request.method === 'PUT')?.body).toMatchObject({
          titleMaxPixels: 700,
        }),
      { timeout: 2_000 },
    );
    // No event says so: the library and every crawl's reads are read again, and say "out of date" where they are.
    await waitFor(() => expect(invalidated).toContainEqual({ queryKey: ['library'] }));
    expect(invalidated).toContainEqual({ queryKey: ['crawl'] });
    expect(await screen.findByRole('button', { name: 'Put back 580 px' })).toBeVisible();
  });

  it('puts one limit back, and every limit back, each with Undo', async () => {
    const transport = sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    fireEvent.change(field, { target: { value: '700' } });
    const putBack = await screen.findByRole('button', { name: 'Put back 580 px' }, { timeout: 2_000 });
    fireEvent.click(putBack);
    await waitFor(() => expect(field).toHaveValue('580'));
    const resets = () => transport.requests.filter((request) => request.path === '/api/analysis-settings/reset');
    expect(resets().at(-1)?.body).toEqual({ limits: ['titleMaxPixels'] });
    fireEvent.change(field, { target: { value: '800' } });
    await screen.findByRole('button', { name: 'Put back 580 px' }, { timeout: 2_000 });
    fireEvent.click(screen.getByRole('button', { name: 'Put back every limit' }));
    await waitFor(() => expect(field).toHaveValue('580'));
    expect(resets()).toHaveLength(2);
    expect(resets().at(-1)?.body).toBeUndefined();
  });

  it('says why when the engine keys a refusal on a limit the user didn’t touch (the review’s finding 1)', async () => {
    const transport = sidecar();
    await open();
    const narrowest = await screen.findByRole('textbox', { name: 'Narrowest title' });
    // The engine reports a narrowest wider than the widest on the widest.
    fireEvent.change(narrowest, { target: { value: '700' } });
    const widest = screen.getByRole('textbox', { name: 'Widest title' });
    await waitFor(() => expect(widest).toHaveAttribute('aria-invalid', 'true'), { timeout: 2_000 });
    expect(widest).toHaveAccessibleDescription(/must be more than the narrowest \(700\)/);
    // Above the form too, named, as the limit wasn't the one changed.
    expect(screen.getByText('These limits can’t be saved together').closest('[role="alert"]')).toHaveTextContent(
      'Widest title: The widest title allowed must be more than the narrowest (700).',
    );
    expect(transport.requests.some((request) => request.method === 'PUT')).toBe(false);
  });

  it('doesn’t say a save of an older draft is saved once more has been typed (the review’s finding 7)', async () => {
    const said: string[] = [];
    const off = onAnnounce((message) => said.push(message));
    let release: (() => void) | null = null;
    const transport = fakeSidecar([
      ...standardRoutes(),
      ['GET', /^\/api\/analysis-settings$/, () => json(settingsOf({}))],
      ['POST', /^\/api\/analysis-settings\/validate$/, () => json({ errors: {} })],
      [
        'PUT',
        /^\/api\/analysis-settings$/,
        (request: TransportRequest) =>
          new Promise<Response>((resolve) => {
            release = () => resolve(json(settingsOf(request.body as Record<string, number>)));
          }),
      ],
    ]);
    setTransport(transport);
    await open();
    const field = await screen.findByRole('textbox', { name: 'Widest title' });
    fireEvent.change(field, { target: { value: '700' } });
    await waitFor(() => expect(release).not.toBeNull(), { timeout: 2_000 });
    // More is typed while the save of 700 is on its way; then the engine answers it.
    fireEvent.change(field, { target: { value: '750' } });
    act(() => release?.());
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(said.filter((message) => message.startsWith('Saved'))).toEqual([]);
    expect(screen.queryByText('Saved', { exact: true })).toBeNull();
    off();
  });
});
