// The mock's answers to what E6 added to the API (docs/v2-sidecar-api.md, E6): search as it's typed, a links view's
// ends and the URL grids narrowed to them, the analysis's limits, robots.txt's origins searched and filtered, and the
// site's icon. Each is a pure answer over rows the mock already has, as the links grid's are (./linkGrid.ts), so the
// rules are the sidecar's and tests can hold them; e6Routes wires them into the mock with one line.
import type { ProblemBody } from '../errors';
import type {
  GridFilter,
  LinkEnd,
  LinkEndRow,
  LinkEnds,
  LinkEndsPage,
  LinkRow,
  RobotsOriginPage,
  RobotsOriginsQuery,
  SearchRank,
  SearchResult,
  SearchResults,
  SummaryState,
  UrlRow,
} from '../grids';
import type { AnalysisLimit, AnalysisLimitUnit, AnalysisSettings } from '../types';
import { pagingErrors } from './gridQuery';
import { answerLinkQuery } from './linkGrid';
import type { MockCrawls, MockRoute } from './sitemapsRobots';

export type Answer<T> = { ok: true; page: T } | { ok: false; problem: ProblemBody };

const invalidQuery = (errors: Record<string, string[]>): ProblemBody => ({
  type: 'invalid-query',
  title: 'This view’s query can’t be run.',
  status: 422,
  detail: Object.values(errors).flat()[0] ?? '',
  errors,
});

const badRequest = (detail: string): ProblemBody => ({
  type: 'bad-request',
  title: 'The request body isn’t the JSON this call takes.',
  status: 400,
  detail,
});

// ------------------------------------------------------------------------------------------------------------ Search

export const SEARCH_DEFAULT_LIMIT = 10;
export const SEARCH_MAX_LIMIT = 50;
export const SEARCH_MAX_TEXT = 1_000;

const RANKS: readonly SearchRank[] = ['exact', 'start', 'anywhere'];

/** ASCII capitals lowercased, and nothing else: what the search ignores. */
const fold = (text: string) => text.replace(/[A-Z]/g, (letter) => letter.toLowerCase());

const isWordCharacter = (character: string) => /[\p{L}\p{N}]/u.test(character);

/** The character before a place in a text, a pair of surrogates whole. */
function characterBefore(text: string, at: number): string {
  const low = text.charCodeAt(at - 1);
  return at >= 2 && low >= 0xdc00 && low <= 0xdfff ? text.slice(at - 2, at) : text.slice(at - 1, at);
}

/**
 * How well a URL matches, as the sidecar ranks it: `exact` when the text is the URL, with or without its scheme, or its
 * path, the slashes around it not counting; `start` when it starts the host (with or without its `www.`) or a segment
 * of the path, never in the scheme, the query string or the fragment; else `anywhere`. Both folded already.
 */
export function urlRank(url: string, needle: string): SearchRank {
  const scheme = url.indexOf('://');
  const host = scheme >= 0 ? scheme + 3 : 0;
  const slash = url.indexOf('/', host);
  const path = slash >= 0 ? slash : url.length;
  const named = needle.replace(/\/+$/, '');
  const names = [url, url.slice(host), url.slice(path), path < url.length ? url.slice(path + 1) : ''];
  if (names.some((name) => name.replace(/\/+$/, '') === named)) return 'exact';
  const marks = [url.indexOf('?', path), url.indexOf('#', path)].filter((mark) => mark >= 0);
  const query = marks.length > 0 ? Math.min(...marks) : url.length;
  for (let at = url.indexOf(needle); at >= 0; at = url.indexOf(needle, at + 1)) {
    if (
      at === host ||
      (url.startsWith('www.', host) && at === host + 4) ||
      (at >= path && at < query && (needle.startsWith('/') || url[at - 1] === '/'))
    ) {
      return 'start';
    }
  }
  return 'anywhere';
}

/** How well a title matches: `start` at its start or after a character that isn't a letter or a digit. Both folded. */
export function titleRank(title: string, needle: string): SearchRank {
  for (let at = title.indexOf(needle); at >= 0; at = title.indexOf(needle, at + 1)) {
    if (at === 0 || !isWordCharacter(characterBefore(title, at))) return 'start';
  }
  return 'anywhere';
}

