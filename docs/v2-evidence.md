# Evidence: what each check shows (step 3b)

Every one of the 87 checks has to answer, from its own views (Advanced Issues, Simple's problem page, the workbook and
the HTML report), without the inspector or the page source:

- **what** is wrong: the actual value against the limit;
- **where** to fix it: the page; for a resource, every page it's on; for a link, the linking page with the anchor or alt
  text and where on the page it is; and whether it's the site's template or a one-off;
- **how**: the catalog's fix matches what is shown.

The owner, 2026-11-17: "the broken links never showed what page they were on … a large images UI … does not show the
image size OR where the image is shown! For every single view / UI it must be actually useful … easy to scan,
understand and action and the user fix their site."

This page is the per-check contract. The API shapes are in [the sidecar API](v2-sidecar-api.md), marked "(evidence)";
what the engine changes to serve them is under
[Engine changes evidence makes](v2-sidecar-api.md#engine-changes-evidence-makes). The audit behind it (what each view
showed, per check, with file and line) was the step 3b evidence inventory.

## What a check gives its views

| What | Where it's served | For |
| --- | --- | --- |
| **Evidence columns** | `evidence` on the check in `GET /api/checks`: URL column ids, in the order to show them | Every row of the check: Advanced's rows and a group's pages ask `/rows` for them; Simple's "Where it is"; the workbook sheet; the report's examples |
| **Target facts** | `targetFacts` on the check: names of [a target's fields](v2-sidecar-api.md#where-a-url-is-found-found-on), in order | What to say about each URL a page's issue names (`/issues/{checkId}/targets`), and about a group's related URL (`related` on `/groups`) |
| **The detail** | Each row's `detail`, written by the rule | One line, at most about 120 characters, quoted text cut with "…". A measurement gives its value and the limit; a count of things the page has names them where they fit |
| **The group's name** | `relatedUrl` and `related` (the URL and what it is), `sharedText` for a duplicate cluster, or `detail` | The cause: the image, the broken URL, the shared title |
| **Template hint** | `likelyTemplate` on a group, and for a check that doesn't group on its count | "Probably your site's template: one fix" |
| **Order** | `/rows` and `/groups` when no order is asked for | The pages that matter most first |

### Row order: by importance

A check's rows, a group's pages, a check's export and the report's examples all come in the same order unless the
caller asks for another:

- **page checks** (`subject` `page`): `inlinks` descending, then `depth`, then `url`;
- **URL checks** (`subject` `url`: a 404 image, a redirect): `usedOn` descending (every page that refers to it, by any
  kind of link, so a broken image used on 200 pages comes before one used once), then `depth`, then `url`.

Without a URL summary (a crawl continued since its analysis), `inlinks` and `usedOn` aren't there to sort by, and the
order is `depth`, then `url`.

### Group order

Largest group first (`pages` descending), as today, except **`image-too-large`: the largest file first**, then file
size times pages, then the image's crawl order. `/groups` takes `sort` (`pages` or `size`) to ask for the other.

### Template hint for checks that don't group

A check whose `subject` is `page`, whose `grouping` is `none` and which isn't `reportedOnce`, gets `likelyTemplate`
true when it flags at least 90% of the pages the analysis audited (`pagesAnalyzed`) and at least 10 of them: every page
lacks a viewport, a lang, x-default. The views say "probably your site's template: one fix".

### Image checks name their images

`image-missing-alt`, `image-alt-too-long` and `image-missing-dimensions` become what `image-broken` and
`image-too-large` already are: `references` `targets`, `grouping` `relatedUrl`, each page's issue naming the images at
fault. A logo without alt text on 265 pages is one group, with the template hint, and each page's targets say which
image, its alt text and its width and height as the page writes them. The detail stays a count ("3 images"). An image
the page names without a usable URL (no `src`, or past the 10,000 links a page's links are kept to) is still counted,
and is in the group without a related URL.

## Words used in the table

- **S/G/R**: subject (P page, U URL) / grouping (N none, PD page detail, RD response detail, RU related URL) /
  references (– none, S sources, T targets), as they will be. A change from today is in bold.
- **Evidence columns** are [URL column](v2-sidecar-api.md#urls-e3a) ids. New ones: `redirectChain`, `robotsRule`,
  `usedOn`, `pixelWidth`, `pixelHeight` and `fetchError`; since [pixel widths](v2-pixel-widths.md), `titleWidth`,
  `descriptionWidth`, `titleInResults` and `descriptionInResults`; since the [new checks](v2-new-checks.md), `language` and
  `redirectKind` (change C2). "–" is none: the row's URL and detail say it all.
- **Target facts** are fields of a target (`/targets`) and of a group's `related` URL: `statusCode` (with `outcome`),
  `final` (where its redirects end), `indexability`, `canonical` (where it's canonicalised to), `sizeBytes`,
  `contentType`, `pixelWidth`, `pixelHeight` (the file), `responseMs` (its own answer's time), `redirectKind` (what its own
  redirect changes), `img` (the `<img>` as the page writes it: `width`, `height`,
  `alt`, `hasAlt`, `altLength`, `srcset`, `loading`), and the page's link to it: `kind`, `text` (anchor, alt or
  hreflang code), `position`, `fragment`, `flags`, `links` (how many links the page has to it). For a `sources` check
  they describe the group's related URL only (its rows' sources are the found-on read, which already has the linking
  page, kind, text, position and flags).
- **Detail**: before → after. "Same" is unchanged. A limit is shown at its default ("over 115"); a detail names the
  limit the analysis ran with, from its stored settings. The four title and description length checks changed again
  after evidence: they measure pixels ([pixel widths](v2-pixel-widths.md)), so their rows give both forms.
- **Rows**: `imp` = inlinks ↓, depth, URL; `use` = usedOn ↓, depth, URL; `once` = reported once, one row.
  **Groups**: `pages` = largest first; `size` = largest file first; "–" = the check doesn't group.

## The checks

### Response codes

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `internal-client-error` | U/RD/S | `type` | – | Same ("404 Not Found") | use · pages |
| 2 | `internal-server-error` | U/RD/S | `type`, `responseMs` | – | Same ("500 Internal Server Error") | use · pages |
| 3 | `internal-unreachable` | U/RD/S | `type`, `fetchError`, `responseMs` | – | Same ("Timed out", "Connection failed": the group stays one per kind; the column says which failure) | use · pages |
| 4 | `internal-blocked` | U/RD/S | `type` | – | Same (the protection). Since the polite crawl a 429 isn't this check's: it's held back, asked again, and stored as not checked if it never gets through | use · pages |
| 114 | `content-type-mismatch` (new) | U/RD/S | `type`, `contentType` | – | `Used as a script, sent as text/html` (each way it's used; "sent with no Content-Type") | use · pages |
| 5 | `soft-404` | P/N/– | `title`, `h1`, `wordCount` | – | "The title or H1 says the page wasn't found" → `The title reads as an error: "Page not found – Example" · 120 words, under 300` (or "The H1 reads as an error: …"; the quoted text is the field that matched) | imp · – |

### Redirects

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 6 | `redirect-chain` | U/RU/S | `redirectChain`, `redirectKind` | `statusCode`, `indexability` (the final URL the group is named by) | Same ("3 redirects") | use · pages |
| 7 | `redirect-loop` | U/N/S | `redirectChain` | – | "3 hops" → `3 hops: /a/ (301) → /a (301) → back to /a/` (paths on the URL's own host, full URLs elsewhere) | use · – |
| 8 | `redirect-broken` | U/RD/S | `redirectChain`, `redirectKind` | `statusCode` (the URL it ends at) | Same ("Ends at a URL that returns 404 Not Found") | use · pages |
| 9 | `redirect-temporary` | U/RD/S | `redirectChain`, `redirectKind` | `statusCode` (the target) | Same ("302 Found") | use · pages |
| 115 | `redirect-temporary-cached` (new) | U/RD/S | `redirectChain`, `redirectKind` | – | `302 Found, cached for 30 days` | use · pages |
| 10 | `redirect-downgrade` | U/RU/S | `redirectChain` | `statusCode`, `final` (the http:// target) | Same (none) | use · pages |
| 11 | `meta-refresh` | P/PD/– | – | – | Same (`Moves to https://x/ after 5 seconds`) | imp · pages |

### Links

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 12 | `broken-internal-links` | P/RU/T | – | `statusCode`, `final`, `text`, `position`, `fragment`, `flags`, `links` | Same ("2 broken URLs (the first redirects to a URL that returns 404 Not Found)") | imp · pages |
| 13 | `broken-external-links` | P/RU/T | – | as 12 | Same | imp · pages |
| 14 | `links-to-redirects` | P/RU/T | – | `statusCode`, `final` (the URL to link to instead), `redirectKind` (what the redirect changes), `text`, `position`, `links` | Same ("2 redirecting URLs") | imp · pages |
| 15 | `internal-nofollow-links` | P/RU/T | – | `statusCode`, `text`, `position`, `flags`, `links` | Same ("Links to 2 URLs") | imp · pages |
| 16 | `empty-anchor-text` | P/RU/T | – | `statusCode`, `position`, `flags` (`imageLink`: an image link whose image has no alt), `links`. A link to the page itself (a home page's logo, since the check audit) names the page: its target is the page, read as its link without text and without a fragment, and the app says "this same page" | Same ("Links to 2 URLs") | imp · pages |
| 17 | `broken-fragment-links` | P/RU/T | – | `statusCode`, `fragment`, `text`, `position`, `links` | "2 links" → `2 links to missing sections: /faq/#specs, #top` (a fragment alone is on the page itself) | imp · pages |
| 18 | `orphan-page` | P/N/S | – | – | Same ("Only found in a sitemap"), or `Only nofollow links lead here` (a sitemap page only nofollow links lead to, since the new checks) | imp · – |
| 101 | `uncrawlable-links` (new) | P/N/– | – | – | `3 links: "Shop" (javascript:), "Next" (onclick) …` (the first three, what makes each uncrawlable; only links that lead somewhere: script that navigates, a routerLink, a URL on a `<span>`). Template hint | imp · – |
| 102 | `broken-resources` (new) | P/RU/T | – | `statusCode`, `final`, `kind`, `position`, `links` | `2 broken files (the first returns 404 Not Found)` | imp · pages |
| 103 | `links-to-localhost` (new) | P/RU/T | – | `statusCode`, `text`, `position` | `2 links, such as http://localhost:3000/shop`; on a crawl started at a private address, that machine's own addresses don't count | imp · pages |
| 104 | `few-internal-outlinks` (new) | P/N/– | `outlinks` | – | `No links to other pages`, `2 links to other pages, under 3` | imp · – |
| 105 | `generic-anchor-text` (new) | P/N/T | – | `text`, `position`, `statusCode` | `3 links: "Read more" ×2, "here"` | imp · – |
| 106 | `non-indexable-inlinks-only` (new) | P/N/S | `inlinks`, `linkingPages` | – | `Linked from 3 pages, none of them indexable` | imp · – |
| 107 | `deep-page` (new) | P/N/– | `depth`, `inlinks` | – | `6 clicks from the start, over 4` (the crawl's recorded depth) | imp · – |
| 108 | `too-many-links` (new) | P/N/– | `outlinks` | – | `1,240 internal links, over 1,000` (and/or `140 links to other sites, over 100`) | imp · – |
| 109 | `slow-external-links` (new) | P/RU/T | – | `statusCode`, `responseMs`, `text`, `position` | `2 slow links, the slowest 7.4 s` | imp · pages |
| 110 | `pagination-not-linked` (new) | P/RU/T | – | `statusCode`, `indexability` | `rel="next" https://x/list/page/2/ has no link` | imp · pages |
| 111 | `pagination-broken` (new) | P/RU/T | – | `statusCode`, `final` | `rel="next" returns 404 Not Found`, `rel="prev" redirects (301 Moved Permanently)` | imp · pages |
| 112 | `pagination-to-non-indexable` (new) | P/RU/T | – | `statusCode`, `indexability`, `canonical` | `rel="next" is noindex` | imp · pages |
| 113 | `pagination-sequence-error` (new) | P/PD/– | – | – | `2 rel="next" URLs`, `rel="next" names the page itself`, `/list/page/2/ doesn't point back`, `rel="next" leads back to /list/`; grouped by its kind | imp · pages |

### Indexability

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 19 | `noindex-page` | P/N/– | `metaRobots`, `xRobotsTag` | – | Same (none) | imp · – |
| 20 | `nofollow-page` | P/N/– | `metaRobots`, `xRobotsTag` | – | Same (none) | imp · – |
| 21 | `blocked-by-robots` | U/RD/S | `robotsRule` | – | Same (none, or "robots.txt couldn't be read") | use · pages |
| 22 | `snippet-blocked` | P/PD/– | `metaRobots`, `xRobotsTag` | – | Same ("nosnippet") | imp · pages |
| 23 | `robots-txt-unreachable` | U/N/– | – | – | Same | once |
| 24 | `robots-txt-unreachable-other-origin` | U/N/– | – | – | Same | once |
| 25 | `robots-txt-missing` | U/N/– | – | – | Same | once |
| 26 | `site-hidden-from-search` | P/N/– | `metaRobots`, `xRobotsTag` (the home page's) | – | Same ("212 of 240 pages are noindex") | once |
| 88 | `robots-directives-conflict` (new) | P/PD/– | `metaRobots`, `xRobotsTag` | – | `The HTML says "index, follow"; the X-Robots-Tag header says "noindex"` (grouped by it: a template's conflict is one group) | imp · pages |
| 89 | `noindex-near-home` (new) | P/N/– | `metaRobots`, `xRobotsTag`, `depth`, `inlinks` | – | `2 clicks from the start` | imp · – |
| 90 | `unavailable-after-passed` (new) | P/N/– | `metaRobots`, `xRobotsTag` | – | `unavailable_after: 2025-06-30, passed` (the date as written) | imp · – |
| 91 | `media-preview-blocked` (new) | P/PD/– | `metaRobots`, `xRobotsTag` | – | The rules found: `noimageindex, max-video-preview:0` | imp · pages |
| 92 | `translation-blocked` (new) | P/N/– | `metaRobots`, `xRobotsTag` | – | None | imp · – |
| 93 | `indexifembedded-without-noindex` (new) | P/N/– | `metaRobots`, `xRobotsTag` | – | None | imp · – |

### Canonicals

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 27 | `canonicalised-page` | P/RU/T | – | `statusCode`, `final`, `indexability`, `canonical`, `flags` (`fromHttpHeader`) | Same (none) | imp · pages |
| 28 | `canonical-missing` | P/N/– | – | – | Same (none) | imp · – |
| 29 | `canonical-multiple` | P/N/– | – | – | "2 different URLs" → `2 URLs: https://x/a/ (head) · https://x/b/ (HTTP header)` | imp · – |
| 30 | `canonical-broken` | P/RU/T | – | `statusCode`, `final`, `flags` | Same ("The canonical URL returns 404 Not Found") | imp · pages |
| 31 | `canonical-to-redirect` | P/RU/T | – | `statusCode`, `final` (the URL to put in the canonical), `flags` | Same (`The canonical URL redirects (301) to https://x/final`) | imp · pages |
| 32 | `canonical-to-non-indexable` | P/RU/T | – | `statusCode`, `indexability`, `canonical` (the next hop of a canonical chain), `final`, `flags` | Same ("The canonical URL is noindex") | imp · pages |
| 33 | `canonical-outside-head` | P/N/– | – | – | none → `<img src="/t.gif"> on line 12 ended the head early`, or `It's in the body (after <body> on line 30)`; none for a page crawled before this was collected | imp · – |
| 34 | `canonical-relative` | P/PD/– | – | – | Same (the href; grouped by its form) | imp · pages |
| 35 | `canonical-invalid` | P/PD/– | – | – | Same (the href; grouped by what's wrong) | imp · pages |
| 36 | `noindex-with-canonical` | P/RU/T | `metaRobots`, `xRobotsTag` | `statusCode`, `indexability`, `flags` | Same (none) | imp · pages |
| 94 | `canonical-to-first-page` (new) | P/RU/T | – | `statusCode`, `indexability`, `canonical`, `flags` | `Page 2 names page 1 as its canonical` (or "A later page …" when only its rel="prev" says so); grouped by the first page, so a series is one group | imp · pages |
| 95 | `canonical-other-domain` (new) | P/RU/T | – | `statusCode`, `final`, `indexability`, `canonical` | `On another host: staging.example.com` | imp · pages |
| 96 | `canonical-loop` (new) | P/RU/T | – | `statusCode`, `indexability`, `canonical` | `A loop of 2: /a/ → /b/ → back to /a/` (paths on the page's own host) | imp · pages |
| 97 | `canonical-on-redirect` (new) | U/N/S | `redirectChain` | – | `Redirects to https://x/c/; its canonical header names https://x/b/` | use · – |
| 98 | `canonical-fragment` (new) | P/PD/– | – | – | The fragment: `#reviews` | imp · pages |
| 99 | `canonical-invalid-attribute` (new) | P/PD/– | – | – | The attributes: `media`, `hreflang, type` | imp · pages |
| 100 | `canonical-unlinked` (new) | P/N/S | `inlinks`, `linkingPages` | – | `Only canonicals lead here`; its found-on lists the pages whose canonical names it | imp · – |

### Sitemaps

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 37 | `sitemap-non-indexable` | U/RD/S | `indexability`, `finalUrl`, `canonicalUrl` (the URL to list instead) | – | Same ("The URL redirects (301 Moved Permanently)") | use · pages |
| 38 | `not-in-sitemap` | P/N/– | – | – | Same (none) | imp · – |
| 39 | `sitemap-error` | U/N/– | – | – | Same | once |
| 40 | `sitemap-missing` | U/N/– | – | – | Same | once |
| 41 | `sitemap-url-other-host` | U/RD/S | `finalUrl` (the URL on the sitemap's own host to list instead) | – | Same ("The URL redirects (301 Moved Permanently)"), and since change C4 `The URL is http://, the sitemap https://` for a URL on another scheme | use · pages |
| 123 | `robots-txt-no-sitemap` (new) | U/N/– | – | – | `https://x/robots.txt has no Sitemap: line; the sitemap was found at https://x/sitemap.xml` | once |
| 124 | `sitemap-over-limit` (new) | U/N/– | – | – | `https://x/a.xml (62,113 URLs), https://x/b.xml (50.5 MB), https://x/index.xml (50,001 sitemaps) and 1 more` | once |
| 125 | `sitemap-near-limit` (new) | U/N/– | – | – | As `sitemap-over-limit`: `https://x/a.xml (46,200 URLs)` | once |
| 126 | `sitemap-empty` (new) | U/N/– | – | – | `https://x/empty.xml and 2 more` | once |
| 127 | `sitemap-url-repeated` (new) | U/N/S | – | – | `In 2 sitemaps` (its found-on lists them) | imp · – |
| 128 | `sitemap-lastmod-invalid` (new) | U/N/– | – | – | `1,204 entries: 1,200 in the future, 4 unreadable (https://x/sitemap.xml and 1 more)` | once |

### Titles

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 42 | `title-missing` | P/N/– | – | – | Same (none) | imp · – |
| 43 | `title-multiple` | P/N/– | – | – | "2 titles" → `2 titles: "Sofas \| Example" · "Example"` | imp · – |
| 44 | `title-too-long` | P/N/– | `title`, `titleInResults`, `titleWidth`, `titleLength` | – | "78 characters" → `78 characters, over 60`, and since [pixel widths](v2-pixel-widths.md) `780 px, over 580 (78 characters)` | imp · – |
| 45 | `title-too-short` | P/N/– | `title`, `titleWidth`, `titleLength` | – | "10 characters" → `10 characters, under 30`, and since pixel widths `98 px, under 200 (10 characters)` | imp · – |
| 46 | `title-duplicate` | P/RU/– | `title` | – | Same ("2 other pages"). The group is named by its `sharedText`, the title | imp · pages |

### Meta descriptions

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 47 | `description-missing` | P/N/– | – | – | Same (none) | imp · – |
| 48 | `description-multiple` | P/N/– | – | – | "2 meta descriptions" → `2 meta descriptions: "Garden sofas for…" · "Shop online"` | imp · – |
| 49 | `description-too-long` | P/N/– | `description`, `descriptionInResults`, `descriptionWidth`, `descriptionLength` | – | "183 characters" → `183 characters, over 155`, and since pixel widths `1,084 px, over 985 (183 characters)` | imp · – |
| 50 | `description-too-short` | P/N/– | `description`, `descriptionWidth`, `descriptionLength` | – | "10 characters" → `10 characters, under 70`, and since pixel widths `63 px, under 400 (10 characters)` | imp · – |
| 51 | `description-duplicate` | P/RU/– | `description` | – | Same. The group is named by its `sharedText` | imp · pages |

### Headings

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 52 | `h1-missing` | P/PD/– | – | – | Same (none, or "The H1 has no text") | imp · pages |
| 53 | `h1-multiple` | P/N/– | – | – | "3 H1s" → `3 H1s: "Sofas" · "Example" · "Sign up"` | imp · – |
| 54 | `h1-duplicate` | P/RU/– | `h1` | – | Same. The group is named by its `sharedText` | imp · pages |

### Content

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 55 | `low-word-count` | P/N/– | – | – | "120 words" → `120 words, under 200` | imp · – |
| 56 | `duplicate-content` | P/RU/– | `indexability`, `canonicalUrl`, `inlinks` (which twin to keep) | – | Same ("2 other pages") | imp · pages |
| 57 | `near-duplicate-content` | P/RU/– | `indexability`, `canonicalUrl`, `inlinks`, `wordCount` | – | Same ("2 similar pages") | imp · pages |
| 58 | `duplicate-site-version` | U/RD/– | – | – | Same | once |

### Images

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 59 | `image-missing-alt` | P/**RU**/**T** | – | `img`, `position`, `links` | Same ("3 images"); the images are now its targets | imp · pages |
| 60 | `image-alt-too-long` | P/**RU**/**T** | – | `img` (`alt`, `altLength`), `position` | Same ("2 images") | imp · pages |
| 61 | `image-missing-dimensions` | P/**RU**/**T** | – | `img` (`width`, `height`: which is missing), `pixelWidth`, `pixelHeight` (what to write), `position` | Same ("5 images") | imp · pages |
| 62 | `image-broken` | P/RU/T | – | `statusCode`, `final`, `text`, `position`, `links` | Same ("1 image") | imp · pages |
| 63 | `image-too-large` | P/RU/T | – | `sizeBytes`, `contentType`, `pixelWidth`, `pixelHeight`, `img` (the size it's shown at, `srcset`, `loading`), `statusCode`, `final`, `position`, `links` | "2 images over 200 KB" → `2 images over 200 KB, the largest 1.8 MB` | imp · **size** |

### Hreflang

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 64 | `hreflang-invalid-code` | P/PD/– | – | – | Same ("en_UK, english") | imp · pages |
| 65 | `hreflang-missing-self` | P/N/– | – | – | none → `None of its 4 entries names this URL; "en" names http://www.example.com/en` (an entry differing only by scheme, www, a trailing slash or case), else `None of its 4 entries names this URL` | imp · – |
| 66 | `hreflang-missing-return` | P/RU/T | – | `statusCode`, `text` (the code), `indexability` | Same ("2 alternates don't link back") | imp · pages |
| 67 | `hreflang-broken-target` | P/RU/T | – | `statusCode`, `final`, `text` | Same ("2 URLs") | imp · pages |
| 68 | `hreflang-to-non-indexable` | P/RU/T | – | `statusCode`, `indexability` (why), `final`, `canonical` (the URL to use instead), `text` | Same ("2 URLs") | imp · pages |
| 69 | `hreflang-duplicate-code` | P/PD/– | – | – | "en-gb" → `en-gb: /uk/ · /gb/; de: /de/ · /at/`. Grouped by the codes alone (a new `FaultOf`: "en-gb, de", today's detail), so a template's conflict stays one group | imp · pages |
| 70 | `hreflang-missing-x-default` | P/N/– | – | – | Same (none) | imp · – |
| 71 | `hreflang-outside-head` | P/N/– | – | – | none → `2 entries in the body: <div id="gtm"> on line 12 ended the head early`; `2 entries in the body` for a page crawled before this was collected | imp · – |

### Structured data

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 72 | `structured-data-invalid` | P/PD/– | – | – | Same (the parser's message; grouped by it) | imp · pages |
| 73 | `structured-data-missing-required` | P/PD/– | – | – | Same ("Product: needs name; Offer: needs price") | imp · pages |

### Security

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 74 | `no-https` | P/N/– | – | – | Same | once |
| 75 | `http-page` | P/N/**S** | – | – | Same (none). Its found-on gives the links to update | imp · – |
| 76 | `mixed-content` | P/RU/T | – | `kind`, `statusCode`, `contentType`, `position`, `links` | Same ("2 HTTP resources") | imp · pages |
| 77 | `security-headers-missing` | P/N/– | – | – | Same ("Missing X-Content-Type-Options, Referrer-Policy") | once |

### URLs

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 78 | `url-uppercase` | P/N/**S** | – | – | Same (none). Its found-on gives the links to update if it's renamed | imp · – |
| 79 | `url-underscores` | P/N/**S** | – | – | Same (none). As 78 | imp · – |
| 80 | `url-too-long` | P/N/– | – | – | "132 characters" → `132 characters, over 115` | imp · – |

### Performance

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 81 | `slow-response` | P/N/– | – | – | "1,250 ms (first byte after 900 ms)" → `1,250 ms, over 1,000 ms (first byte after 900 ms)` | imp · – |
| 82 | `large-page` | P/N/– | – | – | "1.4 MB" → `1.4 MB, over 1 MB` | imp · – |
| 83 | `html-past-crawl-limit` | P/N/– | – | – | "2.3 MB" → `2.3 MB, past the 2 MB Google reads` | imp · – |
| 116 | `resource-past-crawl-limit` (new) | U/N/S | `sizeBytes`, `contentType` | – | `2.3 MB, past the 2 MB Google reads` (the size as received: compressed, if the server compressed it) | use · – |

### HTML

| # | Check | S/G/R | Evidence columns | Target facts | Detail before → after | Rows · groups |
| --- | --- | --- | --- | --- | --- | --- |
| 84 | `viewport-missing` | P/N/– | – | – | Same (none). Template hint | imp · – |
| 85 | `lang-missing` | P/N/– | – | – | Same (none). Template hint | imp · – |
| 86 | `charset-missing` | P/N/– | – | – | None → none, but a meta tag past the first 1024 bytes says where it ends (the check audit's): `A meta tag declares it, but ends at byte 1,180, past the first 1,024 bytes browsers read`. Template hint | imp · – |
| 87 | `open-graph-missing` | P/PD/– | – | – | Same ("Missing og:image; empty og:description") | imp · pages |
| 117 | `head-tags-in-body` (new) | P/PD/– | – | – | `title, meta description and 3 Open Graph tags are in the body: <div id="promo"> on line 12 ended the head early`, or `… are in the body, after <body> on line 30`; grouped by the cause (`FaultOf`: the element, without its line, or "After <body>") | imp · pages |
| 118 | `html-structure-errors` (new) | P/PD/– | – | – | `2 <head> tags; 2 <body> tags`, `<html> after content`, `<head> after content` | imp · pages |
| 119 | `viewport-invalid` (new) | P/PD/– | – | – | `user-scalable=no; maximum-scale=1`, `no width or initial-scale`, `2 viewport tags that differ` | imp · pages |
| 120 | `charset-invalid` (new) | P/PD/– | – | – | `Declares windows-1252, not UTF-8`, `The header says iso-8859-1; the page says utf-8`, `2 declarations: utf-8, iso-8859-1` (a late `<meta charset>` with the header declaring isn't flagged: the real-site audit found it on every page of two of three sites, to no effect) | imp · pages |
| 121 | `lang-invalid` (new) | P/PD/– | `language` | – | The value: `english` | imp · pages |
| 122 | `mobile-alternate` (new) | P/N/– | – | – | `Mobile version: https://m.example.com/x` | imp · – |

**New checks (step 3b, [v2-new-checks.md](v2-new-checks.md)):** rows 88 on, marked "(new)", each in its category's
table. `canonical-unlinked` is P where the specification said U: it flags only indexable pages, as `orphan-page` does.
`canonicalised-page` (27) no longer flags a page `canonical-to-first-page` or `canonical-other-domain` flags, and
`canonical-to-non-indexable` (32) no page of a canonical loop. `meta-refresh` (11) also reads a `Refresh` response header:
`The Refresh header moves to https://x/ after 5 seconds`.

**In all:** 27 checks get evidence columns; 24 get target facts (the 20 `targets` checks, and the related URL of 4
`sources` checks' groups: 6, 8, 9 and 10); 21 details change (22 with `charset-missing`'s, which the check audit added); 6 checks change how they're shown (3 image checks become
`targets` grouped by image, and 3 checks get found-on: `sources` go from 13 checks to 16, `targets` from 17 to 20);
1 group order changes (`image-too-large`); and every check's rows come by importance.

## What the exports carry

- **The workbook's check sheet** (and `kind: 'check'`): "Address", "Page title", "What was found", "Other address",
  then the check's evidence columns under their titles (one already there, "Page title" for `title`, isn't repeated),
  then where to fix it. A `sources` check's columns stay as they are. A `targets` check's are named by what the targets
  are, each target written with its facts:

  | Checks | Columns | A target in the cell |
  | --- | --- | --- |
  | Links (12-17) | "Links to (addresses)", "Links to" | `https://x/old/ (404; footer, “Old range”)`; links-to-redirects `https://x/old (301 → https://x/new/ 200; content, “Shop”)`; broken-fragment `https://x/faq/#specs (200; content, “Specs”)`; empty anchor `https://x/ (200; header, image link)` |
  | Canonicals (27, 30-32, 36) | "Canonical" (one column: a page has one) | `https://x/a (301 → https://x/b/ 200)`, `https://x/a/ (200; noindex)`, `… ; from the HTTP header` |
  | Images (59-63) | "Images (addresses)", "Images"; `image-too-large` also "Largest image (bytes)", a number, so the sheet sorts by it | `https://x/hero.jpg (200; 1.8 MB, image/jpeg, 4000×3000 px, shown at 400×300; content, alt “Sofa”)`; missing alt `… ; no alt`; missing dimensions `… ; no height; the file is 1200×800 px` |
  | Hreflang (66-68) | "Alternates (addresses)", "Alternates" | `https://x/de/ (404; “de”)`, `https://x/de/ (200; noindex; “de”)` |
  | Mixed content (76) | "HTTP resources (addresses)", "HTTP resources" | `http://x/a.js (200; script in the head)` |

  `redirectChain` and `robotsRule` are written as text: `301 → https://x/b/ (301) → https://x/c/ (200)`, and
  `Disallow: /cart/ (line 12 of https://www.example.com/robots.txt)`. Rows come by importance, not crawl order.
- **A check's rows exported** (`kind: 'issueRows'`) without `columns` have `url`, `detail`, the check's evidence
  columns, then `relatedUrl`; without `sort`, by importance.
- **The HTML report**: each problem's examples come by importance; "What was found" gives the detail and the evidence
  ("780 px, over 580 (78 characters) · “Garden sofas and chairs for every…” · Title in results: Garden sofas…"; in
  plain words the four title and description length checks say where Google will likely cut instead, "Google will
  likely cut this title off after “…every season, delivered free”", or "Only 10 characters long", as Simple does: see
  [pixel widths](v2-pixel-widths.md#8-the-report-and-simples-words)); "Where to fix it" is given for `targets` checks
  too ("Image /img/hero.jpg (1.8 MB, 4000×3000 px, shown at 400×300) and 1 other"), and for `sources` checks names
  where on the linking page the link is, and its text ("/garden/ (footer, “Old range”)"). A check that doesn't group
  and has the template hint gets the "One fix" line.

## Left out, and why

- **What's inside an empty link** (`empty-anchor-text`): the `imageLink` flag and the position say "an image link in
  the header"; which image is inside isn't recorded, and recording it is new collection the brief doesn't take on.
- **A breakdown of a large page's HTML** (inline scripts, styles, base64): new collection; the size against the limit
  is shown.
- **Near-duplicates' similarity** (`near-duplicate-content`): the twins are shown; a percentage per cluster isn't
  defined (it's per pair).
- **Every failing sitemap and where each is referenced** (`sitemap-error`): the first three are in the detail and the
  Sitemaps screen has the rest.
- **Which JSON-LD block failed, with the code around it** (`structured-data-invalid`): the parser's message has the
  line and position.
- **The alternate's own hreflang list** (`hreflang-missing-return`): the alternate is named, with its status; its list
  is in its inspector.
- **Whether `canonical-invalid` is in the HTML or a Link header**: a detail prefix would change the fault it's grouped
  by; left for the accuracy pass.

## As built

What Stage B built differs from the contract above only where this says so; the shapes are as
[the sidecar API](v2-sidecar-api.md) gives them.

- **An image is named by its URL, looked up.** Each image at fault is resolved to the crawl's URL by its address,
  normalised as the crawler normalised the link it stored for the same `<img>`, and looked up (`AnalysisReader.ReadUrlId`):
  never by its place among the page's links. Only images at fault are looked up, once a page each. The same image shown
  twice on a page counts twice in the detail and is named once. On the step 3b audit's three real crawls (analysed as
  copies), every image at fault resolved, and each to the URL of the page's own image link to it:

  | Crawl | Images at fault | Resolved to a URL | With the page's own image link | Unresolved |
  | --- | --- | --- | --- | --- |
  | jitsunami.com | 63 | 63 (100%) | 63 (100%) | 0 |
  | www.aptitude.co.uk | 135 | 135 (100%) | 135 (100%) | 0 |
  | www.gardenfurnituresets.co.uk | 4,153 | 4,153 (100%) | 4,153 (100%) | 0 |

  Analysed with this engine, none of their image checks' issues fell in the group naming no image (garden: 4,151
  page-image pairs of `image-missing-dimensions` in 3,661 groups, the reviews logo on 195 pages one group). The synthetic
  site's images are named the same way (`EvidenceDetailTests`, `EvidenceReadTests`).
- **`pixelWidth` and `pixelHeight` are shown only.** They don't sort or filter: a crawl made before them has no column to
  read, so their values are read for the rows of a page, as `redirectChain` and `robotsRule` are.
- **`fetchError`** is the error the crawl recorded for any outcome other than success (a timeout, a connection that
  failed, robots.txt that couldn't be read), and null for a success.
- **The synthetic site's images say their size**: their files are JPEGs whose frame header says 640×480, and the planted
  large image's 4,000×3,000, so a crawl of it has pixel sizes to show and test.
- **Details as built, where the table shows one form**: one oversized image says its size, `1 image over 200 KB: 293 KB`;
  text that ended a head says so without a line, which the parser doesn't give for text (`Text "Welcome" ended the head
  early`); a page with one hreflang entry says `Its one entry doesn't name this URL`. A quoted text is cut at 40
  characters, a soft 404's at 60, and every detail at 120, each with "…".
- **Rows by importance keep the URL as their last key**: on the 100k benchmark crawl, the biggest check's rows
  (`title-duplicate`, 5,152) read by importance in 18.1 ms at p95 (by inlinks alone, 14.3 ms), its groups in 16.1 ms,
  against the 75 ms gate of a check's reads; no fallback to crawl order was needed.
