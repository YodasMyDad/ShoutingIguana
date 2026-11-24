import { describe, expect, it } from 'vitest';
import type { AlertEvent } from '../../api/types';
import {
  blockedMessage,
  inTheWay,
  minutesInWords,
  simplePhaseProgress,
  simplePhaseWords,
  pagesWhenRaised,
  sectionHeld,
  timeLeftWords,
} from './checkingWords';

const alert = (code: string, at: string, data: AlertEvent['data'] = null, id = 1): AlertEvent => ({
  crawlId: '0123456789abcdef',
  id,
  level: 'warning',
  code,
  message: `The engine's words for ${code}`,
  at,
  data,
});

const base = { waits: [], runStartedAt: null, site: 'example.com', pages: 312 };

describe('what’s in the way', () => {
  it('says each alert of the design’s table in plain words, with what to do', () => {
    const entries = inTheWay({
      ...base,
      alerts: [
        alert('blocked', '2026-11-01T10:00:00Z', { host: 'example.com', reason: 'Cloudflare challenge', count: 4 }, 1),
        alert('start-redirects-away', '2026-11-01T09:00:00Z', { url: 'https://www.example.org/' }, 2),
        alert('robots-unreachable', '2026-11-01T08:00:00Z', { origin: 'https://example.com' }, 3),
        alert('site-unreachable', '2026-11-01T07:00:00Z', { host: 'example.com' }, 4),
      ],
    });
    expect(entries.map((entry) => entry.text)).toEqual([
      'The site is turning the check away: its security takes it for a robot. 312 pages were read first.',
      'This address forwards to another site, example.org.',
      'The site’s rules file for search engines isn’t answering, so the check can’t tell where it may go. It’s waiting, and will try again.',
      'The site has stopped answering. The check is waiting, and will carry on by itself when it’s back.',
    ]);
    expect(entries[0]?.actions.map((action) => action.label)).toEqual([
      'Continue more gently (Polite), in Advanced',
      'What to tell whoever runs the site',
      'More ways in Advanced',
    ]);
    // Each lands where Advanced's alert does: the crawl's settings on its speed, then on its headers.
    expect(entries[0]?.actions[0]).toMatchObject({ kind: 'crawlSettings', focus: 'speed' });
    expect(entries[0]?.actions[2]).toMatchObject({ kind: 'crawlSettings', focus: 'headers' });
    expect(entries[1]?.actions).toEqual([
      { kind: 'newCrawl', label: 'Check example.org instead', url: 'https://www.example.org/' },
    ]);
    expect(entries[2]?.actions).toEqual([{ kind: 'stop', label: 'Stop' }]);
  });

  // A live check of the test site (2026-11-24): its note was read from the file and heard live, and was listed twice
  // under one key ("Encountered two children with the same key").
  it('says an alert once when it is given twice, a note too', () => {
    const note = alert('a-code-of-a-newer-engine', '2026-11-01T10:00:00Z', null, 2);
    const entries = inTheWay({ ...base, alerts: [note, { ...note }] });
    expect(entries.map((entry) => entry.text)).toEqual(["The engine's words for a-code-of-a-newer-engine"]);
    expect(new Set(entries.map((entry) => entry.key)).size).toBe(entries.length);
    // Two notes of one code raised at different times are still two.
    const later = alert('a-code-of-a-newer-engine', '2026-11-01T10:05:00Z', null, 3);
    expect(inTheWay({ ...base, alerts: [note, later] })).toHaveLength(2);
  });

  it('says in plain words that the browser for JavaScript pages couldn’t start, without the driver’s fault', () => {
    const entries = inTheWay({
      ...base,
      alerts: [
        {
          ...alert('javascript-rendering', '2026-11-01T10:00:00Z'),
          message:
            "JavaScript rendering couldn't start, so pages are read as they were sent. Driver not found: C:/app/node.exe",
        },
      ],
    });
    expect(entries.map((entry) => entry.text)).toEqual([
      'The browser that reads pages built by JavaScript couldn’t start, so every page is read as the site sent it. A page that fills itself in with a script may look empty. The reason is in Advanced.',
    ]);
  });

  it('says a Shopify shop turns away checks without a signature, in plain words, once', () => {
    const entries = inTheWay({
      ...base,
      alerts: [
        alert('shopify-unsigned', '2026-11-01T10:00:00Z', { host: 'shop.example', count: 1 }, 1),
        alert('shopify-unsigned', '2026-11-01T10:00:30Z', { host: 'shop.example', count: 4 }, 2),
      ],
    });

    expect(entries.map((entry) => entry.text)).toEqual([
      'This site runs on Shopify, and Shopify is turning some pages away because the check isn’t signed: 4 pages so far. They’ll be marked not checked, not broken.',
    ]);
    expect(entries[0]?.shopifySteps).toBe(true);
    expect(entries[0]?.actions).toEqual([
      { kind: 'shopifySign', label: 'Add a Shopify signature', host: 'shop.example' },
    ]);
    const expired = inTheWay({
      ...base,
      alerts: [
        alert('shopify-unsigned', '2026-11-01T10:00:00Z', {
          host: 'shop.example',
          count: 2,
          reason: 'the signature for shop.example expired on 1 March 2027',
        }),
      ],
    });
    expect(expired[0]?.text).toContain('The signature ended on 1 March 2027. Make a new one in Shopify.');

    // Turned away even with one: changed the same way, in Simple.
    const refused = inTheWay({
      ...base,
      alerts: [alert('shopify-signature-refused', '2026-11-01T10:00:00Z', { host: 'shop.example', count: 3 })],
    });
    expect(refused[0]?.actions).toEqual([{ kind: 'shopifySign', label: 'Change the signature', host: 'shop.example' }]);
  });

  it('says the slowdown and the Crawl-delay, from the waits, with nothing to do', () => {
    const entries = inTheWay({
      ...base,
      alerts: [],
      waits: [
        { reason: 'backoff', host: 'example.com', durationMs: 2000 },
        { reason: 'heldBack', host: 'example.com', durationMs: 2000 },
        { reason: 'crawlDelay', host: 'example.com', durationMs: 5000 },
        { reason: 'robotsTxt', host: 'cdn.example.net', urls: 4 },
        { reason: 'otherSitesRobotsTxt', urls: 12 },
      ],
    });
    expect(entries.map((entry) => [entry.text, entry.actions.length])).toEqual([
      ['The site asked the check to slow down, so it has. It will take longer.', 0],
      ['The site asks visitors like this one to go slowly, so the check waits between pages.', 0],
    ]);
  });

  it('says one thing once, whether an alert or a wait said it, and the newest blocked alert only', () => {
    const entries = inTheWay({
      ...base,
      alerts: [
        alert('site-unreachable', '2026-11-01T07:00:00Z', null, 1),
        alert('blocked', '2026-11-01T07:01:00Z', { host: 'a.example.com' }, 2),
        alert('mostly-forbidden', '2026-11-01T07:02:00Z', { host: 'b.example.com' }, 3),
      ],
      waits: [{ reason: 'siteDown', host: 'example.com', durationMs: 60_000 }],
    });
    expect(entries.map((entry) => entry.key.split(':')[0])).toEqual(['blocked', 'down']);
  });

  it('leaves out an earlier run’s alerts, by instant, not by text', () => {
    const entries = inTheWay({
      ...base,
      // "…:57Z" sorts after "…:57.5Z" as text, but is earlier.
      runStartedAt: '2026-11-01T10:00:57.5Z',
      alerts: [
        alert('robots-unreachable', '2026-11-01T10:00:57Z', null, 1),
        alert('site-unreachable', '2026-11-01T10:00:58Z', null, 2),
      ],
    });
    expect(entries.map((entry) => entry.key.split(':')[0])).toEqual(['down']);
  });

  it('leaves out good news, and says a newer engine’s note in its own words', () => {
    const entries = inTheWay({
      ...base,
      alerts: [alert('sitemaps', '2026-11-01T07:00:00Z', null, 1), alert('new-thing', '2026-11-01T07:01:00Z', null, 2)],
    });
    expect(entries.map((entry) => entry.text)).toEqual(["The engine's words for new-thing"]);
  });

  // Views 06 and 07 of gallery 7: the engine's notice that a section is limited, and the wait it holds, said it twice.
  it('says a section the site limits once, in plain words, whether its notice or its wait says so', () => {
    const entries = inTheWay({
      ...base,
      alerts: [alert('section-rate-limited', '2026-11-01T07:00:00Z', { host: 'example.com', url: '/p/' }, 1)],
      waits: [{ reason: 'sectionLimited', host: 'example.com', urls: 1041, durationMs: 4000, section: '/p/' }],
    });
    expect(entries.map((entry) => [entry.text, entry.actions.length])).toEqual([
      [
        'The site only lets some of its pages be read slowly, so the check reads those one at a time. The rest isn’t held up.',
        0,
      ],
    ]);
  });

  it('says the slowdown beside a section the site limits: they are two things', () => {
    const entries = inTheWay({
      ...base,
      alerts: [],
      waits: [
        { reason: 'sectionLimited', host: 'example.com', urls: 1041, durationMs: 4000, section: '/p/' },
        { reason: 'backoff', host: 'example.com', durationMs: 2000 },
      ],
    });
    expect(entries.map((entry) => entry.text)).toEqual([
      'The site only lets some of its pages be read slowly, so the check reads those one at a time. The rest isn’t held up.',
      'The site asked the check to slow down, so it has. It will take longer.',
    ]);
  });

  it('writes a message to send on, with the pages read', () => {
    expect(blockedMessage('example.com', 312)).toContain('after 312 pages');
    expect(blockedMessage('example.com', null)).not.toContain('after');
  });
});

