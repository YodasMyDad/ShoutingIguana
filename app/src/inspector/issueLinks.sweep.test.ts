// @vitest-environment node
// Every issue link the inspector's fallback builds (an engine that doesn't send `issues[].group`), on real crawls,
// against the groups the sidecar really had for each check and the URLs in them: a link never opens a group the
// check doesn't have, nor one without the URL. The fixture is made by scripts/issue-links-fixture.mjs: the synthetic
// planted crawl, and a real crawl as an older engine analysed it and analysed again.
import { describe, expect, it } from 'vitest';
import type { CheckGrouping } from '../api/grids';
import type { UrlDetail } from '../api/inspector';
import fixture from './issueLinks.fixture.json';
import { CLUSTER_GROUPED_CHECKS, issueLink } from './issueLinks';

interface FixtureCrawl {
  name: string;
  summary: UrlDetail['summary'];
  checks: Record<
    string,
    { grouping: CheckGrouping; groups: { detail: string | null; relatedUrlId: number | null; urls: number[] }[] }
  >;
  /** URLs with the same issues, named by the same checks. */
  urls: {
    ids: number[];
    namedBy: string[];
    issues: { checkId: string; detail: string | null; relatedId: number | null }[];
  }[];
}

const crawls = (fixture as unknown as { crawls: FixtureCrawl[] }).crawls;

/** Each link the fallback builds on the crawl: the issue's check, the URL it's on, and the group the link opens. */
function linksOf(crawl: FixtureCrawl) {
  return crawl.urls.flatMap((entry) =>
    entry.ids.flatMap((id) =>
      entry.issues.map((issue) => {
        const link = issueLink(
          {
            checkId: issue.checkId,
            severity: 'notice',
            detail: issue.detail,
            related: issue.relatedId === null ? null : { id: issue.relatedId, url: '' },
          },
          crawl.checks[issue.checkId]?.grouping,
          {
            id,
            namedBy: {
              total: entry.namedBy.length,
              checks: entry.namedBy.map((checkId) => ({ checkId, count: 1 })),
            },
            summary: crawl.summary,
          },
        );
        return { id, checkId: issue.checkId, group: link.group };
      }),
    ),
  );
}

describe.each(crawls)('the issue links of the $name crawl, without the engine’s group', (crawl) => {
  it('open only groups the check has, with the URL in them', () => {
    const links = linksOf(crawl).filter((link) => link.group !== undefined);
    const wrong = links.flatMap(({ id, checkId, group }) => {
      const found = crawl.checks[checkId]?.groups.find(
        (candidate) => candidate.detail === group?.detail && candidate.relatedUrlId === group.relatedUrlId,
      );
      if (!found) return [`#${id} ${checkId}: no group ${JSON.stringify(group)}`];
      return found.urls.includes(id) ? [] : [`#${id} ${checkId}: ${JSON.stringify(group)} doesn’t hold the URL`];
    });
    expect(wrong).toEqual([]);
    // Most issues of a check that groups do get their group: the sweep isn't passing by linking nowhere.
    expect(links.length).toBeGreaterThan(linksOf(crawl).length / 2);
  });

  it.runIf(crawl.summary.available)('find the cluster of every duplicate this engine grouped', () => {
    const duplicates = linksOf(crawl).filter((link) => CLUSTER_GROUPED_CHECKS.includes(link.checkId));
    expect(duplicates.length).toBeGreaterThan(0);
    expect(duplicates.filter((link) => link.group === undefined)).toEqual([]);
  });
});
