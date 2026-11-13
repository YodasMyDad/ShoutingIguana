import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { TransportRequest } from '../../api/transport';
import { setTransport } from '../../api/transport';
import type { AnalysisLimit, AnalysisSettings } from '../../api/types';
import { App } from '../../app/App';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';
import { replaceSettings } from '../../settings/settingsStore';
import { fakeSidecar, json, problem, standardRoutes } from '../../test/fakeSidecar';
import { changeOf, draftOf, limitName, rangeWords, sameAsSaved, splitErrors, valueWords } from './analysisLimits';

const LIMITS: AnalysisLimit[] = [
  {
    id: 'titleMinLength',
    value: 30,
    default: 30,
    unit: 'characters',
    min: 0,
    max: 10_000,
    description: 'A title shorter than this is flagged as too short.',
  },
  {
    id: 'titleMaxLength',
    value: 60,
    default: 60,
    unit: 'characters',
    min: 1,
    max: 10_000,
    description: 'A title longer than this is flagged as too long.',
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
    expect(limitName('titleMaxLength')).toBe('Longest title');
    expect(limitName('someNewLimit')).toBe('Some new limit');
    expect(rangeWords(LIMITS[1]!)).toBe('From 1 to 10,000 characters. The engine’s is 60 characters.');
    expect(rangeWords(LIMITS[2]!)).toBe('From 1 to 10,737,418,240 bytes (10 GB). The engine’s is 200 KB.');
    expect(valueWords(1, 'words')).toBe('1 word');
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
    expect(sameAsSaved({ ...draftOf(LIMITS), titleMaxLength: '61' }, LIMITS)).toBe(false);
  });

  it('puts a limit’s error beside it, and the whole set’s and a reset’s above the form', () => {
    expect(
      splitErrors({ titleMaxLength: ['Too short.'], settings: ['Together, no.'], 'limits[0]': ['No such limit.'] }, [
        'titleMaxLength',
      ]),
    ).toEqual({ byLimit: { titleMaxLength: 'Too short.' }, whole: ['Together, no.', 'No such limit.'] });
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

/** The engine's limits, kept as a save leaves them; a longest title not above the shortest is refused. */
function sidecar() {
  let saved: Record<string, number> = {};
  const errorsOf = (body: Record<string, unknown>) => {
    const max = body.titleMaxLength ?? 60;
    const min = body.titleMinLength ?? 30;
    return typeof max === 'number' && typeof min === 'number' && max <= min
      ? { titleMaxLength: [`The longest title allowed must be more than the shortest (${min}).`] }
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
    const field = await screen.findByRole('textbox', { name: 'Longest title' });
    expect(field).toHaveValue('60');
    expect(field).toHaveAccessibleDescription(/A title longer than this is flagged as too long\. From 1 to 10,000/);
    expect(screen.getByRole('textbox', { name: 'Largest image' })).toHaveValue('204800');
  });

  it('says what the engine refuses beside the limit, and saves nothing until it’s put right', async () => {
    const transport = sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Longest title' });
    fireEvent.change(field, { target: { value: '20' } });
    expect(
      await screen.findByText('The longest title allowed must be more than the shortest (30).', {}, { timeout: 2_000 }),
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
    const field = await screen.findByRole('textbox', { name: 'Longest title' });
    fireEvent.change(field, { target: { value: '70' } });
    await waitFor(
      () =>
        expect(transport.requests.find((request) => request.method === 'PUT')?.body).toMatchObject({
          titleMaxLength: 70,
        }),
      { timeout: 2_000 },
    );
    // No event says so: the library and every crawl's reads are read again, and say "out of date" where they are.
    await waitFor(() => expect(invalidated).toContainEqual({ queryKey: ['library'] }));
    expect(invalidated).toContainEqual({ queryKey: ['crawl'] });
    expect(await screen.findByRole('button', { name: 'Put back 60 characters' })).toBeVisible();
  });

  it('puts one limit back, and every limit back, each with Undo', async () => {
    const transport = sidecar();
    await open();
    const field = await screen.findByRole('textbox', { name: 'Longest title' });
    fireEvent.change(field, { target: { value: '70' } });
    const putBack = await screen.findByRole('button', { name: 'Put back 60 characters' }, { timeout: 2_000 });
    fireEvent.click(putBack);
    await waitFor(() => expect(field).toHaveValue('60'));
    const resets = () => transport.requests.filter((request) => request.path === '/api/analysis-settings/reset');
    expect(resets().at(-1)?.body).toEqual({ limits: ['titleMaxLength'] });
    fireEvent.change(field, { target: { value: '80' } });
    await screen.findByRole('button', { name: 'Put back 60 characters' }, { timeout: 2_000 });
    fireEvent.click(screen.getByRole('button', { name: 'Put back every limit' }));
    await waitFor(() => expect(field).toHaveValue('60'));
    expect(resets()).toHaveLength(2);
    expect(resets().at(-1)?.body).toBeUndefined();
  });
});