/** `POST /crawls/{id}/search`: the best few of the URLs whose URL or title holds the text, as the sidecar answers it. */
export function answerSearch(rows: readonly UrlRow[], body: unknown): Answer<SearchResults> {
  const { text, limit = SEARCH_DEFAULT_LIMIT } = (body ?? {}) as { text?: unknown; limit?: unknown };
  if (
    (text !== undefined && text !== null && typeof text !== 'string') ||
    (limit !== null && typeof limit !== 'number')
  ) {
    return { ok: false, problem: badRequest('text is a string, and limit a number.') };
  }
  const errors: Record<string, string[]> = {};
  if (typeof text !== 'string') errors.text = ['Give the text to search for.'];
  else if (text.length > SEARCH_MAX_TEXT)
    errors.text = [`Search for at most ${SEARCH_MAX_TEXT.toLocaleString('en')} characters.`];
  else if (text.includes('\0')) errors.text = ['The text can’t hold a zero character.'];
  if (typeof limit !== 'number' || !Number.isInteger(limit) || limit < 1 || limit > SEARCH_MAX_LIMIT) {
    errors.limit = [`Ask for 1 to ${SEARCH_MAX_LIMIT} results.`];
  }
  if (Object.keys(errors).length > 0 || typeof text !== 'string' || typeof limit !== 'number') {
    return { ok: false, problem: invalidQuery(errors) };
  }
  if (text === '') return { ok: true, page: { results: [], matching: rows.length, total: rows.length } };

  const needle = fold(text);
  const found: (SearchResult & { length: number })[] = [];
  for (const row of rows) {
    const url = row.url;
    if (url === undefined) continue;
    const title = row.title ?? null;
    const byUrl = fold(url).includes(needle) ? urlRank(fold(url), needle) : null;
    const byTitle = title !== null && fold(title).includes(needle) ? titleRank(fold(title), needle) : null;
    if (byUrl === null && byTitle === null) continue;
    const inUrl = byUrl !== null && (byTitle === null || RANKS.indexOf(byUrl) <= RANKS.indexOf(byTitle));
    found.push({
      id: row.id,
      url,
      title,
      isInternal: row.isInternal ?? true,
      statusCode: row.statusCode ?? null,
      outcome: row.outcome ?? null,
      rank: inUrl ? byUrl : (byTitle ?? 'anywhere'),
      matchedIn: inUrl ? 'url' : 'title',
      length: inUrl ? url.length : (title ?? '').length,
    });
  }
  found.sort(
    (left, right) =>
      RANKS.indexOf(left.rank) - RANKS.indexOf(right.rank) ||
      Number(!left.isInternal) - Number(!right.isInternal) ||
      Number(left.matchedIn === 'title') - Number(right.matchedIn === 'title') ||
      left.length - right.length ||
      left.id - right.id,
  );
  return {
    ok: true,
    page: {
      results: found.slice(0, limit).map(({ length: _length, ...result }) => result),
      matching: found.length,
      total: rows.length,
    },
  };
}

// --------------------------------------------------------------------------------------------------------- Link ends

/** The links a view of the links grid matches, every one, as the links grid answers them; or why it can't be run. */
function viewLinks(
  links: readonly LinkRow[],
  filters: readonly GridFilter[] | undefined,
  search: string | undefined,
  summary: SummaryState,
): Answer<LinkRow[]> {
  const matched: LinkRow[] = [];
  const byId = new Map(links.map((link) => [link.id, link]));
  for (let offset = 0; ; offset += 1_000) {
    const answer = answerLinkQuery(
      links,
      {
        columns: ['kind'],
        filters: [...(filters ?? [])],
        sort: [],
        offset,
        limit: 1_000,
        ...(search !== undefined ? { search } : {}),
      },
      summary,
    );
    if (!answer.ok) return answer;
    for (const row of answer.page.rows) {
      const link = byId.get(row.id);
      if (link) matched.push(link);
    }
    if (answer.page.rows.length < 1_000) return { ok: true, page: matched };
  }
}

const endOf = (link: LinkRow, end: LinkEnd) =>
  end === 'from' ? { id: link['from.id'], url: link['from.url'] } : { id: link['to.id'], url: link['to.url'] };

