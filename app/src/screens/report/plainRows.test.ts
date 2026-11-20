// The report as Simple shows it says what Simple's screens say (the UX audit's S-11): the engine writes the report, so it
// mirrors the app's plain sentences (engine: Reporting/ReportPlainWords.cs, ReportPlainDetails.cs). The two are held
// together by one file of cases, engine/tests/ShoutingIguana.Engine.Tests/Reporting/PlainRows/plain-rows.json: each
// case is what goes in and what the app says. This test holds the file to the app; the engine's
// PlainRowsTests holds the engine to the file. So a sentence changed in app/src/simple fails here first.
//
// To take a changed sentence into the file, from app/:
//   SI_WRITE_PLAIN_ROWS=1 npx vitest run src/screens/report/plainRows.test.ts --project app
// then run the engine's PlainRowsTests, which name each sentence its mirror must now change.
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import type { IndexabilityReason, RobotsRule, UrlOutcome } from '../../api/grids';
import type { LinkPosition, RedirectChain } from '../../api/inspector';
import {
  plainFailureWords,
  plainFetchError,
  plainFileKind,
  plainLength,
  plainSeconds,
} from '../../simple/plainAnswers';
import { plainDetailWords } from '../../simple/plainDetails';
import {
  plainAlt,
  plainEvidence,
  plainImageFile,
  plainNotInSearch,
  plainOthers,
  plainPosition,
  plainRedirectKind,
  plainShownAt,
  plainStatus,
} from '../../simple/plainEvidence';

const FILE = path.join(
  __dirname,
  '..',
  '..',
  '..',
  '..',
  'engine',
  'tests',
  'ShoutingIguana.Engine.Tests',
  'Reporting',
  'PlainRows',
  'plain-rows.json',
);

type Said = string | null;

interface Cases {
  about: string;
  details: { check: string; detail: string; said: Said }[];
  evidence: { column: string; value: string | number; origin: string | null; url: string | null; said: Said }[];
  chains: {
    hops: { url: string; statusCode: number | null; outcome: UrlOutcome | null }[];
    loop: boolean;
    tooLong: boolean;
    origin: string | null;
    said: Said;
  }[];
  rules: { reason: RobotsRule['reason']; rule: string | null; line: number | null; said: Said }[];
  statuses: { statusCode: number | null; outcome: UrlOutcome | null; error: string | null; said: Said }[];
  positions: { position: LinkPosition | null; said: Said }[];
  notInSearch: { reason: IndexabilityReason | null; said: Said }[];
  redirectKinds: { kinds: string; said: Said }[];
  fileKinds: { contentType: string; said: Said }[];
  seconds: { ms: number; said: Said }[];
  lengths: { seconds: number; said: Said }[];
  failures: { error: string; words: Said; sentence: Said }[];
  imageFiles: { sizeBytes: number | null; pixelWidth: number | null; pixelHeight: number | null; said: Said }[];
  shownAt: { width: string | null; height: string | null; said: Said }[];
  alts: { alt: string | null; hasAlt: boolean; altLength: number | null; withLength: boolean; said: Said }[];
  others: { check: string; count: number; said: Said }[];
}

/** The file's cases, each with what the app says of it now. */
function saidByTheApp(cases: Cases): Cases {
  return {
    about: cases.about,
    details: cases.details.map((each) => ({ ...each, said: plainDetailWords(each.check, each.detail) })),
    evidence: cases.evidence.map((each) => ({
      ...each,
      said: plainEvidence(each.column, each.value, each.origin, each.url),
    })),
    chains: cases.chains.map((each) => {
      const hops = each.hops.map((hop, index) => ({ id: index + 1, ...hop, via: 'http' as const }));
      const last = hops.at(-1);
      const chain = {
        hops,
        final: { id: last?.id ?? 0, url: last?.url ?? '', statusCode: last?.statusCode ?? null },
        loop: each.loop,
        tooLong: each.tooLong,
      } as unknown as RedirectChain;
      return { ...each, said: plainEvidence('redirectChain', chain, each.origin) };
    }),
    rules: cases.rules.map((each) => ({
      ...each,
      said: plainEvidence('robotsRule', { origin: null, reason: each.reason, rule: each.rule, line: each.line }, null),
    })),
    statuses: cases.statuses.map((each) => ({ ...each, said: plainStatus(each.statusCode, each.outcome, each.error) })),
    positions: cases.positions.map((each) => ({ ...each, said: plainPosition(each.position) })),
    notInSearch: cases.notInSearch.map((each) => ({ ...each, said: plainNotInSearch(each.reason) })),
    redirectKinds: cases.redirectKinds.map((each) => ({ ...each, said: plainRedirectKind(each.kinds) })),
    fileKinds: cases.fileKinds.map((each) => ({ ...each, said: plainFileKind(each.contentType) })),
    seconds: cases.seconds.map((each) => ({ ...each, said: plainSeconds(each.ms) })),
    lengths: cases.lengths.map((each) => ({ ...each, said: plainLength(each.seconds) })),
    failures: cases.failures.map((each) => ({
      ...each,
      words: plainFailureWords(each.error),
      sentence: plainFetchError(each.error),
    })),
    imageFiles: cases.imageFiles.map((each) => ({ ...each, said: plainImageFile(each) })),
    shownAt: cases.shownAt.map((each) => ({ ...each, said: plainShownAt(each) })),
    alts: cases.alts.map((each) => ({ ...each, said: plainAlt(each, each.withLength) })),
    others: cases.others.map((each) => ({ ...each, said: plainOthers({ id: each.check }, each.count) })),
  };
}

describe('the plain sentences the engine’s report mirrors', () => {
  const cases = JSON.parse(readFileSync(FILE, 'utf8')) as Cases;
  const now = saidByTheApp(cases);

  if (process.env.SI_WRITE_PLAIN_ROWS === '1') {
    it('writes what the app says now into the cases', () => {
      writeFileSync(FILE, `${JSON.stringify(now, null, 2)}\n`);
      expect(JSON.parse(readFileSync(FILE, 'utf8'))).toEqual(now);
    });
    return;
  }

  // Each kind apart, so a failure names the sentence that moved.
  for (const kind of Object.keys(now).filter((key) => key !== 'about') as Exclude<keyof Cases, 'about'>[]) {
    it(`says each of the file’s ${kind} as the file has them`, () => {
      expect(cases[kind].length).toBeGreaterThan(0);
      expect(now[kind]).toEqual(cases[kind]);
    });
  }

  it('says every detail the file holds in plain words, or says why not', () => {
    // A detail the app has no plain words for is in the file on purpose (the engine says its own text there too):
    // most are said.
    const said = cases.details.filter((each) => each.said !== null).length;
    expect(said / cases.details.length).toBeGreaterThan(0.9);
  });
});