describe('how long is left', () => {
  it('is a floor: what is still to check over the current pace, in whole minutes down', () => {
    // 1,417 still to check at 2 a second: 11.8 minutes, so "at least 11".
    expect(timeLeftWords({ status: 'running', queued: 1417, urlsPerSecond: 2, atLimit: false })).toEqual({
      lead: 'At least 11 minutes to go.',
      rest: 'It’s still finding new pages, so this can grow. It’s reading about 2 addresses a second.',
    });
    expect(timeLeftWords({ status: 'running', queued: 90_000, urlsPerSecond: 12, atLimit: false }).lead).toBe(
      'At least 2 hours and 5 minutes to go.',
    );
    expect(timeLeftWords({ status: 'running', queued: 50, urlsPerSecond: 5, atLimit: false }).lead).toBe(
      'Under a minute for what it has found so far.',
    );
  });

  it('says the limit is what’s left when it’s nearer than the end of the queue, as "about"', () => {
    // 5,933 queued, but the limit lets it read 1,141 more: 1,141 at 16 a second is 1.2 minutes.
    expect(
      timeLeftWords({
        status: 'running',
        queued: 5933,
        urlsPerSecond: 16,
        atLimit: false,
        limit: 1500,
        limitLeft: 1141,
        internalFound: 1600,
      }),
    ).toEqual({
      lead: 'About 1 minute to go.',
      rest: 'It stops at its limit of 1,500 addresses. It’s reading about 16 addresses a second.',
    });
    expect(
      timeLeftWords({ status: 'running', queued: 100, urlsPerSecond: 1, atLimit: false, limit: 1500, limitLeft: 1141 })
        .lead,
    ).toBe('At least 1 minute to go.');
  });

  it('says "about" and the limit only once the site has more addresses of its own than the limit, as Advanced does', () => {
    // 5,933 queued, most of them other sites' (links out): the site's own found so far, 1,200, is under the limit
    // of 1,500, so the limit isn't what's left, and the queue's floor stands.
    const words = timeLeftWords({
      status: 'running',
      queued: 5933,
      urlsPerSecond: 16,
      atLimit: false,
      limit: 1500,
      limitLeft: 1141,
      internalFound: 1200,
    });
    expect(words.lead).toBe('At least 6 minutes to go.');
    expect(words.rest).not.toMatch(/limit/);
  });

  it('has its own sentence for each state that has no pace', () => {
    expect(timeLeftWords({ status: 'running', queued: 10, urlsPerSecond: 0, atLimit: false }).lead).toBe(
      'Working out how long it will take.',
    );
    expect(timeLeftWords({ status: 'paused', queued: 10, urlsPerSecond: 3, atLimit: false }).lead).toBe('Paused.');
    expect(timeLeftWords({ status: 'stopping', queued: 10, urlsPerSecond: 3, atLimit: false }).lead).toBe('Stopping.');
    expect(timeLeftWords({ status: 'running', queued: 10, urlsPerSecond: 3, atLimit: true }).lead).toBe('Nearly done.');
    expect(timeLeftWords({ status: 'running', queued: 0, urlsPerSecond: 3, atLimit: false }).lead).toBe('Nearly done.');
  });

  // Views 06 and 31 of gallery 7: "Nearly done" while 1,041 of a section the site limits were still to read.
  it('never says "Nearly done" while a section the site limits still has addresses waiting', () => {
    const lastOnes = {
      lead: 'Reading the last pages slowly.',
      rest: '1,041 addresses are left. The site only lets them be read one at a time, so this can take a while.',
    };
    expect(
      timeLeftWords({ status: 'running', queued: 1041, urlsPerSecond: 0.2, atLimit: false, heldInSections: 1041 }),
    ).toEqual(lastOnes);
    // Its limit reached for the rest, or no pace to go by: still the last ones, read slowly.
    expect(
      timeLeftWords({ status: 'running', queued: 1041, urlsPerSecond: 0.2, atLimit: true, heldInSections: 1041 }),
    ).toEqual(lastOnes);
    expect(
      timeLeftWords({ status: 'running', queued: 1041, urlsPerSecond: 0, atLimit: false, heldInSections: 1041 }),
    ).toEqual(lastOnes);
    expect(
      timeLeftWords({ status: 'running', queued: 1, urlsPerSecond: 0.2, atLimit: false, heldInSections: 1 }).rest,
    ).toBe('1 address is left. The site only lets it be read slowly, so this can take a while.');
    // With more of the site to read besides, the floor stands, the section's addresses in it.
    expect(
      timeLeftWords({ status: 'running', queued: 2041, urlsPerSecond: 2, atLimit: false, heldInSections: 1041 }).lead,
    ).toBe('At least 17 minutes to go.');
  });

  it('counts the addresses waiting in the sections the site limits, from the progress’s waits', () => {
    expect(
      sectionHeld(
        [
          { reason: 'sectionLimited', host: 'example.com', urls: 1041, section: '/p/' },
          { reason: 'backoff', host: 'example.com', durationMs: 2000 },
          { reason: 'sectionLimited', host: 'example.com', urls: 9, section: '/c/' },
        ],
        {},
      ),
    ).toBe(1050);
    // Sections beyond those listed hold one address each at least.
    expect(sectionHeld([{ reason: 'sectionLimited', urls: 4 }], { sectionLimited: 3, backoff: 2 })).toBe(7);
    expect(sectionHeld([], {})).toBe(0);
  });

  it('says hours and minutes in words', () => {
    expect([1, 12, 60, 61, 125].map(minutesInWords)).toEqual([
      '1 minute',
      '12 minutes',
      '1 hour',
      '1 hour and 1 minute',
      '2 hours and 5 minutes',
    ]);
  });
});