/** A view's ends, each URL once with its links, in the order the crawl found the URLs. */
function ends(links: readonly LinkRow[], end: LinkEnd): LinkEndRow[] {
  const counted = new Map<number, LinkEndRow>();
  for (const link of links) {
    const { id, url } = endOf(link, end);
    if (id === undefined || url === undefined) continue;
    const row = counted.get(id);
    if (row) row.links++;
    else counted.set(id, { id, url, links: 1 });
  }
  return [...counted.values()].sort((left, right) => left.id - right.id);
}

const END_MESSAGE = 'Give the end of the links: from (the pages they’re on) or to (the URLs they lead to).';

/** `POST /crawls/{id}/links/ends`: a page of a view's ends, as the sidecar answers it. */
export function answerLinkEnds(links: readonly LinkRow[], body: unknown, summary: SummaryState): Answer<LinkEndsPage> {
  const query = (body ?? {}) as Partial<{
    end: unknown;
    filters: GridFilter[];
    search: string;
    offset: number;
    limit: number;
  }>;
  if (query.end !== undefined && typeof query.end !== 'string')
    return { ok: false, problem: badRequest('end is a string.') };
  const offset = query.offset ?? 0;
  const limit = query.limit ?? 100;
  const errors: Record<string, string[]> = { ...pagingErrors(offset, limit) };
  if (query.end !== 'from' && query.end !== 'to') errors.end = [END_MESSAGE];
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const matched = viewLinks(links, query.filters, query.search, summary);
  if (!matched.ok) return matched;
  const end = query.end as LinkEnd;
  const all = ends(matched.page, end);
  return {
    ok: true,
    page: {
      end,
      total: all.length,
      links: matched.page.length,
      offset,
      rows: all.slice(offset, offset + limit),
      summary,
    },
  };
}

/**
 * The URL ids a URL grid's `linkEnds` narrows it to, or why it can't: the links grid's own errors, each keyed under
 * `linkEnds`. Null when the query has no `linkEnds`.
 */
export function linkEndIds(
  links: readonly LinkRow[],
  linkEnds: LinkEnds | undefined,
  summary: SummaryState,
): Answer<Set<number>> | null {
  if (linkEnds === undefined) return null;
  const errors: Record<string, string[]> = {};
  // The request is JSON, whatever the type says: an end may be anything.
  const end: unknown = linkEnds.end;
  if (end !== 'from' && end !== 'to') errors['linkEnds.end'] = [END_MESSAGE];
  const matched = viewLinks(links, linkEnds.filters, linkEnds.search, summary);
  if (!matched.ok) {
    if (matched.problem.type !== 'invalid-query') return matched;
    for (const [field, messages] of Object.entries(matched.problem.errors ?? {}))
      errors[`linkEnds.${field}`] = messages;
  }
  if (Object.keys(errors).length > 0 || !matched.ok) return { ok: false, problem: invalidQuery(errors) };
  return { ok: true, page: new Set(ends(matched.page, linkEnds.end).map((row) => row.id)) };
}

// -------------------------------------------------------------------------------------------- Analysis settings

const CHARACTERS: AnalysisLimitUnit = 'characters';
const MAX_CHARACTERS = 10_000;
const MAX_BYTES = 10 * 1024 * 1024 * 1024;

type LimitInfo = Omit<AnalysisLimit, 'value'>;

