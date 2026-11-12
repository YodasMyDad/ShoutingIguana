# Sitemaps and robots.txt: review findings (saved by the orchestrator from the reviewer's report, 2026-11-11)

Reviewed: `ui/sitemaps-robots` at `8d77343` (`git diff 17b340d..8d77343`). 17 findings: 2 high, 7 medium, 8 low.
Checked against the real sidecar: the garden crawl, the 100k crawl, and three crawls of the synthetic test site the
reviewer made (full, stopped between the index and its sitemaps, robots.txt ignored), kept in `crawls\` beside this
file. Proof tests (`review-proofs.test.tsx`, `review-live.test.tsx`), scripts, logs (`checks-*.log`,
`live-watch.log`) and screenshots (`shots\`) are beside this file too. Paths are under `app/src/` unless they start
with `engine` or `app/e2e`. CONFIRMED = shown by a test, the running app or the API. PLAUSIBLE = reasoned from code.

## Findings, most severe first

**1. HIGH. Pages the engine can't place are told "in a sitemap". CONFIRMED**
- Where: `screens/sitemaps/sitemapWords.ts:198-201` (`coverageOf`), shown by `SitemapList.tsx:178`.
- Defect: only the `inSitemap: false` bucket is read, and a missing bucket counts as 0. The engine answers `null`,
  never `false`, when the analysis skipped the not-in-sitemap check (`UrlSummaryBuilder.cs:124`).
- Scenario (real): the stopped test-site crawl, analysed, says "The site's 1 page is in a sitemap." under a sitemap
  marked "Never read". With one sitemap read and one failed on a 503 it would say "All of the site's 6,172 pages
  are in a sitemap."
- Proof: `shots\stopped-list-analysed.png`; `review-proofs.test.tsx` F0. The sweep shares the blind spot
  (`app/e2e/sitemaps-robots.sweep.spec.ts:176`).
- Fix: when the `null` bucket has a count, return `known: false` with the analysis's skip reason for
  `not-in-sitemap`.

**2. HIGH. "Crawled" counts URLs robots.txt refused and URLs that never answered. CONFIRMED**
- Where: `screens/sitemaps/sitemapLinks.ts:13-14,21-22`; `screens/sitemaps/urlOutcome.ts:12`.
- Defect: the column and the "Not crawled" filter go by `fetched`, which is true for `robotsDisallowed`, `timeout`,
  `networkError`, `blocked` and `processingError` (`UrlColumns.cs:22,94-97`). The same grid's Outcome cell says
  "Crawled" only for `success`.
- Scenario: a sitemap lists 10 URLs robots.txt disallows. The list says Crawled 10, Not crawled 0, and the link
  opens ten rows reading "Blocked by robots.txt".
- Proof: `review-proofs.test.tsx` F1; the garden's two `robotsDisallowed` URLs come back `fetched: true`. Neither
  test crawl has a listed URL that is blocked, so the list itself wasn't seen doing it.
- Fix: see "Verdict on Crawled" and the orchestrator's decision in the fixer's brief.

**3. MEDIUM. During a crawl the cursor in the origins list and the sitemap tree is a row number. CONFIRMED**
- Where: `screens/robots/RobotsOrigins.tsx:173-190`; `screens/sitemaps/SitemapTreeGrid.tsx:189-208`.
- Scenario: cursor on `zebra.example`; the crawl asks `apple.example`, which sorts ahead. After the refresh the
  cursor is on `apple`, and Enter opens the wrong file. The test site has three origins, so the build's live watch
  couldn't see it.
- Proof: `review-live.test.tsx` F9 (a component test on the origins list; the tree follows from the same code).
- Fix: re-anchor the active row by identity (a row key on `GridCore`) across a refresh.

**4. MEDIUM. The garden contradiction. CONFIRMED (cause), PLAUSIBLE (today's engine)**
- Where: `screens/robots/robotsWords.ts:59-64`; `RobotsFileView.tsx:195,578-579`;
  `engine/.../Storage/RobotsReader.cs:103,232-242`.
- Cause: the crawl ran 14:13-14:51 on 31 Oct on the first engine (`3e556be`). `111c7f3` ("give dead sites no
  verdict") landed at 15:49. Before it, no answer meant a full disallow, stored without an error. Today's reader
  labels that origin `unanswered`, and `Why()` only falls back to `unreachable` when the state is `Unreachable`, so
  the URL comes out `unknown`.
- Today's engine: not in one run (`RobotsCache.cs:160-163`). A continued crawl can by the contract: run 1 reads a
  file that refuses a URL, run 2 gets no answer, and only the last answer is kept. Not reproduced; the test site's
  robots.txt can't change between runs.
- What the screen shows: the origins row "No answer: crawled anyway" beside Blocked URLs 1. The file page says "The
  crawl gave no verdict and went ahead: this origin's URLs show their own errors." above "1 URL is stored as
  refused, though nothing kept here refuses it". The inspector's "See the robots.txt that blocked it" leads there.
- Proof: `shots\garden-copperfield.png`; `review-proofs.test.tsx` F2.
- Fix: UI: a state that refuses nothing, with `blocked > 0`, says the URLs were refused earlier. Engine: no status,
  no file and refused URLs without an error should read `unreachable` (engine part goes to the engine agent).

**5. MEDIUM. A crawl told to ignore robots.txt still reads "obeyed its rules". CONFIRMED**
- Where: `screens/robots/robotsWords.ts:45`; `RobotsFileView.tsx:195,367,575`; `RobotsOrigins.tsx:102`.
- Scenario (real crawl with `respectRobotsTxt: false`): "The crawl read this file and obeyed its rules for
  ShoutingIguana." sits directly above the ignore banner, followed by "the crawl obeyed lines 1 to 2".
- Proof: `shots\ignored-file.png`; `review-proofs.test.tsx` F4.
- Fix: pass `respected` into `stateWords`, `obeyedWords` and `nothingBlockedWords`.

**6. MEDIUM. An index that was read shows 0 for every count while its sitemaps weren't read. CONFIRMED**
- Where: `screens/sitemaps/sitemapTree.ts:70`; `SitemapTreeGrid.tsx:96-99,132`; `SitemapDetail.tsx:452-458,521`.
- Scenario (real, the stopped crawl): the index row reads 0 / 0 / 0 / 0, "Index of 1" and "In the file: 1 sitemap"
  (the file lists 3). Its page says "This sitemap lists no URLs."
- Proof: `shots\stopped-list.png`, `shots\stopped-index.png`; `review-proofs.test.tsx` F6.
- Fix: an index's counts are known only when its whole subtree is; otherwise a dash, and "the crawl has 1 of its
  sitemaps".

**7. MEDIUM. The list jumps and a status banner is announced while a crawl runs. CONFIRMED**
- Where: `screens/sitemaps/SitemapList.tsx:163-176`; `RobotsFileView.tsx:487-496`.
- Scenario (real crawl): the tree's top moves from 156 to 208 px when the "crawl's own judgement" banner mounts with
  `role="status"`. The blocked list drops 12 px when its note wraps.
- Proof: `live-watch.log`.
- Fix: keep the banner's room from the start and give it `role="none"`; fix the note's height.

**8. MEDIUM. Enter in one sitemap's search throws focus to the page title. CONFIRMED**
- Where: `screens/sitemaps/SitemapDetail.tsx:353-367` (`key={q ?? ''}` remounts the field).
- Proof: `checks-4.log`.
- Fix: don't key the field on `q`.

**9. MEDIUM. A 404 at the default location reads as a sitemap found and unreadable. CONFIRMED**
- Where: `screens/sitemaps/sitemapWords.ts:142-153`.
- Scenario (garden): "The crawl found 1 sitemap, which couldn't be read: HTTP 404."
- Proof: `shots\garden-sitemaps-outofdate.png`; `review-proofs.test.tsx` F3.
- Fix: for `defaultLocation` with a 4xx, say "Nothing declared a sitemap, and there's none at /sitemap.xml."

**10. LOW. Ctrl+F on an origin's page works only with focus already in the file. CONFIRMED**
- Where: `inspector/SourcePane.tsx:128-136`; `RobotsFileView.tsx:125-159`.
- Scenario: from the blocked list or the title the key does nothing, and the palette has no "Search the file".
- Proof: `checks-1.log`, `checks-3.log`.
- Fix: register a `mod+f` command on the view.

**11. LOW. A sitemap left unread says "Not read:" twice; the fixtures use an error the engine never sends.
CONFIRMED (unit test on the engine's string; no real crawl has a `notRead` sitemap)**
- Where: `screens/sitemaps/sitemapWords.ts:59,84`; `sitemapWords.test.ts:55`; `SitemapsScreen.stories.tsx:297`.
- Defect: the engine's error is `Not read: the crawler reads at most 1,000 sitemaps` (`SitemapDiscoverer.cs:130`).
- Proof: `review-proofs.test.tsx` F8.
- Fix: strip the prefix and use the real string in the fixtures.

**12. LOW. The engine's raw failure text sits in the main sentence. CONFIRMED**
- Where: `screens/robots/robotsWords.ts:22-25,33,63`.
- Scenario (real): "robots.txt didn't answer (TLS handshake failed: Cannot determine the frame size or a corrupted
  frame was received.). The crawl..."
- Proof: `checks-1.log`.
- Fix: plain words per kind of failure, the engine's text in the tooltip.

**13. LOW. An origin spelled another way is "one the crawl never asked". CONFIRMED (upper case, trailing slash);
PLAUSIBLE (Unicode IDN)**
- Where: `screens/robots/robotsFile.ts:154-156`; `api/errors.ts:43-46`.
- Scenario: an upper-case origin or one with a trailing slash gets the stale-place page. A Unicode domain would
  too, since the engine stores punycode.
- Proof: `checks-1.log`.
- Fix: `new URL(param).origin` when it parses.

**14. LOW. The palette offers commands a stale place can't run. CONFIRMED**
- Where: `screens/sitemaps/SitemapDetail.tsx:167-207` (registered before the stale return at `:241`);
  `RobotsFileView.tsx:145-149`.
- Proof: `checks-3.log`.
- Fix: `enabled: () => !stale`.

**15. LOW. An empty `Sitemap:` line is told "its value isn't a number". CONFIRMED**
- Where: `screens/robots/robotsWords.ts:144`; `engine/.../RobotsTxtParser.cs:105-106`. A negative `Crawl-delay`
  gets the same words (by reading the parser).
- Proof: `review-proofs.test.tsx` F5.
- Fix: word it by the line's kind.

**16. LOW. A place a running crawl hasn't reached yet is "from before the site was crawled again". CONFIRMED (code)**
- Where: `api/errors.ts:38-47`; `SitemapDetail.tsx:237-258`; `RobotsFileView.tsx:175-192`. Its reads are refreshed
  with the crawl, so it should fill in once the crawl gets there; only the words are wrong.
- Fix: a "not found yet" sentence while the crawl runs.

**17. LOW. `?line=` on a non-rule or missing line is called a rule that blocked nothing. CONFIRMED**
- Where: `screens/robots/robotsFile.ts:132-136`; `RobotsFileView.tsx:549-552`.
- Scenario: "Rule is line 1: User-agent: *"; "Line 999 blocked no URLs." in a 4-line file. Nothing breaks.
- Proof: `checks-1.log`.
- Fix: label the chip "Line"; say "This file has no line 999".

## Verdict on "Crawled" (the reviewer's)

The number is consistent with the rest of the app, but the words are not honest on this screen. `fetched` means
"the crawl is done with it", the URL grid already labels that filter "Crawled", and E3c defines `crawledUrlCount`
the same way. On a sitemap screen, though, "listed but blocked" is what people come for, and the slice's own
Outcome column uses "Crawled" for `success` only.

- **Honest column:** `fetched` less `robotsDisallowed`, i.e. `outcome` in `success`, `redirect`, `clientError`,
  `serverError`, `blocked`, `timeout`, `networkError`, `processingError`. "Not crawled" is then listed less that. A
  stricter "Answered" (the first four) is also defensible.
- **Can the UI compute it today?** On one sitemap's page, yes: one extra `/sitemaps/urls` read with an `outcome`
  filter and `limit: 1`. For the list, no: it would be one read per sitemap per count, each second while live. The
  engine should add `urls.robotsDisallowed` to `/sitemaps`.
- **Until then:** one meaning per word. Either rename the column ("Reached"), or keep "Crawled" and have the
  Outcome cell say "Answered" as the inspector does. Add an uncounted "Blocked by robots.txt" quick filter.

## Looks right

- **Numbers:** every count lands on itself at the API on the garden (43 checks), 100k (78) and full test-site (38)
  crawls, including the origins' order and lines plus unmatched adding up. In the app the Crawled and Non-indexable
  links and the no-sitemap sentence were followed (28 lands on 28).
- **One sitemap's grid live** (unverified by the build): the header and chosen filter moved together, rows left
  "Not crawled", the cursor and scroll stayed, no skeleton appeared, and it made at most 2 reads a second.
- **robots.txt truthfulness:** the app parses and matches nothing itself, and its line split matches the engine's
  by reading both (LF, CR, CRLF, BOM). `?line=999`, `?line=0` and junk params are safe; reload, Enter on a rule, the
  cursor marking its line and copy all work.
- **Shared pieces:** Issues tree Enter and URL grid Enter are unchanged. No pre-existing test was changed apart
  from `overview.spec.ts` moving its axe helper. 465 unit tests pass.
- **Accessibility and performance:** axe is clean on 9 real-data states in both themes; forced colours and 200%
  zoom hold. First rows on 100k arrive in 78-228 ms on the dev build with no duplicate reads; re-marking a
  20,000-line file takes 7 ms.

## Not checked by the review

- The Tauri shell, a Storybook run, and the slice's e2e specs (the sweep was read, not re-run).
- The inspector's Source section in the app (the test crawl stored no HTML): rests on unit tests and the diff.
- A robots.txt near 512 KB in the running app: about 2.2 MB of JSON re-read each second while live.
- Finding 4's continued-crawl path on real data.
