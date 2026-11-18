# Lane realsites: ready (2026-11-18)

Build: detached worktree at 8cbd8c24, CLI built in Release, no code changed. Real-site actions: one crawl of jitsunami.com,
one recheck of 7 jitsunami product URLs, and three garden rechecks on a copy. Each ran once. No other host was touched.

## 1. Jitsunami: crawl 4 against crawl 3

| | Crawl 3 (17 Nov) | Crawl 4 (18 Nov, 06:31:43 to 06:52:11) |
|---|---|---|
| Settings | c 5, no rate cap, no crawl-delay, no max (from its `options_json`) | the brief's: `-c 2 --rate 2 --respect-crawl-delay --store-html --max-urls 5000` |
| URLs fetched / pages | 61 / 9 | 62 / 9 |
| Not checked (429) | 8 (7 products and /collections/belts) | 8 (the same 8) |
| Blocked / errors | 0 / 0 | 0 / 0 |
| Time | 14:05 | 20:27 (hit the 20-minute cap) |
| Score, re-analysed at v14 | 100 | 97 (`placeholder-text` -3, a check crawl 3 couldn't run) |
| Checks | 143 the same, 23 new checks that couldn't run on crawl 3 | 17 of 166 fired: 0 errors, 1 warning, 75 notices |

- **Progress words about slowing down:**
  - "jitsunami.com limits its /products/ pages (429 Too Many Requests)…", and the same for /collections/.
  - "limits /products/ pages: 7 left, next try in 6 min", and so on.
  - Tries came at about 00:20, 01:20, 03:20, 07:20 and 15:20, so the gap doubled from 1 to 8 minutes.
  - At 15:31 it said "next try in 16 min", but the cap gave up at 20:27 without that try (bug B5).
- **The last refused requests:** samurai at 06:47:02, /collections/belts at 06:47:09.
- `placeholder-text` on /blogs/news is **correct**: the theme's "About" sidebar shows visible lorem ipsum (`dump4/html/56.html`, line 671).

### Recheck of the 7 product pages
- **When:** 07:02:30, 15 minutes after the last refused request.
- **Plan:** "Rechecking 7 URLs. 7 judged, 0 past the limit of 1,000; about 02:18. Planned in 34 ms."
- **Result:** the site refused it again, and this was not retried. It ran 20:02 and printed "Rechecked 47 URLs. Score 97 before, 97 after. 47
  requests in 20:02".
  - Read: `samurai-jiu-jitsu-belt`, 200 at 07:05:38, plus 40 new links from that page.
  - Refused again (429): roman-gladiator at 07:02:36 and white-belt at 07:18:26.
  - Never asked: blue, purple, brown and black. They were stored as not checked at the moment it gave up (07:22:39).

### Product-page audit
Pages audited:
- `samurai` (crawl 4, after the recheck);
- `brown-belt-oni` (a copy of crawl 2, re-analysed with this build; 28 checks were skipped there as "earlier version").

Every fired check was judged against the stored HTML. Pixel widths were measured independently (`tools/width.mjs`) and match
the engine on all 9 pages.

| Check | Fired on | Verdict | Example |
|---|---|---|---|
| cookies-insecure | 2 of 2 | correct (Shopify platform) | `localization=GB; path=/; …; SameSite=Lax` has no Secure |
| description-too-long | 1 of 2 | correct | samurai is 1,994 px with 320 characters. brown is 981 px, under 985, and correctly not flagged |
| empty-anchor-text | 2 of 2 | correct (theme) | the logo `<a href="/"><img alt="">` |
| heading-levels-skipped | 2 of 2 | correct (theme) | the H1 "Samurai Jiu-Jitsu Belt" is followed by an H5 "Buy Any 2 Belts…" |
| image-missing-dimensions | 2 of 2 | correct (theme) | 36 of 38 `<img>` lack width and height; only the 120×400 and 40×40 ones have them |
| image-no-srcset | 1 of 1 | correct | `<img class="zoom-product" src='//…_1024x1024.jpg'>` has no srcset |
| protocol-relative-links | 1 of 1 | correct (theme) | 18 `href` or `src` values start with `//` (17 in double quotes, 1 in single) |

**Checked and not flagged:**
- **Product structured data:** correct. It is Microdata: Product with name, offers, aggregateRating and image; the Offer has
  priceCurrency and availability. But the price is read wrongly (bug B6).
- **Title:** correct. samurai is 567 px, under the 580 px default, at 65 characters. It would be over Screaming Frog's 561 px.
- **Canonical:** correct. It is self-referencing and in the head.
- **Open Graph:** correct. og:title, og:description and og:image are present, and og:url equals the canonical.
- **X card:** correct. It is summary_large_image with a title and description; the image falls back to og:image.
- **hreflang:** correct. There is none: the store is in one language.

**Checks on more than 80% of product pages:** every check that fired, since only 2 product pages were read. All are template
or platform issues, not rule bugs.

## 2. Garden recheck (a copy; v14 analysis gave score 83, 31 of 147 checks fired)

| | Plan | Fetches | Result | Time |
|---|---|---|---|---|
| a `internal-client-error` (the 4xx id) | "Rechecking 8 URLs, then up to 4 pages linking to those still failing. 8 judged…; about 00:06. Planned in 65 ms." | 14 (8 + 4 linking pages + 2 new images) | 0 fixed, 8 still | 00:07 |
| b `title-duplicate` | "Rechecking 2 URLs. 2 judged…; about 00:01. Planned in 65 ms." | 2 | 0 fixed, 2 still | 00:01 |
| c `robots-txt-no-sitemap` | "Rechecking robots.txt, and the sitemaps. 1 judged…; about 00:01. Planned in 64 ms." | "0" | **FAILED**, exit 1 (bugs B1 and B2) | 2 s |

**Spot-checks:**
- No recheck called anything fixed, so there was nothing to disprove.
- a: all 8 URLs were refetched between 06:32:53 and 06:32:57, and each is still a 404.
- b: both /login/ and /login/?register=true were refetched at 06:33:15: 200, with the title "Login".
- "Still" is the expected answer, since the owner fixed nothing.
- But after c, `si compare` against the before copy says `orphan-page` and `sitemap-non-indexable` are **fixed**. They aren't
  (see B2).

## Bugs (evidence)
- **B1 (high): a sitemaps recheck fails on any crawl made before `sitemap_urls.lastmod` existed.**
  - The error: `SQLite Error 1: 'table sitemap_urls has no column named lastmod'`. It comes from `WriterStatements.cs:128` and `:266`, called from
    `CrawlWriter.Recheck.cs:465`.
  - The cause: when it opens a file to recheck, `CrawlWriter.Recheck.cs:70-71` adds the missing `responses` and `sitemaps` columns, but not
    `CrawlSchema.SITEMAP_URL_COLUMNS`. A crawl that is resumed does add them (`CrawlWriter.cs:157`).
  - It hits every robots.txt or sitemap recheck on the owner's 17 Nov crawls.
- **B2 (high): a failed recheck leaves the file half-written, and the analysis still runs and stores results.**
  - `RegisterRecheckSitemap` (`CrawlWriter.Recheck.cs:427`) deleted the `sitemap_urls` rows (240 became 0) in a batch that was already committed. The
    robots row had already been rewritten.
  - The analysis after the failure then stored:
    - score 83 → 85;
    - `not-in-sitemap` 18 → 257;
    - `orphan-page` 1 → 0;
    - `sitemap-non-indexable` 1 → 0.
  - The CLI printed "Score 83 before, 85 after".
  - `si compare` says those two checks are "fixed", with a score of +2, and the page it calls fixed has no new links. The file is evidence:
    `garden/www.gardenfurnituresets.co.uk.sicrawl`, and its snapshot `garden/before-recheck.sicrawl`.
- **B3 (medium): the URL-list recheck's summary misleads.**
  - `RecheckCommand.cs:181-182` prints "Rechecked {Fetched} URLs" (47) when 7 URLs were asked for and 1 was read. Nothing in the summary
    says that 6 were refused again. The row's fixed, still and not_checked counts are all 0, because `RecheckSettlement` sets no
    outcomes when there is no check.
  - "47 requests" counts the 4 products that were never asked.
  - Garden c printed "0 requests", but robots.txt was refetched (its `fetched_at` is 06:33:28).
  - A recheck whose output is redirected also prints no progress for 20 minutes.
- **B4 (low): the warning's words are untrue.** `CrawlSession.cs:463` says "asked for again at the end, three times, each after the pause".
  - In crawl 4, 5 of the 7 products were never asked: they were stored at 06:52:11, the moment it gave up. `SectionTrickle.cs` `GiveUp`
    stores `Failed(now)` when `last` is null.
  - In the recheck, 4 of the 6 were never asked.
- **B5 (low): a promised try never happens.** The section gap is scheduled past the cap ("next try in 16 min" at 15:21) while
  `SectionTrickle.cs:29` gives up 20 minutes after the rest of the crawl ends (at 20:27). The try never happens, and the last 5 minutes sit idle.
  The next try could be clamped to the cap.
- **B6 (low, latent): Microdata ignores a `content` attribute on elements other than `<meta>`.** `MicrodataReader.cs:154` does this.
  - Samurai's `<div itemprop="price" content="9.1">` is read as "£15.15 £9.10".
  - schema.org's own Offer example uses `content=` on a `<span>`. That Google honours it is my inference.
  - No check validates the price yet.
- **B7 (cosmetic): repeated notes.** `CrawlAnalyzer.cs:483-487` writes one "N checks weren't run: this crawl was made by an earlier version…"
  note per fact collection: 5 lines worded the same (9, 3, 2, 4, 4).
- **Note:** `cookies-insecure` went from 2 to 3 after recheck a. The refetch with an empty cookie jar was sent an `ASP.NET_SessionId` cookie on
  /rattan-fire-pit-sets/, so a recheck can flag a session cookie as new when only the crawler's cookies changed.

## Files (`realsites/`)
- `crawl4/jitsunami.com.sicrawl`: live, after the recheck. Also `crawl4-as-crawled.sicrawl`, `crawl4-after-recheck.sicrawl`
  and `product-urls.txt`.
- `crawl3copy/` and `crawl2copy/`: re-analysed at v14.
- `garden/`:
  - `www.gardenfurnituresets.co.uk.sicrawl`: damaged by c;
  - `before-recheck.sicrawl`;
  - `after-failed-c.sicrawl`;
  - `before-copy-for-compare.sicrawl`.
- `logs/`: crawl4, the plans, every recheck, and the compares.
- `dump4/`, `dump4r/` and `dump2/`: details and HTML.
- `tools/`: `dump.mjs`, `width.mjs` and `facts.py`.
- `checkpoint.md`.
- The worktree was removed. The bugs reproduce at 8cbd8c24 with these files.