/** Every limit, in the order Settings lists them, as the sidecar describes them (Sidecar/Options/AnalysisLimits.cs). */
export const ANALYSIS_LIMITS: readonly LimitInfo[] = [
  {
    id: 'titleMinLength',
    default: 30,
    unit: CHARACTERS,
    min: 0,
    max: MAX_CHARACTERS,
    description: 'A title shorter than this is flagged as too short.',
  },
  {
    id: 'titleMaxLength',
    default: 60,
    unit: CHARACTERS,
    min: 1,
    max: MAX_CHARACTERS,
    description: 'A title longer than this is flagged as too long: search results cut it short.',
  },
  {
    id: 'descriptionMinLength',
    default: 70,
    unit: CHARACTERS,
    min: 0,
    max: MAX_CHARACTERS,
    description: 'A meta description shorter than this is flagged as too short.',
  },
  {
    id: 'descriptionMaxLength',
    default: 155,
    unit: CHARACTERS,
    min: 1,
    max: MAX_CHARACTERS,
    description: 'A meta description longer than this is flagged as too long: search results cut it short.',
  },
  {
    id: 'minWordCount',
    default: 200,
    unit: 'words',
    min: 0,
    max: 1_000_000,
    description: 'A page with fewer words of main content than this is flagged as thin.',
  },
  {
    id: 'duplicateMinWordCount',
    default: 50,
    unit: 'words',
    min: 0,
    max: 1_000_000,
    description: 'A page with fewer words of main content than this is left out of the duplicate content checks.',
  },
  {
    id: 'nearDuplicateMaxBits',
    default: 24,
    unit: 'bits',
    min: 0,
    max: 32,
    description:
      'How far apart two pages’ content fingerprints may be, of 256 bits, for the pages to count as near duplicates: 24 is about 96% the same, and fewer is stricter.',
  },
  {
    id: 'imageAltMaxLength',
    default: 125,
    unit: CHARACTERS,
    min: 1,
    max: MAX_CHARACTERS,
    description: 'An image’s alt text longer than this is flagged as too long.',
  },
  {
    id: 'largeImageBytes',
    default: 204_800,
    unit: 'bytes',
    min: 1,
    max: MAX_BYTES,
    description: 'An image larger than this is flagged as too large.',
  },
  {
    id: 'largePageBytes',
    default: 1_048_576,
    unit: 'bytes',
    min: 1,
    max: MAX_BYTES,
    description: 'A page whose HTML is larger than this is flagged as too large.',
  },
  {
    id: 'slowResponseMs',
    default: 1_000,
    unit: 'ms',
    min: 1,
    max: 3_600_000,
    description: 'A page that takes longer than this to answer is flagged as slow.',
  },
  {
    id: 'maxUrlLength',
    default: 115,
    unit: CHARACTERS,
    min: 1,
    max: MAX_CHARACTERS,
    description: 'A URL longer than this is flagged as too long.',
  },
];

const PAIRS: readonly [min: string, max: string, what: string][] = [
  ['titleMinLength', 'titleMaxLength', 'title'],
  ['descriptionMinLength', 'descriptionMaxLength', 'meta description'],
];

export interface SettingsAnswer {
  status: 200 | 400 | 422;
  body: AnalysisSettings | { errors: Record<string, string[]> } | ProblemBody;
}