describe('working out the results', () => {
  it('says the phase and its progress in Simple’s words', () => {
    expect(simplePhaseWords('pageRules')).toBe('Checking each page');
    expect(simplePhaseWords('somethingNew')).toBe('Working out the results');
    expect(simplePhaseProgress({ phase: 'reading', done: 1204, total: 4521 })).toBe('1,204 of 4,521 addresses');
    expect(simplePhaseProgress({ phase: 'writing', done: 0, total: 1 })).toBeNull();
  });
});

describe('the pages read when an alert was raised', () => {
  const point = (at: string, htmlPages: number) =>
    ({ at, htmlPages, fetched: 0, internalFetched: 0, internalFound: 0, errors: 0, blocked: 0 }) as const;
  const points = [
    point('2026-11-01T10:00:00Z', 100),
    point('2026-11-01T10:10:00Z', 250),
    point('2026-11-01T10:20:00Z', 400),
  ];

  it('is the history’s last point at or before it', () => {
    expect(pagesWhenRaised(points, '2026-11-01T10:15:00Z')).toBe(250);
    expect(pagesWhenRaised(points, '2026-11-01T10:10:00.000Z')).toBe(250);
  });

  it('is unknown where the history doesn’t reach that moment', () => {
    expect(pagesWhenRaised(points, '2026-11-01T10:25:00Z')).toBeNull();
    expect(pagesWhenRaised(points, '2026-11-01T09:00:00Z')).toBeNull();
    expect(pagesWhenRaised(undefined, '2026-11-01T10:15:00Z')).toBeNull();
  });
});