/** The analysis's limits as the sidecar keeps them: in force, saved, checked and reset, held in memory here. */
export function createAnalysisSettings() {
  let saved: Record<string, number> = {};

  const answer = (): AnalysisSettings => {
    const limits = ANALYSIS_LIMITS.map((limit) => ({ ...limit, value: saved[limit.id] ?? limit.default }));
    return {
      settings: Object.fromEntries(limits.map((limit) => [limit.id, limit.value])),
      limits,
      isDefault: limits.every((limit) => limit.value === limit.default),
    };
  };

  /** The limits a body asks for, each checked by itself and the pairs together; or a body that isn't one. */
  const check = (body: unknown): { values: Record<string, number>; errors: Record<string, string[]> } | ProblemBody => {
    if (body === null || typeof body !== 'object' || Array.isArray(body)) {
      return badRequest('Send the limits as an object, each by its name.');
    }
    const values: Record<string, number> = {};
    const errors: Record<string, string[]> = {};
    for (const [name, value] of Object.entries(body)) {
      const limit = ANALYSIS_LIMITS.find((each) => each.id === name);
      if (!limit) return badRequest(`There’s no limit called '${name}'.`);
      if (value === null || value === undefined) continue;
      if (typeof value !== 'number' || !Number.isInteger(value)) errors[name] = ['This must be a whole number.'];
      else if (value < limit.min || value > limit.max) {
        errors[name] = [
          `This must be from ${limit.min.toLocaleString('en')} to ${limit.max.toLocaleString('en')} ${limit.unit}.`,
        ];
      } else values[name] = value;
    }
    const valueOf = (id: string) => values[id] ?? ANALYSIS_LIMITS.find((limit) => limit.id === id)?.default ?? 0;
    for (const [min, max, what] of PAIRS) {
      if (errors[min] === undefined && errors[max] === undefined && valueOf(max) <= valueOf(min)) {
        errors[max] = [
          `The longest ${what} allowed must be more than the shortest (${valueOf(min).toLocaleString('en')}).`,
        ];
      }
    }
    return { values, errors };
  };

  const isProblemBody = (value: unknown): value is ProblemBody => typeof (value as ProblemBody).type === 'string';

  const invalidSettings = (errors: Record<string, string[]>): ProblemBody => ({
    type: 'invalid-settings',
    title: 'These limits can’t be saved.',
    status: 422,
    detail: Object.values(errors).flat()[0] ?? '',
    errors,
  });

  return {
    get: (): SettingsAnswer => ({ status: 200, body: answer() }),
    /** Replaces the limits: one left out, or null, takes the engine's value. */
    put(body: unknown): SettingsAnswer {
      const checked = check(body);
      if (isProblemBody(checked)) return { status: 400, body: checked };
      if (Object.keys(checked.errors).length > 0) return { status: 422, body: invalidSettings(checked.errors) };
      saved = Object.fromEntries(
        Object.entries(checked.values).filter(
          ([id, value]) => ANALYSIS_LIMITS.find((limit) => limit.id === id)?.default !== value,
        ),
      );
      return { status: 200, body: answer() };
    },
    validate(body: unknown): SettingsAnswer {
      const checked = check(body);
      return isProblemBody(checked)
        ? { status: 400, body: checked }
        : { status: 200, body: { errors: checked.errors } };
    },
    /** Puts the named limits back (both of a pair a single reset would leave crossed); all of them when none is named. */
    reset(body: unknown): SettingsAnswer {
      const named = (body as { limits?: unknown } | undefined)?.limits;
      if (named !== undefined && named !== null && !Array.isArray(named))
        return { status: 400, body: badRequest('limits is a list of names.') };
      const list = (named ?? []) as unknown[];
      const errors: Record<string, string[]> = {};
      list.forEach((name, index) => {
        if (typeof name !== 'string' || !ANALYSIS_LIMITS.some((limit) => limit.id === name)) {
          errors[`limits[${index}]`] = [`There’s no limit called '${String(name)}'.`];
        }
      });
      if (Object.keys(errors).length > 0) return { status: 422, body: invalidSettings(errors) };
      if (list.length === 0) saved = {};
      else {
        const kept = new Map(Object.entries(saved).filter(([id]) => !list.includes(id)));
        for (const [min, max] of PAIRS) {
          const valueOf = (id: string) =>
            kept.get(id) ?? ANALYSIS_LIMITS.find((limit) => limit.id === id)?.default ?? 0;
          if (valueOf(max) <= valueOf(min)) {
            kept.delete(min);
            kept.delete(max);
          }
        }
        saved = Object.fromEntries(kept);
      }
      return { status: 200, body: answer() };
    },
  };
}

// ----------------------------------------------------------------------------------------------- robots.txt origins

/** `POST /crawls/{id}/robots` with E6's `search` and `blocked`: the origins they match, a page of them, and the counts. */
export function answerOrigins(saved: RobotsOriginPage, body: unknown): Answer<RobotsOriginPage> {
  const query = (body ?? {}) as RobotsOriginsQuery & Record<string, unknown>;
  if (
    (query.search !== undefined && typeof query.search !== 'string') ||
    (query.blocked !== undefined && typeof query.blocked !== 'boolean')
  ) {
    return { ok: false, problem: badRequest('search is a string, and blocked true or false.') };
  }
  const offset = query.offset ?? 0;
  const limit = query.limit ?? 100;
  const errors: Record<string, string[]> = { ...pagingErrors(offset, limit) };
  if ((query.search ?? '').length > SEARCH_MAX_TEXT)
    errors.search = [`Search for at most ${SEARCH_MAX_TEXT.toLocaleString('en')} characters.`];
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const text = (query.search ?? '').toLowerCase();
  const found = saved.origins.filter((origin) => origin.origin.toLowerCase().includes(text));
  const blocking = found.filter((origin) => origin.blocked > 0);
  const matching =
    query.blocked === undefined ? found : query.blocked ? blocking : found.filter((origin) => origin.blocked === 0);
  return {
    ok: true,
    page: {
      ...saved,
      total: saved.origins.length,
      matching: matching.length,
      counts: { all: found.length, blocked: blocking.length, notBlocked: found.length - blocking.length },
      offset,
      origins: matching.slice(offset, offset + limit),
    },
  };
}

// ------------------------------------------------------------------------------------------------------------ Icon

/** The synthetic site's icon, as a crawl of it keeps it: a 32 px PNG (ShoutingIguana.TestSite's /favicon.ico). */
export const SYNTHETIC_FAVICON = Uint8Array.from(
  atob(
    'iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAZ0lEQVR42mP48vEFTRHDCLDAqtYFPyLfAoJGE2MNA1VMx2MHA7VMx2UHAxVNx2oHA3VNx7SDgeqmo9lBRwuoaDqyHaMWjFowasHwsmDoF3b0qA/oUaPRo06mR6uCHu0ierTshkbjFwCU+d/JYZdKeQAAAABJRU5ErkJggg==',
  ),
  (character) => character.charCodeAt(0),
);

/** What `GET /crawls/{id}/favicon` answers: the icon a crawl kept, as an image, or nothing (204) for one that has none. */
export function faviconResponse(icon: Uint8Array<ArrayBuffer> | null): Response {
  return icon
    ? new Response(icon, { status: 200, headers: { 'Content-Type': 'image/png', 'X-Content-Type-Options': 'nosniff' } })
    : new Response(null, { status: 204 });
}

// ---------------------------------------------------------------------------------------------------------- Routes

/** What E6's routes need of the mock beyond the crawls: each crawl's links, and the icon it kept. */
export interface E6Crawls extends MockCrawls {
  /** The crawl's links as the links grid's rows, or null when the fixtures hold none of them. */
  links: (id: string) => Promise<LinkRow[] | null>;
  icon: (id: string) => Uint8Array<ArrayBuffer> | null;
}

export function e6Routes(mock: E6Crawls, settings = createAnalysisSettings()): MockRoute[] {
  const crawl = String.raw`^\/api\/crawls\/([0-9a-f]{16})`;
  const settingsResponse = (answer: SettingsAnswer) =>
    answer.status === 200 ? mock.json(answer.body) : mock.problem(answer.body as ProblemBody);
  const answered = <T>(answer: Answer<T>) => (answer.ok ? mock.json(answer.page) : mock.problem(answer.problem));
  const noLinks = (): ProblemBody => ({
    type: 'not-found',
    title: 'The design fixtures don’t hold this crawl’s links.',
    status: 404,
  });

  return [
    [
      'POST',
      new RegExp(`${crawl}/search$`),
      async ([, id = ''], request) =>
        (await mock.item(id))
          ? answered(answerSearch(await mock.rows(id), request.body))
          : mock.problem(mock.notFound(id)),
    ],
    [
      'POST',
      new RegExp(`${crawl}/links/ends$`),
      async ([, id = ''], request) => {
        const item = await mock.item(id);
        if (!item) return mock.problem(mock.notFound(id));
        const links = await mock.links(id);
        return links ? answered(answerLinkEnds(links, request.body, mock.summary(item))) : mock.problem(noLinks());
      },
    ],
    [
      'GET',
      new RegExp(`${crawl}/favicon$`),
      async ([, id = '']) => ((await mock.item(id)) ? faviconResponse(mock.icon(id)) : mock.problem(mock.notFound(id))),
    ],
    ['GET', /^\/api\/analysis-settings$/, () => Promise.resolve(settingsResponse(settings.get()))],
    [
      'PUT',
      /^\/api\/analysis-settings$/,
      (_, request) => Promise.resolve(settingsResponse(settings.put(request.body))),
    ],
    [
      'POST',
      /^\/api\/analysis-settings\/validate$/,
      (_, request) => Promise.resolve(settingsResponse(settings.validate(request.body))),
    ],
    [
      'POST',
      /^\/api\/analysis-settings\/reset$/,
      (_, request) => Promise.resolve(settingsResponse(settings.reset(request.body))),
    ],
  ];
}
