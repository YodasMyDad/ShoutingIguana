# The check audit on three real sites (step 3b)

Every one of the 87 checks, held to three real sites on 2026-11-17: what it flagged, whether each flag was right, what
it missed, and whether its words, in Advanced and in Simple, say what it does. The plan is in
[v2-rebuild.md](v2-rebuild.md), "Step 3b, the check audit". The fixes are on `ui/3b-accuracy`; each has a planted case
on the synthetic site, or a crawl test, that was seen failing without it.

## In short

- **29 of 87 checks fired** on at least one of the three sites, 58 on none ("not seen on a real site": proven on the
  synthetic site only, until a site that has the fault is named).
- **Wrong flags: 11, all from one cause**, Shopify's hosted login: jitsunami.com's "Sign In" link goes to
  `/account/login`, which answers 302 to Shopify's login page with a one-off token (10 `links-to-redirects`, 1
  `redirect-temporary`). Three more orphans on jitsunami were unproven: the pages that would link them were refused,
  and the check ran regardless. Every other flag sampled, and on the on-page checks every page, was right.
- **Misses: one kind.** A home page's own empty logo link was never tested (jitsunami's). Every check otherwise
  flagged everything its definition covers, on every site.
- **Faults found by reading the code**, not seen on these sites: a canonical or hreflang target that refused the
  crawler counted as non-indexable (jitsunami's 429s show how easily that happens), schema.org subtypes never checked,
  and a meta charset past the first 1024 bytes accepted. Each is fixed, with a planted case.
- **Verdicts:** 66 right (50 of them not seen), 8 fixed, 9 reworded, 3 severity changed, 1 handed to `ui/3b-polite`
  (`internal-blocked`), 0 dropped.
- **Scores after the fixes** (copies of the three crawls analysed again): aptitude 85, unchanged; jitsunami 76 to 79
  (10 `links-to-redirects` and 1 `redirect-temporary` gone, `orphan-page` skipped and saying why, its home page's logo
  link now among the links without text); garden 83 to 85 (duplicate descriptions are a notice).
- **Since the audit**, the four title and description length checks measure pixels, not characters
  ([pixel widths](v2-pixel-widths.md), `7047d4b`). On copies of the three crawls, `title-too-long` went from 14/0/84
  to 11/0/69, `title-too-short` from 3/6/10 to 2/3/8, `description-too-long` from 0/2/203 to 0/2/163 and
  `description-too-short` from 1/1/0 to 0/1/0. They're Notices.

## How it was checked

**The sites**, named by the owner on 2026-11-10 and crawled on 2026-11-17 with the CLI, politely: `-c 2 --rate 2
--respect-crawl-delay --store-html`, at most 5,000 URLs, robots.txt obeyed, sitemaps discovered.

| Site | What it is | URLs | Pages read | Crawl |
| --- | --- | ---: | ---: | --- |
| `www.aptitude.co.uk` (a) | Company site and blog | 602 | 33 | 2 min |
| `jitsunami.com` (j) | Shopify shop | 75 | 10 | 35 min, every product page answered 429 |
| `www.gardenfurnituresets.co.uk` (g) | Furniture shop, 6,051 URLs found (mostly images) | 4,360 | 260 | 36 min |

**Three audits, one per group of checks** (indexing: canonicals, indexability, robots.txt, hreflang, sitemaps;
responses: response codes, redirects, links, images, security, performance; on-page: titles, descriptions, headings,
content, URLs, structured data, HTML). Each worked from the stored crawl only, never fetching the sites again:

- **Every check recomputed independently** from the raw tables and stored HTML, and diffed with the `issues` and
  `issue_targets` tables: a Python re-parse of every stored page (`html.parser`, not the engine's AngleSharp) for
  titles, descriptions, headings, viewport, lang, charset, Open Graph, word counts, canonicals, hreflang and robots
  directives; SQL over every response for status, redirect and link checks; all-pairs brute force over the stored
  fingerprints for near duplicates; Python's `json` over every JSON-LD block. On all three crawls the expected flags
  equalled the stored ones for every check, related URLs included: the rules do what their code says.
- **Flags read by hand** against the stored HTML and headers: every flag where a check fired fewer than ten times on a
  site, ten spread across page types otherwise, and on the on-page checks every page.
- **Misses hunted with scans that don't use the engine's parser**: raw regexes over every page for images without alt
  or with long alt, `http://` resources, meta refreshes, error wording in titles and H1s, unsized images by CSS class,
  rel=canonical and hreflang anywhere in the HTML, robots meta and X-Robots-Tag; SQL over every non-200 response and
  every image over 200 KB.
- **Words read as a site owner** in both modes, and severities set against the scale ("Serious: broken for search
  engines or users now, fix first") and Screaming Frog's where `docs/v2-comparison.md` gives it.
- **Claims checked at the source**: the catalog's "Google stopped showing FAQ rich results in May 2026" is right:
  Google's Search Central changelog, 8 May 2026, "This feature will no longer appear in Google Search starting May 7,
  2026", and 15 June 2026, the documentation removed. Kept as written.

**jitsunami.com was rate-limited.** Shopify answered 429 to all 12 product URLs (2 requests a second, concurrency 2,
Crawl-delay obeyed), so no product page was read: the structured-data and product-template checks saw only its home,
collection, blog, cart, contact, FAQ and policy pages, and its product markup is unaudited. Pacing is being fixed on
`ui/3b-polite` (slow start, a learned pace per host, Retry-After honoured host-wide, 429s deferred to the retry pass).
Re-crawl jitsunami and audit its product pages once that merges.

## Every check

| Check | Category, severity now | Fired a/j/g | Wrong flags | Misses | Verdict | Change | Commit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `internal-client-error` | Response codes, Error | 0/2/8 | 0 | 0 | right | – | – |
| `internal-server-error` | Response codes, Error | 0/0/1 | 0 (whether g's 500 lasts is unverified) | 0 | right | – | – |
| `internal-unreachable` | Response codes, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `internal-blocked` | Response codes, Warning | 0/12/0 | 0, but all 12 are 429 rate limits under a "firewall" title | 0 | reword and severity change, on `ui/3b-polite` | Title "refused the crawler", Notice; not here (that branch owns it) | – |
| `soft-404` | Response codes, Warning | 0/0/0 | – | 0 (gap, no real case: "We can't find the page", "isn't available") | right, not seen | – | – |
| `redirect-chain` | Redirects, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `redirect-loop` | Redirects, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `redirect-broken` | Redirects, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `redirect-temporary` | Redirects, Notice | 0/1/0 | 1: j `/account/login`, 302 to Shopify's hosted login | 0 | fixed | A temporary redirect to another site isn't flagged | `6921d66` |
| `redirect-downgrade` | Redirects, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `meta-refresh` | Redirects, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `broken-internal-links` | Links, Error | 0/1/4 | 0 | 0 | right | – | – |
| `broken-external-links` | Links, Error (Warning at the audit; the owner's decision of 2026-11-19: a broken link is an error) | 8/0/1 | 0 (bot walls rightly left out; who sent a's two 409s is unverified) | 0 | reworded | What says refused and robots-disallowed links aren't counted, in both modes | `b84ee69` |
| `links-to-redirects` | Links, Notice | 0/10/0 | 10: j's "Sign In" link to a 302 hosted login | 0 | fixed | Links to 302, 303 and 307 aren't counted; words in both modes | `6921d66` |
| `internal-nofollow-links` | Links, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `empty-anchor-text` | Links, Notice | 0/9/260 | 0 | 1 kind: a home page's own empty logo link | fixed | A page's empty links to itself count; `#` and `#top` don't | `a3b83c1` |
| `broken-fragment-links` | Links, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `orphan-page` | Links, Warning | 0/3/1 | 0 proven; j's 3 unproven (their linking pages answered 429) | 0 | fixed | Skipped (`pages-unread`) when 1 in 20 of the pages tried couldn't be read | `54caf2d` |
| `noindex-page` | Indexability, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `nofollow-page` | Indexability, Warning | 0/0/0 | – | 0 | right, not seen | Notice beside noindex: not done (see below) | – |
| `blocked-by-robots` | Indexability, Notice | 0/4/0 | 0 | 0 | right | Backlog: judge Googlebot's own group too | – |
| `snippet-blocked` | Indexability, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `robots-txt-unreachable` | Indexability, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `robots-txt-unreachable-other-origin` | Indexability, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `robots-txt-missing` | Indexability, Notice | 0/0/0 | – | 0 | right, not seen | New-check idea from g (below) | – |
| `site-hidden-from-search` | Indexability, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonicalised-page` | Canonicals, Notice | 7/0/3 | 0 | 0 | reworded | Simple: page 2 of a list isn't a copy, and should name itself | `b84ee69` |
| `canonical-missing` | Canonicals, Notice | 0/0/8 | 0 | 0 | right | – | – |
| `canonical-multiple` | Canonicals, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonical-broken` | Canonicals, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonical-to-redirect` | Canonicals, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonical-to-non-indexable` | Canonicals, Warning | 0/0/0 | – | 0 | fixed, not seen | A target that refused the crawler (firewall, 429) is unverified, not non-indexable; "or a firewall" dropped | `8220bce` |
| `canonical-outside-head` | Canonicals, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonical-relative` | Canonicals, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `canonical-invalid` | Canonicals, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `noindex-with-canonical` | Canonicals, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `sitemap-non-indexable` | Sitemaps, Warning | 0/0/1 | 0 | 0 | right | – | – |
| `not-in-sitemap` | Sitemaps, Notice | 0/4/18 | 0 (13 of g's are real products the sitemap lacks) | 0 | reworded | Login, basket and account pages belong out of search, not in the sitemap | `b84ee69` |
| `sitemap-error` | Sitemaps, Warning | 0/0/0 | – | 0 | severity changed, not seen | Error to Warning | `ef19fa0` |
| `sitemap-missing` | Sitemaps, Notice | 0/0/0 | – | 0 | reworded, not seen | Simple says what checking again adds | `b84ee69` |
| `sitemap-url-other-host` | Sitemaps, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `title-missing` | Titles, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `title-multiple` | Titles, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `title-too-long` | Titles, Notice | 14/0/84 | 0 | 0 | reworded; changed: pixels | Simple: "Titles that may be cut off in search results". Then pixels: over 580 px, not 60 characters (11/0/69 after) | `b84ee69`, `7047d4b` |
| `title-too-short` | Titles, Notice | 3/6/10 | 0 | 0 | reworded; changed: pixels | Simple step: skip login and basket pages. Then pixels: under 200 px, not 30 characters (2/3/8 after) | `b84ee69`, `7047d4b` |
| `title-duplicate` | Titles, Warning | 0/0/2 | 0 | 0 | right | – | – |
| `description-missing` | Meta descriptions, Notice | 0/6/12 | 0 | 0 | right | – | – |
| `description-multiple` | Meta descriptions, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `description-too-long` | Meta descriptions, Notice | 0/2/203 | 0 | 0 | reworded; changed: pixels | Simple: "Search descriptions that may be cut off". Then pixels: over 985 px, not 155 characters (0/2/163 after) | `b84ee69`, `7047d4b` |
| `description-too-short` | Meta descriptions, Notice | 1/1/0 | 0 | 0 | right; changed: pixels | Pixels: under 400 px, not 70 characters (0/1/0 after) | `7047d4b` |
| `description-duplicate` | Meta descriptions, Notice | 0/0/4 | 0 | 0 | severity changed | Warning to Notice | `ef19fa0` |
| `h1-missing` | Headings, Notice | 0/2/1 | 0 | 0 | right | – | – |
| `h1-multiple` | Headings, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `h1-duplicate` | Headings, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `low-word-count` | Content, Notice | 1/3/9 | 0 | 0 | right | Cookie-banner words: not done (see below) | – |
| `duplicate-content` | Content, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `near-duplicate-content` | Content, Notice | 0/0/0 | – | 0 | reworded, not seen | What drops "24 of the 256 bits" | `b84ee69` |
| `duplicate-site-version` | Content, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `image-missing-alt` | Images, Warning | 0/0/0 | – | 0 (gap, no real case: an image whose only source is a `data:` URI) | right, not seen | – | – |
| `image-alt-too-long` | Images, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `image-missing-dimensions` | Images, Notice | 17/10/225 | 0 against the HTML (CSS sizes 469 of g's 4,153; only 2 pages flagged for those alone) | 0 | right | – | – |
| `image-broken` | Images, Error | 1/1/0 | 0 | 0 | right | – | – |
| `image-too-large` | Images, Warning | 24/0/60 | 0 | 0 | right | – | – |
| `hreflang-invalid-code` | Hreflang, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-missing-self` | Hreflang, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-missing-return` | Hreflang, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-broken-target` | Hreflang, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-to-non-indexable` | Hreflang, Warning | 0/0/0 | – | 0 | fixed, not seen | A refused alternate is unverified; another site's robots.txt doesn't count; words in both modes | `8220bce` |
| `hreflang-duplicate-code` | Hreflang, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-missing-x-default` | Hreflang, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `hreflang-outside-head` | Hreflang, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `structured-data-invalid` | Structured data, Warning | 0/0/0 | – | 0 | severity changed, not seen | Error to Warning | `ef19fa0` |
| `structured-data-missing-required` | Structured data, Warning | 0/0/0 | – | 0 (gap: subtypes never checked) | fixed, not seen | Subtypes need their parent's properties; the FAQ date checked and kept | `15984e5` |
| `no-https` | Security, Error | 0/0/0 | – | 0 | right, not seen | – | – |
| `http-page` | Security, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `mixed-content` | Security, Error | 1/0/0 | 0 | 0 | right | – | – |
| `security-headers-missing` | Security, Notice | 1/1/1 | 0 | 0 | reworded | Why: browsers already default Referrer-Policy | `b84ee69` |
| `url-uppercase` | URLs, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `url-underscores` | URLs, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `url-too-long` | URLs, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `slow-response` | Performance, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `large-page` | Performance, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `html-past-crawl-limit` | Performance, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `viewport-missing` | HTML, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `lang-missing` | HTML, Notice | 0/0/0 | – | 0 | right, not seen | – | – |
| `charset-missing` | HTML, Notice | 0/0/0 | – | 0 (gap: a meta tag past 1024 bytes passed; j's is at byte 21,898) | fixed, not seen | A meta tag past the first 1024 bytes declares nothing on its own | `9f0d76c` |
| `open-graph-missing` | HTML, Notice | 0/0/12 | 0 | 0 | right | – | – |

"Fired" counts flagged pages, or URLs for a check whose subject is the URL, in each crawl's own analysis before the
fixes. "Not seen" means it fired on none of the three. The audits' working files, with every URL id and HTML snippet
behind these rows, were `accuracy-indexing.md`, `accuracy-responses.md` and `accuracy-onpage.md` in the session's
scratchpad.

## What changed

Each logic fix has a planted case on the synthetic site, or a crawl test, that failed with the rule fix taken out and
passes with it; the whole synthetic site is compared issue by issue, so nothing else moved.

| Commit | Change | Proven by |
| --- | --- | --- |
| `8220bce` | `canonical-to-non-indexable` and `hreflang-to-non-indexable`: a target that refused the crawler (`FetchOutcome.Blocked`: a firewall, a bot challenge, a 429) is unverified, as the sitemap checks already took it, not non-indexable. `hreflang-to-non-indexable` no longer counts an alternate on another site that its own robots.txt keeps the crawler from (canonicals already didn't). Words in both modes; "or a firewall" gone | `/plants/canonical-blocked`, `/plants/hreflang-unverified` (the partner site now has a robots.txt disallowing `/ext-private/`; a language switcher links the alternates, as hreflang isn't crawled by default). Both parts of the hreflang fix fail on their own |
| `6921d66` | `links-to-redirects` counts only permanent redirects (301, 308, an instant meta refresh): a link to a 302, 303 or 307 keeps the URL that should be linked. `redirect-temporary` leaves out a temporary redirect to another site (a hosted login or checkout). Words in both modes | `/plants/temporary-links` links `/plants/account-login` (302 to the partner site) and `/plants/sale` (302 within the site, still flagged). Products linking `/r2/` (302 to 301) no longer count as linking to redirects |
| `a3b83c1` | `empty-anchor-text` tests a page's links to itself (the home page's logo), but not links written as a fragment (`#`, `#top`). A bare `#` is now stored as an empty fragment so the two can be told apart | `/plants/empty-self-link`; `UrlNormalizerTests` |
| `54caf2d` | `orphan-page` is skipped, with the new reason `pages-unread`, when the site's pages that were refused, rate-limited, timed out or gave no answer are 1 in 20 or more of the pages the crawl tried to read. A few stray failures don't stop it (see below) | `SkippedCheckCrawlTests`: a shop whose products answer 429, at 2 of 4 pages (skipped) and 1 of 22 (run) |
| `15984e5` | `structured-data-missing-required` holds schema.org subtypes to their parent's requirements: LocalBusiness's 127 (Restaurant, Store, FurnitureStore...), Event's 17 (MusicEvent, SportsEvent...), three of Product's, three of Review's, EmployerAggregateRating. ProductGroup, Car and ClaimReview have requirements of their own and aren't mapped; NewsArticle and BlogPosting need nothing, as Article requires nothing | `/plants/jsonld-subtypes`; `PageRuleTests` |
| `9f0d76c` | `charset-missing`: a meta charset that ends past the first 1024 bytes declares nothing when neither the header nor a byte-order mark does. The single parse records where the declaring tag ends (`PageFacts.DeclaredCharsetEnd`, from AngleSharp's element positions, counted in the encoding it names); the detail says where | `/plants/charset-late` (flagged), `/plants/charset-meta` (not); `HtmlPageParserTests` |
| `ef19fa0` | Severities: `sitemap-error` Error to Warning (its own words: well-linked pages are still found), `structured-data-invalid` Error to Warning (the same effect as missing required properties, a warning; one stray comma no longer caps the score at 89), `description-duplicate` Warning to Notice (the effect of a missing description, a notice) | The score tests: the design's garden crawl (2026-10-31) now scores 87, not 85, and fixing its first five leaves 100; the design's worked example keeps 85 with another warning in its place |
| `b84ee69` | Words, both modes: `canonicalised-page` (page 2 of a list isn't a copy and should name itself), `not-in-sitemap` (login, basket and account pages belong out of search), `sitemap-missing` (what checking again adds), `title-too-long` and `description-too-long` ("may be cut off"), `title-too-short` (skip login and basket pages), `near-duplicate-content` (no "24 of the 256 bits"), `broken-external-links` (refused and robots-disallowed links aren't counted), `security-headers-missing` (browsers already default Referrer-Policy) | `IssueCatalogTests`, the plain-words template and data tests, `docs/v2-ui-simple.md`'s ten checks |
| `97e3352` | `AnalysisFreshness.VERSION` 9: analyses by 8 are out of date, so the app offers to run them again | – |
| `7047d4b` | After the audit, at the owner's word: `title-too-long`, `title-too-short`, `description-too-long` and `description-too-short` measure the width in Google's desktop results in pixels, not characters: over 580 px or under 200 px for titles, over 985 px or under 400 px for descriptions. Details read `612 px, over 580 (64 characters)`; Simple and the report's plain words say where Google will likely cut. Limits saved in characters are converted. `AnalysisFreshness.VERSION` 12. The design as built: [pixel widths](v2-pixel-widths.md) | Six plants where characters and pixels disagree, each judged the other way by characters (`4268406`: `/plants/title-caps`, `title-narrow`, `title-narrow-short`, `title-caps-short`, `description-caps`, `description-narrow`); `SearchResultWidthTests`, `PageRuleTests` |
| `e02b185` | After the audit, at the owner's word (2026-11-19): `broken-external-links` Warning to Error (a broken link is an error, on the site or off it) and `canonical-to-first-page` Warning to Notice (canonicalising the later pages of a series to the first is a common and often deliberate setup), its words rewritten in both modes: what it does, when it matters (what's listed only on later pages belongs in the sitemap or linked from elsewhere), and a fix that is a choice. `non-indexable-inlinks-only` and `canonicalised-page` no longer say each page of a list should name itself. No rule changed | The score tests: the design's badly broken site keeps 36 with another warning in place of the broken external links, and scores 34 as it is now; the report's first five on the synthetic site; `ScoreReference/cases.json` regenerated. The garden crawl (2026-10-31) scores 87: 2 errors, 3 warnings and 23 notices |

**Why 1 in 20 for orphans.** The audit asked for the check to be skipped whenever the site's pages couldn't all be
read. Taken literally, one URL that times out, or a "Log in" link to a `/wp-login.php` that answers bots with 403,
would switch the check off on most large sites, and on the synthetic site, which plants a blocked page and an
unreachable one. A
share keeps the jitsunami case (12 of 22 refused) and leaves a stray failure alone. Below it, a page linked only from a
page that was refused is still reported; the limitation is in [v2-rebuild.md](v2-rebuild.md), "Known limitations".
robots.txt-disallowed pages don't count: search engines can't read them either.

**A crawl made before these fixes, analysed again**, gets the rule changes but not the two new facts the crawl records:
a meta charset's position is unknown, so a late one still passes; and a link written `#` was stored like a link to the
page's own address, so an icon link written `#` with no label counts as a link without text (jitsunami's collection
page shows it: its grid-layout toggles). Crawling again mends both.

**Before and after on the three crawls** (copies, analysed again with the fixes): aptitude 85, every count unchanged;
jitsunami 76 to 79: `links-to-redirects` 10 to 0, `redirect-temporary` 1 to 0, `orphan-page` 3 to skipped ("12 of the
22 pages the crawl tried to read refused it or didn't answer"), `empty-anchor-text` 9 to 10 (its home page's logo, and
on the collection page the `#` toggles above); garden 83 to 85, every count unchanged (`description-duplicate` is now a
notice).

## Not done, and why

- **`internal-blocked`**: the title ("refused the crawler", as 429s aren't firewalls) and Warning to Notice, and the
  Simple words, belong to `ui/3b-polite`, which owns rate limiting and the "blocking the crawler" notice.
- **Cookie-banner words in the word count** (`PageWalker.cs:44`, 54 words on every garden page): excluding consent
  regions changes every page's content hash and fingerprint, so the synthetic site's duplicate and near-duplicate
  plants and the bench would need re-baselining. No garden page sits at 200 to 259 words, so it hid no flag here.
- **`nofollow-page` as a Notice when the page is also noindex**: a deliberate `noindex, nofollow` login page is a fair
  case, but no site had one, and a severity that depends on another directive is a new kind of rule. Not seen.
- **Details that name the limit** ("61 characters (limit 60)") and the full-width parenthetical only when it changes
  the verdict: details belong to `ui/3b-evidence`, which is rewriting them. Pixels have since taken the parenthetical
  away: a wide character takes its room in the width (`7047d4b`).
- **`blocked-by-robots` judged for Googlebot's own group**: rules are matched for the crawler's token, falling back to
  `*`. None of the sites has a Googlebot group, so nothing here was misjudged; backlog.
- **`soft-404` wording** ("We can't find the page you're looking for", "This page isn't available") and
  **`image-missing-alt` on an image whose only source is a `data:` URI**: gaps found by reading, with no real case.
  Backlog.
- **`mixed-content` stays an Error**: browsers block mixed scripts, styles and frames outright; images, which they now
  upgrade, are the weaker case, and `image-broken` reports the one that fails.

## New checks for the backlog

From what the sites showed, not built here:

- **"robots.txt doesn't name your sitemap"** (Notice). Garden's robots.txt is an empty 200, and its only sitemap was
  found by guessing `/sitemap.xml`. Fires when no robots.txt on a main origin has a `Sitemap:` line and a sitemap was
  found only by the probe or from the settings.
- **"Paginated pages canonicalised to page 1"** (Warning as proposed; built as `canonical-to-first-page`, and a Notice
  since 2026-11-19 at the owner's word: it's a common and often deliberate setup). Garden's `/garden-sheds/?p=2`,
  `/greenhouses/?p=2` and `/blog/?p=2` name page 1 as canonical though each lists its own products or posts, which are
  then reached only through pages asking to be dropped. `canonicalised-page` reports them today, as a Notice beside
  textbook parameter variants.

## The second audit, 2026-11-24

Before the repository is shown in public, every one of the 170 checks was held to its standard again, and the three
sites crawled again. The first audit proved the 87 checks of its day; this one weighed the 84 added since
([the new checks](v2-new-checks.md)), which had met the least real HTML. The fixes are on `audit/checks`.

### In short

- **170 checks reviewed, 42 fired** on at least one of the three sites; 128 on none ("not seen": proven on the synthetic
  site only).
- **Wrong flags on the three sites: none.** Every flag of every check that fired was recomputed from the stored HTML,
  headers and link rows with parsers that aren't the engine's (Python's `html.parser`, lxml, regexes, SQL), and read by
  hand where a check fired fewer than ten times.
- **Misses on the three sites: none that a check's definition covers.** One near miss: aptitude's posts with code samples
  scored 31.1 on `readability-low` against its limit of 30, the code's words counted into the prose's sentences.
- **Faults found by reading the rules against their standards: 23 checks fixed**, none of them seen on these sites: 20
  flagged correct markup, 3 missed a fault. The one that mattered most: a canonical link with `hreflang`,
  `lang`, `media` or `type` was taken as the page's canonical, though Google says it isn't used.
- **Words changed on 9 checks**, one severity (`canonical-invalid-attribute`, Notice to Warning).
- **Left, each with its reason below: 5.**
- **`AnalysisFreshness.VERSION` 25:** analyses by 24 are out of date, so the app offers to run them again.

### How it was checked

**Reading.** Four reviews, one per group of rule files (indexing; responses, links, security and URLs; on-page; images
and structured data), each check's rule, facts and words against the standard it names: Google Search Central (read on
the day for canonicals, Googlebot's file limits, product snippets, job postings, video, review snippets and the FAQ
notice), RFC 9110 and 9309, BCP 47, the HTML standard, sitemaps.org, schema.org. Each suspect was traced to a concrete
input before anything changed, and each fix has a test that failed without it.

**The sites**, crawled on 2026-11-24 with the CLI at its default polite pace, `--max-urls 1500 --store-html`:

| Site | URLs fetched | Pages read | Crawl | Notes |
| --- | ---: | ---: | --- | --- |
| `www.aptitude.co.uk` (a) | 627 | 33 | 1.5 min | |
| `www.gardenfurnituresets.co.uk` (g) | 1,543 | 260 | 0.6 min | The limit kept 3,083 URLs out, 2,922 of them images: the image-file checks (`image-too-large`, `image-legacy-format`, `image-no-srcset`, `image-broken`) judged the 1,147 fetched. Every page was read |
| `jitsunami.com` (j) | 76 | 10 | 2 min | Shopify refused its 13 product and collection URLs for want of a signature: they are "not checked", and its product markup is still unaudited |

`s` in the table is a check the crawl skipped (the orphan family on g, for the URL limit, and on j, for the refused
pages).

### Every check

| Check | Category, severity now | Fired a/g/j | Verdict | Change or note | Commit |
| --- | --- | --- | --- | --- | --- |
| `internal-client-error` | Response codes, Error | 0/8/1 | right | – | – |
| `internal-server-error` | Response codes, Error | 0/0/0 | right, not seen | – | – |
| `internal-unreachable` | Response codes, Error | 0/0/0 | right, not seen | – | – |
| `internal-blocked` | Response codes, Notice | 0/0/0 | right, not seen | – | – |
| `content-type-mismatch` | Response codes, Warning | 0/0/0 | fixed, reworded, not seen | All sixteen JavaScript media types of the HTML standard are a script's (`text/x-javascript`…). Words: any URL, not only internal; "many sites" send nosniff | `94a6ad60`, `d655c008` |
| `soft-404` | Response codes, Warning | 0/0/0 | right, not seen | – | – |
| `redirect-chain` | Redirects, Warning | 0/0/0 | right, not seen | – | – |
| `redirect-loop` | Redirects, Error | 0/0/0 | right, not seen | – | – |
| `redirect-broken` | Redirects, Error | 0/0/0 | right, not seen | – | – |
| `redirect-temporary` | Redirects, Notice | 0/0/0 | right, not seen | – | – |
| `redirect-temporary-cached` | Redirects, Notice | 0/0/0 | right, not seen | – | – |
| `redirect-downgrade` | Redirects, Error | 0/0/0 | right, not seen | – | – |
| `meta-refresh` | Redirects, Warning | 0/0/0 | right, not seen | – | – |
| `broken-internal-links` | Links, Error | 0/4/0 | right | – | – |
| `broken-external-links` | Links, Error | 8/1/0 | right | – | – |
| `links-to-redirects` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `internal-nofollow-links` | Links, Warning | 0/0/0 | right, not seen | – | – |
| `empty-anchor-text` | Links, Notice | 0/260/10 | right | – | – |
| `broken-fragment-links` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `orphan-page` | Links, Warning | 0/s/s | right, not seen | – | – |
| `uncrawlable-links` | Links, Warning | 0/0/0 | right, not seen | – | – |
| `broken-resources` | Links, Warning | 0/0/0 | right, not seen | – | – |
| `links-to-localhost` | Links, Warning | 0/0/0 | right, not seen | – | – |
| `few-internal-outlinks` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `generic-anchor-text` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `non-indexable-inlinks-only` | Links, Notice | 0/s/s | right, not seen | – | – |
| `deep-page` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `too-many-links` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `slow-external-links` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `pagination-not-linked` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `pagination-broken` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `pagination-to-non-indexable` | Links, Notice | 0/2/0 | right | – | – |
| `pagination-sequence-error` | Links, Notice | 0/0/0 | right, not seen | – | – |
| `noindex-page` | Indexability, Notice | 0/0/0 | right, not seen | – | – |
| `nofollow-page` | Indexability, Warning | 0/0/0 | right, not seen | – | – |
| `blocked-by-robots` | Indexability, Notice | 0/0/4 | right | – | – |
| `snippet-blocked` | Indexability, Notice | 0/0/0 | right, not seen | – | – |
| `robots-txt-unreachable` | Indexability, Error | 0/0/0 | right, not seen | – | – |
| `robots-txt-unreachable-other-origin` | Indexability, Warning | 0/0/0 | right, not seen | – | – |
| `robots-txt-missing` | Indexability, Notice | 0/0/0 | right, not seen | – | – |
| `site-hidden-from-search` | Indexability, Error | 0/0/0 | right, not seen | – | – |
| `robots-directives-conflict` | Indexability, Warning | 0/0/0 | right, not seen | – | – |
| `noindex-near-home` | Indexability, Warning | 0/0/0 | right, not seen | – | – |
| `unavailable-after-passed` | Indexability, Warning | 0/0/0 | right, not seen | – | – |
| `media-preview-blocked` | Indexability, Notice | 0/0/0 | reworded, not seen | Only `noimageindex` keeps images out of search; the two preview rules only remove the preview. The fix names `noimageindex` | `d655c008` |
| `translation-blocked` | Indexability, Notice | 0/0/0 | right, not seen | – | – |
| `indexifembedded-without-noindex` | Indexability, Notice | 0/0/0 | right, not seen | – | – |
| `canonicalised-page` | Canonicals, Notice | 7/0/0 | fixed | No longer reported for a page whose only canonical has an alternate's attributes | `1851544c` |
| `canonical-missing` | Canonicals, Notice | 0/8/0 | right | – | – |
| `canonical-multiple` | Canonicals, Warning | 0/0/0 | right; left, not seen | The first canonical still decides indexability, though Google likely ignores them all (see below) | – |
| `canonical-broken` | Canonicals, Error | 0/0/0 | right, not seen | – | – |
| `canonical-to-redirect` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-to-non-indexable` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-outside-head` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-relative` | Canonicals, Notice | 0/0/0 | right, not seen | – | – |
| `canonical-invalid` | Canonicals, Warning | 0/0/0 | reworded, not seen | A template placeholder resolves as a relative URL, so it isn't an example of this check | `d655c008` |
| `noindex-with-canonical` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-to-first-page` | Canonicals, Notice | 0/3/0 | right | – | – |
| `canonical-other-domain` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-loop` | Canonicals, Warning | 0/0/0 | right, not seen | – | – |
| `canonical-on-redirect` | Canonicals, Notice | 0/0/0 | right, not seen | – | – |
| `canonical-fragment` | Canonicals, Notice | 0/0/0 | right, not seen | – | – |
| `canonical-invalid-attribute` | Canonicals, Warning | 0/0/0 | fixed, severity changed, reworded, not seen | Google: such a link is "not used for canonicalization". It no longer canonicalises the page; Notice to Warning; the Why no longer says no Google page says so | `1851544c`, `d655c008` |
| `canonical-unlinked` | Canonicals, Notice | 0/s/s | right, not seen | – | – |
| `sitemap-non-indexable` | Sitemaps, Warning | 0/0/0 | right, not seen | – | – |
| `not-in-sitemap` | Sitemaps, Notice | 0/18/4 | right | Noisy on login and basket pages, as its words say | – |
| `sitemap-error` | Sitemaps, Warning | 0/0/0 | right, not seen | – | – |
| `sitemap-missing` | Sitemaps, Notice | 0/0/0 | right, not seen | – | – |
| `sitemap-url-other-host` | Sitemaps, Warning | 0/0/0 | right, not seen | – | – |
| `robots-txt-no-sitemap` | Sitemaps, Notice | 0/1/0 | fixed | A Sitemap line is read as the parser reads it: `Sitemap :` counts, an empty or commented one doesn't | `94a6ad60` |
| `sitemap-over-limit` | Sitemaps, Warning | 0/0/0 | right, not seen | – | – |
| `sitemap-near-limit` | Sitemaps, Notice | 0/0/0 | right, not seen | – | – |
| `sitemap-empty` | Sitemaps, Notice | 0/0/0 | right, not seen | – | – |
| `sitemap-url-repeated` | Sitemaps, Notice | 0/0/0 | right, not seen | – | – |
| `sitemap-lastmod-invalid` | Sitemaps, Notice | 0/0/0 | right, not seen | – | – |
| `title-missing` | Titles, Error | 0/0/0 | right, not seen | – | – |
| `title-multiple` | Titles, Warning | 0/0/0 | right, not seen | – | – |
| `title-too-long` | Titles, Notice | 11/70/0 | right | – | – |
| `title-too-short` | Titles, Notice | 2/7/3 | right | – | – |
| `title-duplicate` | Titles, Warning | 0/2/0 | right | – | – |
| `description-missing` | Meta descriptions, Notice | 0/11/6 | right | – | – |
| `description-multiple` | Meta descriptions, Warning | 0/0/0 | right, not seen | – | – |
| `description-too-long` | Meta descriptions, Notice | 0/163/2 | right | – | – |
| `description-too-short` | Meta descriptions, Notice | 0/0/1 | right | – | – |
| `description-duplicate` | Meta descriptions, Notice | 0/4/0 | right | – | – |
| `h1-missing` | Headings, Notice | 0/1/2 | right | – | – |
| `h1-multiple` | Headings, Notice | 0/0/0 | right, not seen | – | – |
| `h1-duplicate` | Headings, Notice | 0/0/0 | right, not seen | – | – |
| `heading-levels-skipped` | Headings, Notice | 10/81/8 | right | – | – |
| `h1-image-only` | Headings, Notice | 0/0/0 | fixed, not seen | Not beside `h1-missing` when the image's alt is empty | `94a6ad60` |
| `heading-too-long` | Headings, Notice | 1/53/0 | right | – | – |
| `h2-missing` | Headings, Notice | 1/0/0 | right | – | – |
| `low-word-count` | Content, Notice | 1/8/3 | right | – | – |
| `duplicate-content` | Content, Warning | 0/0/0 | right, not seen | – | – |
| `near-duplicate-content` | Content, Notice | 0/0/0 | right, not seen | – | – |
| `duplicate-site-version` | Content, Warning | 0/0/0 | right, not seen | – | – |
| `placeholder-text` | Content, Warning | 0/0/1 | right | – | – |
| `readability-low` | Content, Notice | 0/0/0 | fixed, not seen | Only blocks of text with a sentence end are judged: lists, grids and code made every sentence look longer (aptitude's code posts lost 30 points, to 31.1 against the limit of 30) | `e1e584d8` |
| `boilerplate-heavy` | Content, Notice | 0/0/0 | fixed, reworded, not seen | A `<noscript>`'s markup isn't the template's words. Words: menus and footer, not header and sidebars | `e1e584d8`, `d655c008` |
| `image-missing-alt` | Images, Warning | 0/0/0 | right, not seen | An `<img>` with no URL is `image-missing-src`'s alone (aptitude's one) | – |
| `image-alt-too-long` | Images, Notice | 0/0/0 | right, not seen | – | – |
| `image-missing-dimensions` | Images, Notice | 17/225/10 | right | – | – |
| `image-broken` | Images, Error | 1/0/1 | right | – | – |
| `image-too-large` | Images, Warning | 24/24/0 | right | – | – |
| `image-missing-src` | Images, Warning | 1/0/0 | fixed, reworded | An `<img>` in a `<picture>` shows its sources. Words: "doesn't show unless a script gives it one"; data-src is common, not standard | `94a6ad60`, `d655c008` |
| `image-no-srcset` | Images, Notice | 25/60/1 | right | – | – |
| `image-legacy-format` | Images, Notice | 28/81/0 | right | – | – |
| `image-alt-unhelpful` | Images, Notice | 7/0/0 | fixed | A one- or two-character name in capitals is a name (`X`, `TV`, `4K`) | `94a6ad60` |
| `image-hotlinked` | Images, Notice | 1/0/0 | fixed | 24 more hosts a site rents (Webflow, Squarespace, GoDaddy, Duda, HubSpot, S3, GCS, Azure, Bunny, Cloudflare Images, ImageKit, Sanity…) | `94a6ad60` |
| `image-inline-large` | Images, Notice | 0/0/0 | right, not seen | – | – |
| `hreflang-invalid-code` | Hreflang, Error | 0/0/0 | right, not seen | – | – |
| `hreflang-missing-self` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-missing-return` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-broken-target` | Hreflang, Error | 0/0/0 | right, not seen | – | – |
| `hreflang-to-non-indexable` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-duplicate-code` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-missing-x-default` | Hreflang, Notice | 0/0/0 | right, not seen | – | – |
| `hreflang-outside-head` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-inconsistent-code` | Hreflang, Warning | 0/0/0 | right, not seen | – | – |
| `hreflang-header-mismatch` | Hreflang, Notice | 0/0/0 | right, not seen | – | – |
| `hreflang-unlinked` | Hreflang, Notice | 0/s/s | right, not seen | – | – |
| `hreflang-lang-mismatch` | Hreflang, Notice | 0/0/0 | fixed, not seen | Two codes for one language aren't a mismatch (`no`/`nb`, `tl`/`fil`, `he`/`iw`, `zh`/`cmn`) | `94a6ad60` |
| `hreflang-self-only` | Hreflang, Notice | 0/0/0 | right, not seen | – | – |
| `structured-data-invalid` | Structured data, Warning | 0/0/0 | fixed, not seen | A script typed `application/ld+json; charset=utf-8` is read | `e1e584d8` |
| `structured-data-missing-required` | Structured data, Warning | 0/0/0 | fixed, not seen | JobPosting: `applicantLocationRequirements` stands in for `jobLocation`. Microdata follows `itemref` | `94a6ad60`, `e1e584d8` |
| `structured-data-schema-errors` | Structured data, Warning | 0/0/0 | right, not seen | – | – |
| `structured-data-missing-recommended` | Structured data, Notice | 0/0/0 | fixed, not seen | An offer's currency may be its `priceSpecification`'s, as Google's product page says (WooCommerce) | `94a6ad60` |
| `structured-data-on-noindex` | Structured data, Notice | 0/0/0 | reworded, not seen | Normal where a plugin adds Organization or BreadcrumbList to every page | `d655c008` |
| `no-https` | Security, Error | 0/0/0 | right, not seen | – | – |
| `http-page` | Security, Warning | 0/0/0 | right, not seen | – | – |
| `mixed-content` | Security, Error | 1/0/0 | fixed | Not reported when the page's CSP has `upgrade-insecure-requests` | `94a6ad60` |
| `security-headers-missing` | Security, Notice | 1/1/1 | right; left | A CSP or referrer policy given only in a meta tag reads as missing (see below) | – |
| `security-headers-weak` | Security, Notice | 0/1/1 | right | – | – |
| `cookies-insecure` | Security, Notice | 0/4/10 | fixed | A CSRF token cookie (`XSRF-TOKEN`, `csrftoken`) isn't asked for HttpOnly: a script must read it | `94a6ad60` |
| `form-not-secure` | Security, Warning | 0/0/0 | right, not seen | – | – |
| `protocol-relative-links` | Security, Notice | 0/0/0 | fixed, not seen | A `dns-prefetch` hint written `//` isn't counted | `e1e584d8` |
| `url-uppercase` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-underscores` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-too-long` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-tracking-parameters` | URLs, Notice | 0/0/0 | fixed, not seen | Every `utm_` parameter counts (GA4's `utm_source_platform`…) | `94a6ad60` |
| `url-multiple-slashes` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-space` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-parameters` | URLs, Notice | 0/1/0 | right | – | – |
| `url-non-ascii` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `url-repetitive-path` | URLs, Notice | 0/0/0 | fixed, not seen | Digit-only segments repeat nothing (`/2025/11/11/`) | `94a6ad60` |
| `url-internal-search` | URLs, Notice | 0/0/0 | right, not seen | – | – |
| `slow-response` | Performance, Warning | 0/29/0 | right | – | – |
| `large-page` | Performance, Notice | 0/0/0 | right, not seen | – | – |
| `html-past-crawl-limit` | Performance, Warning | 0/0/0 | left, not seen | On a rendered page the size judged is the rendered DOM's, not the fetched HTML's (see below) | – |
| `resource-past-crawl-limit` | Performance, Warning | 0/0/0 | reworded; left, not seen | The fix no longer says to compress: Google's limit is on the decompressed size. Left: the crawl stores only the compressed size of scripts and stylesheets, and none for a chunked response, so a large compressed file is missed (see below) | `d655c008` |
| `viewport-missing` | HTML, Warning | 0/0/0 | right, not seen | – | – |
| `lang-missing` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `charset-missing` | HTML, Notice | 0/0/0 | fixed, not seen | `charset = utf-8` with spaces is a declaration | `e1e584d8` |
| `open-graph-missing` | HTML, Notice | 0/11/0 | right | – | – |
| `x-card-missing` | HTML, Notice | 0/3/0 | right; words unverified | X's documentation still can't be read (see below) | – |
| `x-card-incomplete` | HTML, Notice | 0/6/0 | right; words unverified | The six flags are true to the tags; what X then shows is unverified (see below) | – |
| `social-image-relative` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `og-url-not-canonical` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `social-tags-multiple` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `og-title-duplicate` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `og-description-duplicate` | HTML, Notice | 0/0/6 | right | – | – |
| `social-image-broken` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `social-image-too-large` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `social-image-too-small` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `head-tags-in-body` | HTML, Warning | 0/0/0 | right, not seen | – | – |
| `html-structure-errors` | HTML, Notice | 0/0/0 | right, not seen | – | – |
| `viewport-invalid` | HTML, Warning | 0/0/0 | right, not seen | – | – |
| `charset-invalid` | HTML, Notice | 0/0/0 | fixed, reworded, not seen | An http-equiv charset ends at a semicolon or space (`utf-8;`). The fix no longer holds markup | `e1e584d8`, `d655c008` |
| `lang-invalid` | HTML, Notice | 0/0/0 | fixed, not seen | Extended language subtags (`zh-cmn-Hans`, `zh-yue`) and the renamed codes (`iw`, `in`, `ji`, `jw`) are well-formed | `94a6ad60` |
| `mobile-alternate` | HTML, Notice | 0/0/0 | fixed, not seen | Not an alternate stylesheet, a typed feed or a print version | `e1e584d8` |

**Before and after on the three crawls** (copies, analysed again with the fixes): every count on every site is
unchanged, as none of the faults fixed was on them.

### What changed

| Commit | Change | Proven by |
| --- | --- | --- |
| `94a6ad60` | Fourteen rules: `robots-txt-no-sitemap`, `hreflang-lang-mismatch`, `lang-invalid`, `url-repetitive-path`, `url-tracking-parameters`, `cookies-insecure`, `mixed-content`, `content-type-mismatch`, `h1-image-only`, `image-missing-src`, `image-hotlinked`, `image-alt-unhelpful`, `structured-data-missing-recommended`, `structured-data-missing-required` (JobPosting), each as its row says | 23 rule tests seen failing with the rules as they were (`SecurityUrlCheckTests`, `TextHreflangCheckTests`, `HeadCheckTests`, `ImageCheckTests`, `PageRuleTests`, `ResourceTypesTests`, `StructuredDataCheckTests`, `SiteFileCheckTests`) |
| `1851544c` | A canonical link with `hreflang`, `lang`, `media` or `type` is no canonical: the parse keeps the fact (`CanonicalFact.IsHonoured`) and writes no canonical link for it, the crawl doesn't record the page as canonicalised, and `CanonicalRules` and `SiteVisibilityRules` leave it out. Such a page gets `canonical-invalid-attribute` alone, not `canonicalised-page`, `noindex-with-canonical` or `canonical-multiple` | `IndexingCheckTests`, seen failing; the plant `/plants/canonical-alternate` |
| `e1e584d8` | The parse: text blocks are marked (U+2029) so `readability-low` counts the sentences, syllables and words of blocks with a sentence end only (a new fact, `PageFacts.ProseWordCount`; a page parsed before is judged as it was); a `<noscript>`'s raw markup isn't words; an http-equiv charset is read as the header's is; `MobileAlternate` leaves stylesheets, typed files and print; a JSON-LD type may carry parameters; a `dns-prefetch` written `//` isn't counted; Microdata follows `itemref` | Five tests seen failing with only the new fact declared; the content hash and fingerprint are unchanged (the mark is no letter or digit), so every duplicate plant stands |
| `d655c008` | Words of 9 checks in the catalog, 3 of them in plain words too (`plain-words.json`, generated); `canonical-invalid-attribute` a Warning; `AnalysisFreshness.VERSION` 25; two plants: `/plants/canonical-alternate` and `/plants/correct-markup`, a clean page holding five of the cases above (easy prose over a 150-line list, an `<img>` in a `<picture>`, the alt "TV", a `dns-prefetch` written `//`, an `XSRF-TOKEN` cookie); the app's copy of the catalog captured again from the sidecar (`app/src/test/simple/engine-checks.json`) | The whole-table crawl tests (`CrawlEngineTests`), `IssueCatalogTests`, the plain-words tests, the bench |

**A crawl made before these fixes, analysed again**, gets every rule change. What the parse changed needs the site
crawled again: a page stored as canonicalised by a canonical with an alternate's attributes stays so (it now gets
`canonical-invalid-attribute` alone); `readability-low` judges a stored page over all its text, as before; a
`<noscript>`'s markup stays in the stored word total; `utf-8;`, an `itemref` and a `dns-prefetch` are as they were read.

### Left, and why

- **`resource-past-crawl-limit` misses a large file sent compressed or chunked.** Google's 2 MB is of the decompressed
  file. The crawl doesn't read a script's or stylesheet's body: it stores the `Content-Length` (compressed), and nothing
  for a chunked response (garden's combined script, Google Fonts' CSS). Every flag is true, as a compressed file over
  2 MB is larger still; a 6 MB bundle sent as 1.4 MB of brotli is missed. The fix is in the fetcher (count the decoded
  bytes while the file streams by), outside this audit's lane. The words now say compressing doesn't help.
- **`html-past-crawl-limit` and `large-page` on a rendered page** judge the rendered DOM's size, where Google's limit is
  on each fetch. For the rendering lane: keep the fetched body's byte count beside the rendered one.
- **`security-headers-missing` doesn't read a policy given in a meta tag** (`http-equiv="Content-Security-Policy"`,
  `name="referrer"`): both are valid, and read as missing. It needs two facts the parse doesn't collect. None of the
  three sites has either.
- **`canonical-multiple`: the first canonical still decides the page's indexability**, while the check's own words say
  Google will likely ignore them all. A design choice of the crawl's, in every reader of "the first honoured canonical";
  changing it changes stored indexability, so it wants the owner's word.
- **`x-card-missing` and `x-card-incomplete`: the words rest on X's documentation, which still can't be read** (402 and
  404 on 2026-11-24). What's unverified: that X reads `twitter:` tags from `name` only, and that a card with no title or
  image shows as a bare link. Garden's six `x-card-incomplete` flags are true to the tags (an empty `twitter:title` and
  `og:title`).

### Seen on the sites, not faults of a check

- **The image-file checks on a URL-limited crawl are partial and nothing says so.** Garden's limit left 2,922 images
  unfetched; `skipped_checks` and the notes name only the orphan checks. A note for "the limit kept N images out" belongs
  with the coverage notes.
- **`not-in-sitemap` is noisy on `/login/`, `/forgot-password/` and `/cart`**, which its own words say belong out of
  search.
- **`heading-levels-skipped`:** 5 of jitsunami's 8 are the footer's "Info" H4, on every page.
- **Garden's pagers are `<a rel="next">`**, which the parse doesn't take as pagination (`<link>` only, as Google's old
  documentation had it): `/blog/?p=2` was known as a later page by its URL.

### Checks worth adding

Not built here, by the audit's rule:

- Hreflang in XML sitemaps (`xhtml:link`): not read, so not validated and not counted as a return link.
- A robots.txt that answers 200 with HTML, or is past Google's 500 KiB.
- Required properties one level down: an Offer's `price`, an AggregateOffer's `lowPrice`, a rating's `ratingValue`, a
  breadcrumb item's `position`, `name` and `item`.
- Rich result types in Google's gallery with no requirements here: SoftwareApplication, ProfilePage, QAPage,
  DiscussionForumPosting, Course, Dataset, ProductGroup.
- A missing alt on `<input type="image">` and `<area>`, and an H1 hidden with `hidden` or `aria-hidden`.
- `X-Frame-Options` with a value browsers don't know (`ALLOW-FROM`), and a CSP sent only as Report-Only.
- More click ids on internal links (`gbraid`, `wbraid`, `dclid`, `_gl`, `ttclid`, `twclid`, `yclid`).
- og:type and og:url missing (ogp.me requires four properties; two are checked).

## The third audit, 2026-11-24: real-site proof

The second audit left 128 of the 170 checks proven only on the synthetic site: its three sites are well kept, and gave
them nothing to find. This one went looking for sites that would. Every check was run on some forty real sites and
test targets, each first firing was read by hand against the real page, and each site was searched for the opposite
fault, a page that plainly has the problem and wasn't flagged. The fixes are on `fix/checks`.

### In short

- **Of the 128, 101 fired, and 99 are now proven on a real page**: 81 on an ordinary site, 18 only on a target made
  for testing crawlers (crawler-test.com, badssl.com, httpbin). Two fired only wrongly, were fixed, and are unproven
  again (`content-type-mismatch`, `pagination-not-linked`). **27 have still never fired**, each with its reason below.
- **Of the 170, 141 are proven on a real page** (the 42 of the second audit, 41 of which fired again here, and these
  99).
- **Wrong flags: 19 checks flagged what a real site showed to be right.** The largest: 48,195 pages of wordpress.org
  "listed in more than one sitemap" for being in Jetpack's image sitemap; 25,443 gov.uk URLs with a "repeating path"
  that only its sitemaps list; 584 sitemap entries "on another host" where the sitemap's own URL redirects to that
  host; 299 of theregister.com's pages "non-indexable" for a robots.txt that refuses every crawler but the search
  engines'; 132 Wix product photos as "links search engines can't follow"; 71 unrelated pages as near duplicates for
  a header menu counted as their content; 59 of mozilla.org's pages for an `x-default` that does what `x-default` is
  for.
- **Missed cases: 4 checks missed a real fault** (a redirect chain on a crawl of a URL list, "More →" as link text, a
  password field outside a form, and `boilerplate-heavy`, which couldn't fire on a site whose menu isn't a `<nav>`).
- **Evidence: 7 checks said something wrong or useless about a true flag** (a detail cut before the part that matters,
  a parser's advice to a programmer, "2.0 MB, past the 2 MB").
- **29 checks changed, all with a test of the real markup, 31 of the 44 cases seen failing with the rules as they
  were.** `AnalysisFreshness.VERSION` 26.
- **Five faults are the fetcher's, outside this audit's lane, and are left**: the worst reads a page that declares
  `charset=unicode` as UTF-16, which turned berkshirehathaway.com into one page of garbage, flagged for having no
  title, no H1 and no links.

### How it was checked

**The sites**, crawled on 2026-11-24 with the CLI at its default polite pace, robots.txt obeyed, `--store-html`,
`--max-urls` 200 to 400 (3,000 on crawler-test.com, which has 1,213 URLs), rendering left on Automatic. Sites named
by a search for each kind, each checked to answer an ordinary request before it was crawled. None was crawled past a
block or asked again after one. The sites a fix was for were crawled a second time, to see the fix on the real pages.

| Kind | Sites (URLs fetched, pages read) |
| --- | --- |
| Made for testing crawlers | crawler-test.com (1,263, 812), badssl.com with its subdomains (211, 35), httpbin.org and httpbingo.org (a list of 20 status and redirect URLs each), quotes.toscrape.com (304, 234), books.toscrape.com (404, 226), webscraper.io's test shop (316, 179) |
| Old and unmaintained | info.cern.ch (704, 174), spacejam.com/1996 (103, 47), cs.stanford.edu/~knuth (643, 52), paulgraham.com (2,303, 299), neverssl.com, httpforever.com |
| Documentation | postgresql.org/docs (417, 239), sqlite.org (728, 280), create-react-app.dev (113, 1 folder) |
| Multilingual | mozilla.org (1,294, 307), debian.org (7,261, 126), european-union.europa.eu (2,251, 217), videolan.org (2,339, 296), kde.org (3,507, 201), softmaker.com (644, 192), oo-software.com (833, 347) |
| Government and academic | gov.uk (774, 273), mit.edu (737, 23) |
| News and blogs | arstechnica.com (1,879, 126), wordpress.org/news (1,597, 130), omgubuntu.co.uk (1,988, 218; AMP), cnx-software.com (871, 125; AMP) |
| Small businesses on builders | Wix: waldenfloral.com (1,782, 250), pellabakery.com (132, 11). Squarespace: mugipottery.com (690, 74), olasceramics.com (158, 33). WordPress: jonasbrotherstaxidermy.com (344, 27), littletonareachamber.com (735, 161). Shopify: brooklyncandlestudio.com (545, 26), lannacoffeeco.com (344, 52). Webflow: dianasseafood.com (67, 1). Weebly: kilmingtonvillage.com (325, 58) |
| Structured data | kingarthurbaking.com/recipes (889, 167), carrabelle.org (684, 174) |
| Not read | w3.org and text.npr.org (a firewall refused the crawler: `internal-blocked`), theregister.com (its robots.txt refuses every crawler but the ones it names: 300 `blocked-by-robots`), berkshirehathaway.com (one page, garbled: see the fetcher's faults), gnu.org (down), docs.python.org and nasa.gov (stopped after an hour of checking their links to other sites, not analysed, not counted) |

**Seven readers, one per group of checks** (responses and links; indexability; canonicals and sitemaps; titles,
content and URLs; the HTML category; images, security and performance; hreflang and structured data), each working
from the stored crawls, with a live fetch only where the stored page couldn't settle it:

- **Every first firing read by hand**: up to five flagged URLs a check, spread over two sites or more where it fired on
  more, the stored HTML and headers against the check's own words and against the standard it rests on.
- **Counted again without the engine** where a check counts: Python's `html.parser` over every stored page for robots
  directives, viewports, lang, charset, image alts and sizes, hreflang sets, fragment ids and JSON-LD; Windows' own
  `arial.ttf` over 3,551 titles and 1,818 descriptions (no pixel width differed); a shingle cosine over the near
  duplicates; a word count by region over the pages `boilerplate-heavy` flagged.
- **The opposite fault hunted**: each site scanned for the markup a check should have caught, and crawler-test.com's
  named cases compared with what fired on each (a case its robots.txt keeps out, or that the crawl never reached, is
  no miss).
- **Then crawled again** with the fixes, the sites a fix was for (below), as what the parse changed takes a new crawl.

### Every check that had never fired

`–` under samples is a check that didn't fire. A count after a site is the URLs flagged there, before any fix.

| Check | Fired on | Samples read | Verdict | Note |
| --- | --- | ---: | --- | --- |
| `internal-server-error` | create-react-app.dev 1, crawler-test.com 18 and 2 more | 22 | right; left | 21 true. A 101 answer is called a 5xx: the fetcher's classing (see below) |
| `internal-unreachable` | omgubuntu.co.uk 2, badssl.com 35 and 1 more | 8 | right; left | True. A connection given up after 10 s says "20 seconds": the fetcher's message (see below) |
| `internal-blocked` | cnx-software.com 4, brooklyncandlestudio.com 1, w3.org 1 | 5 | right | cnx-software.com: 403 with `Cf-Mitigated: challenge`; brooklyncandlestudio.com: 429 with the same |
| `content-type-mismatch` | crawler-test.com 4 | 4 | fixed | All four were a stray `<link rel="stylesheet" href="#">` naming the page itself. No real mismatch seen |
| `soft-404` | – | – | not seen | Needs a 200 page with an error title or H1 and under 300 words; none crawled |
| `redirect-chain` | debian.org 224, kde.org 16, mozilla.org 3, crawler-test.com 20 and 2 more | 30 | fixed | True. Missed on a crawl of a URL list (httpbin `/redirect/5`): a listed URL is a way in now |
| `redirect-loop` | crawler-test.com 8 | 8 | right; left | True. An instant meta refresh to the page itself isn't traced as a loop (see below) |
| `redirect-broken` | cs.stanford.edu/~knuth 2, create-react-app.dev 1, crawler-test.com 6 and 2 more | 10 | right | All true. The www probe that ends at a 404 (cs.stanford.edu) is reported by design: that address is broken for whoever types it |
| `redirect-temporary` | debian.org 7, oo-software.com 7, sqlite.org 5, httpbin.org 13 and 10 more | 20 | right | – |
| `redirect-temporary-cached` | – | – | not seen | Of 52 temporary redirects whose headers were read, the longest cache was `max-age=600` |
| `redirect-downgrade` | kde.org 55, quotes.toscrape.com 46 and 4 more | 12 | right | kde.org and quotes.toscrape.com redirect HTTPS URLs to http://, confirmed live |
| `meta-refresh` | crawler-test.com 21 | 21 | fixed | 17 true. A script's redirect was described as a tag; the detail and the words now say script |
| `links-to-redirects` | oo-software.com 345, omgubuntu.co.uk 218, european-union.europa.eu 217, quotes.toscrape.com 185 and 19 more | 14 | right; left | True. On crawler-test, 300, 304, 305 and 306 answers count as redirects: the fetcher's classing |
| `internal-nofollow-links` | postgresql.org/docs 239, carrabelle.org 174, cnx-software.com 125, crawler-test.com 8 and 9 more | 6 | right | Noisy: carrabelle.org's 174 are one hidden bot-trap link |
| `broken-fragment-links` | omgubuntu.co.uk 209, kingarthurbaking.com 167, lannacoffeeco.com 52, crawler-test.com 6 and 12 more | 11 | fixed | True but for cnx-software.com's two: an id and its link written with lowercase percent escapes |
| `orphan-page` | mugipottery.com 38, olasceramics.com 8, crawler-test.com 10 | 14 | right | Every one grepped for in every stored page |
| `uncrawlable-links` | waldenfloral.com 130, paulgraham.com 19, pellabakery.com 2 | 135 | fixed | 132 of 151 were Wix's `<div href>` holding a product photo. paulgraham.com's `<xa href>` is true |
| `broken-resources` | paulgraham.com 1, videolan.org 1, crawler-test.com 577 | 3 | right | – |
| `links-to-localhost` | create-react-app.dev 1 | 1 | right | create-react-app.dev links `http://localhost:3000` |
| `few-internal-outlinks` | info.cern.ch 63, spacejam.com 43, cs.stanford.edu/~knuth 7, crawler-test.com 625 and 10 more | 9 | right | berkshirehathaway.com's is the charset fault (see below) |
| `generic-anchor-text` | waldenfloral.com 134, arstechnica.com 81, kde.org 39 and 20 more | 6 | fixed | True. Missed "More →", "Read more …": 53 links on 4 sites |
| `non-indexable-inlinks-only` | – | – | not seen | Skipped on a URL-limited crawl; none on the complete ones |
| `deep-page` | webscraper.io 45 | 3 | right | – |
| `too-many-links` | kingarthurbaking.com 149, debian.org 22, sqlite.org 4 and 6 more | 5 | right | Noisy: a site's own subdomains count as other sites |
| `slow-external-links` | mozilla.org 36, kde.org 21, european-union.europa.eu 6 and 13 more | 4 | right | Noisy: one slow answer flags every page linking to it |
| `pagination-not-linked` | omgubuntu.co.uk 6, cnx-software.com 4 | 3 | fixed | All 10 were AMP copies of a series in order |
| `pagination-broken` | – | – | not seen | No rel=next/prev in any crawl leads to an error or a redirect |
| `pagination-sequence-error` | omgubuntu.co.uk 6, cnx-software.com 4, debian.org 1, crawler-test.com 4 and 1 more | 6 | fixed | 6 true (waldenfloral.com, crawler-test.com, debian.org); 10 were AMP copies |
| `noindex-page` | omgubuntu.co.uk 32, waldenfloral.com 28, arstechnica.com 6, crawler-test.com 46 and 9 more | 12 | right | Every hit matched an independent scan of all stored HTML and headers |
| `nofollow-page` | lannacoffeeco.com 6, crawler-test.com 16 | 7 | right | – |
| `snippet-blocked` | crawler-test.com 4 | 4 | right | crawler-test only |
| `robots-txt-unreachable` | – | – | not seen | Needs a robots.txt that answers 5xx, 429 or nothing |
| `robots-txt-unreachable-other-origin` | badssl.com 1 | 1 | right | badssl.com: 35 origins, each a TLS failure by design |
| `robots-txt-missing` | info.cern.ch 1, spacejam.com 1, neverssl.com 1, badssl.com 1 and 6 more | 8 | right | w3.org's 403 is a firewall's challenge, which the words cover |
| `site-hidden-from-search` | – | – | not seen | Needs a home page that is noindex |
| `robots-directives-conflict` | crawler-test.com 2 | 2 | right | crawler-test only |
| `noindex-near-home` | omgubuntu.co.uk 32, waldenfloral.com 28, arstechnica.com 6, crawler-test.com 41 and 7 more | 10 | right | Noisy: tag archives, booking and profile pages, noindex on purpose |
| `unavailable-after-passed` | – | – | not seen | crawler-test's only case is also noindex, so the check is rightly silent |
| `media-preview-blocked` | gov.uk 58 | 58 | right | gov.uk organisation pages: `max-image-preview:none` |
| `translation-blocked` | – | – | not seen | As above: the only notranslate page is noindex |
| `indexifembedded-without-noindex` | – | – | not seen | No page in any crawl has indexifembedded |
| `canonical-multiple` | crawler-test.com 4 | 4 | fixed | True. The detail lost its second URL to the 120-character cut. No real site has two canonicals |
| `canonical-broken` | crawler-test.com 38 | 8 | right | crawler-test only |
| `canonical-to-redirect` | littletonareachamber.com 3, arstechnica.com 1, create-react-app.dev 1, crawler-test.com 2 | 3 | right | – |
| `canonical-to-non-indexable` | omgubuntu.co.uk 10, crawler-test.com 2 | 3 | right | – |
| `canonical-outside-head` | cnx-software.com 97, crawler-test.com 6 | 4 | right | cnx-software.com: a `<div>` on line 59 of the head |
| `canonical-relative` | crawler-test.com 10 | 5 | right | crawler-test only |
| `canonical-invalid` | – | – | not seen | Needs an empty or non-web canonical href |
| `noindex-with-canonical` | omgubuntu.co.uk 10, arstechnica.com 5, european-union.europa.eu 1, crawler-test.com 2 | 4 | right | arstechnica.com: syndicated articles, noindex with a canonical to wired.com |
| `canonical-other-domain` | mit.edu 23, crawler-test.com 2 | 3 | right | mit.edu names tlecms.mit.edu on all 23 pages |
| `canonical-loop` | – | – | not seen | No pair of pages in any crawl name each other |
| `canonical-on-redirect` | – | – | not seen | No 3xx answer in any crawl carries a canonical |
| `canonical-fragment` | – | – | not seen | No canonical href in any stored page has a fragment |
| `canonical-invalid-attribute` | – | – | not seen | No canonical link in any stored page has hreflang, lang, media or type |
| `canonical-unlinked` | – | – | not seen | crawler-test's cases name 404s, which is canonical-broken's |
| `sitemap-non-indexable` | theregister.com 299, create-react-app.dev 46, arstechnica.com 5, crawler-test.com 5 and 5 more | 9 | fixed | theregister.com's 299 were refused to this crawler by robots.txt and open to Googlebot |
| `sitemap-error` | paulgraham.com 1, cnx-software.com 1, crawler-test.com 1 | 3 | right; left | True. crawler-test's `sitemap.txt` failed once on a dropped connection, with no second try |
| `sitemap-missing` | info.cern.ch 1, spacejam.com 1, neverssl.com 1, badssl.com 1 and 12 more | 4 | fixed | 15 true. w3.org's guess at /sitemap.xml met a firewall's 403 |
| `sitemap-url-other-host` | cs.stanford.edu/~knuth 584, theregister.com 1, crawler-test.com 9 | 6 | fixed | cs.stanford.edu's 584: the sitemap was asked for on the bare domain and served by www |
| `sitemap-over-limit` | crawler-test.com 1 | 1 | right | crawler-test: 50 MB is 52,428,800 bytes |
| `sitemap-near-limit` | crawler-test.com 1 | 1 | right | crawler-test |
| `sitemap-empty` | – | – | not seen | No sitemap in any crawl lists nothing |
| `sitemap-url-repeated` | gov.uk 183,443, wordpress.org/news 48,195, theregister.com 244, crawler-test.com 2 and 4 more | 5 | fixed | gov.uk's 183,443 are true. wordpress.org's 48,195 (Jetpack image sitemap) and the news sitemaps weren't repeats |
| `sitemap-lastmod-invalid` | – | – | not seen | Over 1.6 million real entries, none unreadable or in the future |
| `title-missing` | berkshirehathaway.com 1, crawler-test.com 24 and 2 more | 7 | right | berkshirehathaway.com's is the charset fault (see below) |
| `title-multiple` | lannacoffeeco.com 45, info.cern.ch 1, crawler-test.com 2 | 4 | right | info.cern.ch: two real titles; lannacoffeeco.com: 45 collection pages |
| `description-multiple` | lannacoffeeco.com 39, crawler-test.com 2 | 3 | right | – |
| `h1-multiple` | waldenfloral.com 211, softmaker.com 192, sqlite.org 101, webscraper.io 29 and 18 more | 10 | right | Noisy: counts hidden modal H1s |
| `h1-duplicate` | waldenfloral.com 222, oo-software.com 169, videolan.org 146, crawler-test.com 570 and 21 more | 10 | fixed | True. wordpress.org's 24 and cnx-software.com's 10 were pages of one archive read with a gap |
| `h1-image-only` | mugipottery.com 72, lannacoffeeco.com 2, crawler-test.com 2 | 4 | right | – |
| `duplicate-content` | softmaker.com 38, videolan.org 18, kde.org 16, quotes.toscrape.com 85 and 11 more | 8 | right | – |
| `near-duplicate-content` | softmaker.com 50, kingarthurbaking.com 41, videolan.org 31, quotes.toscrape.com 26 and 14 more | 20 | fixed | 71 of 311 were unrelated pages alike only in a header menu counted as content |
| `duplicate-site-version` | spacejam.com 2, neverssl.com 2, berkshirehathaway.com 2, crawler-test.com 3 and 11 more | 9 | right | Confirmed live |
| `readability-low` | littletonareachamber.com 28, brooklyncandlestudio.com 14, oo-software.com 5, quotes.toscrape.com 3 and 12 more | 14 | fixed | 19 of 81 were scored on option lists and embed-code boxes. The detail no longer reads "30, under 30" |
| `boilerplate-heavy` | kingarthurbaking.com (crawled again) 8 | 3 | fixed | Couldn't fire where a header's menu has no `<nav>`: its words counted as the page's own. After the fix, kingarthurbaking.com 8, true by an independent count (268 of 1,305 words) |
| `image-missing-alt` | paulgraham.com 299, kde.org 138, softmaker.com 45, crawler-test.com 6 and 12 more | 9 | right; left | True. A `data:` image with no alt isn't judged (sqlite.org's logo, see below) |
| `image-alt-too-long` | mugipottery.com 74, waldenfloral.com 36, brooklyncandlestudio.com 26, books.toscrape.com 15 and 10 more | 7 | right | – |
| `image-inline-large` | – | – | not seen | The largest inline image in any crawl is 4.6 KB |
| `hreflang-invalid-code` | videolan.org 127, kde.org 97, crawler-test.com 2 | 5 | right | `ast` and `nds` are no ISO 639-1 codes, which is all Google documents |
| `hreflang-missing-self` | kde.org 194, waldenfloral.com 3, softmaker.com 1, crawler-test.com 14 and 1 more | 6 | right | – |
| `hreflang-missing-return` | waldenfloral.com 3, crawler-test.com 4 | 5 | right | – |
| `hreflang-broken-target` | – | – | not seen | Hreflang targets aren't fetched unless something else leads to them |
| `hreflang-to-non-indexable` | softmaker.com 192, mozilla.org 59, create-react-app.dev 1 | 6 | fixed | softmaker.com's 192 true. mozilla.org's 59 were x-default naming a home page that redirects by language |
| `hreflang-duplicate-code` | crawler-test.com 4 | 4 | right | crawler-test only |
| `hreflang-missing-x-default` | mozilla.org 246, european-union.europa.eu 213, kde.org 194, crawler-test.com 8 | 5 | right | – |
| `hreflang-outside-head` | – | – | not seen | No page with hreflang has its head ended early |
| `hreflang-inconsistent-code` | – | – | not seen | No two fetched versions name each other by different codes |
| `hreflang-header-mismatch` | crawler-test.com 4 | 4 | right | crawler-test only |
| `hreflang-unlinked` | – | – | not seen | Skipped on every multilingual crawl for its URL limit |
| `hreflang-lang-mismatch` | – | – | not seen | An independent scan of every self entry found none either |
| `hreflang-self-only` | waldenfloral.com 184, mozilla.org 7, european-union.europa.eu 3 | 5 | right | Noisy: Wix writes x-default and en-us, both naming the page, on a one-language site |
| `structured-data-invalid` | brooklyncandlestudio.com 2 | 2 | fixed | True. The detail carried the parser's advice to a programmer |
| `structured-data-missing-required` | waldenfloral.com 130 | 3 | right | waldenfloral.com: `"Offers"` with a capital, which schema.org doesn't know |
| `structured-data-schema-errors` | kde.org 201, waldenfloral.com 163, gov.uk 70 and 5 more | 12 | fixed | True but for waldenfloral.com's 24: "No @context" for an empty `{}` block |
| `structured-data-missing-recommended` | softmaker.com 188, mugipottery.com 72, gov.uk 69, webscraper.io 179 and 8 more | 10 | right | Noisy: a Microdata Article shell beside a complete JSON-LD one |
| `structured-data-on-noindex` | omgubuntu.co.uk 32, lannacoffeeco.com 6, arstechnica.com 5 and 5 more | 5 | right | – |
| `no-https` | info.cern.ch 1, neverssl.com 1, httpforever.com 1 | 3 | right | info.cern.ch, neverssl.com, httpforever.com |
| `http-page` | videolan.org 1, crawler-test.com 400 and 4 more | 6 | right | – |
| `form-not-secure` | badssl.com 4, crawler-test.com 3 and 1 more | 8 | fixed | True. Missed a password field outside any form (http-password.badssl.com) |
| `protocol-relative-links` | carrabelle.org 174, littletonareachamber.com 161, kilmingtonvillage.com 58, crawler-test.com 581 and 3 more | 6 | right | Suppressed by any HSTS max-age, however short (see below) |
| `url-uppercase` | info.cern.ch 173, mozilla.org 98, debian.org 39, quotes.toscrape.com 46 and 4 more | all | right | Noisy: mozilla.org's 98 are locale folders (`/en-US/`) |
| `url-underscores` | european-union.europa.eu 213, sqlite.org 107, videolan.org 26, crawler-test.com 617 and 7 more | all | right | – |
| `url-too-long` | arstechnica.com 37, cnx-software.com 27, european-union.europa.eu 3, crawler-test.com 45 and 6 more | all | right | – |
| `url-tracking-parameters` | lannacoffeeco.com 4 | 4 | right | lannacoffeeco.com: a blog links a product with `utm_` parameters |
| `url-multiple-slashes` | – | – | not seen | crawler-test's cases are disallowed in its robots.txt |
| `url-space` | crawler-test.com 4 | 4 | right | crawler-test only |
| `url-non-ascii` | crawler-test.com 25 | 6 | right | crawler-test only |
| `url-repetitive-path` | gov.uk 25,446, info.cern.ch 4, debian.org 3, crawler-test.com 6 and 7 more | 16 | fixed | gov.uk's 25,443 were sitemap-only URLs built that way on purpose. info.cern.ch's 4 are the real fault |
| `url-internal-search` | gov.uk 6, debian.org 1, brooklyncandlestudio.com 1 and 1 more | 8 | right | – |
| `large-page` | waldenfloral.com 237, pellabakery.com 8, sqlite.org 1, crawler-test.com 20 and 1 more | 5 | fixed | True. "1.0 MB, over 1 MB" is now given to the byte |
| `html-past-crawl-limit` | waldenfloral.com 120, crawler-test.com 16 | 3 | fixed | True (waldenfloral.com: 120 pages). "2.0 MB, past the 2 MB" is now given to the byte |
| `resource-past-crawl-limit` | – | 7 files | left, not seen | No script or stylesheet in any crawl is over 2 MB decompressed (the largest 1.43 MB). The hole is confirmed (see below) |
| `viewport-missing` | paulgraham.com 299, info.cern.ch 174, videolan.org 90, crawler-test.com 796 and 9 more | 5 | right | An independent scan of all stored HTML agrees |
| `lang-missing` | paulgraham.com 299, sqlite.org 279, info.cern.ch 174, crawler-test.com 806 and 8 more | 4 | right | As above |
| `charset-missing` | paulgraham.com 299, info.cern.ch 174, berkshirehathaway.com 1, crawler-test.com 14 and 1 more | 5 | right | berkshirehathaway.com's is the charset fault (see below) |
| `social-image-relative` | lannacoffeeco.com 23 | 2 | right | lannacoffeeco.com: og:image is a Liquid error message |
| `og-url-not-canonical` | postgresql.org/docs 238, wordpress.org/news 22, omgubuntu.co.uk 9, crawler-test.com 3 and 5 more | 9 | right | – |
| `social-tags-multiple` | – | – | not seen | 18 pages repeat a tag, all with the same content |
| `og-title-duplicate` | kde.org 18, omgubuntu.co.uk 6, wordpress.org/news 5 | 29 | right | – |
| `social-image-broken` | cnx-software.com 1, carrabelle.org 1, crawler-test.com 8 | 10 | right | – |
| `social-image-too-large` | waldenfloral.com 1 | 1 | right | waldenfloral.com: 11.4 MB |
| `social-image-too-small` | mugipottery.com 67, kilmingtonvillage.com 43, waldenfloral.com 2 | 8 | fixed | 22 of kilmingtonvillage.com's 43 were a small og:image after a large first one |
| `head-tags-in-body` | cnx-software.com 97, info.cern.ch 23, paulgraham.com 2 | 6 | fixed | True. cnx-software.com's 97 details were cut before the cause, and fell into five groups |
| `html-structure-errors` | mugipottery.com 74, arstechnica.com 28, sqlite.org 1, quotes.toscrape.com 2 | 4 | right | – |
| `viewport-invalid` | littletonareachamber.com 161, jonasbrotherstaxidermy.com 27, debian.org 4 and 1 more | 4 | fixed | debian.org's 4: `initial-scale=1` and `1.0` "differ" |
| `charset-invalid` | crawler-test.com 2 | 1 | right; left | True. berkshirehathaway.com's `charset=unicode` should be one (see below) |
| `lang-invalid` | – | – | not seen | All 130 lang values in the crawls are well-formed |
| `mobile-alternate` | crawler-test.com 32 | 5 | right | crawler-test only. One sent in a Link header isn't read |

**The 42 that had fired before**: 41 fired again (all but `canonical-to-first-page`), and their counts were read for
anything odd. Two were at fault: `image-missing-dimensions` flagged every image of an AMP page (its size is on the
`<amp-img>`: omgubuntu 21 pages, cnx 4), and `security-headers-missing` called a referrer policy missing where a
`<meta name="referrer">` sets it (cnx-software.com's home page), the open item of the second audit. Both are fixed.
The title and description widths were measured again as above. `cookies-insecure` is noisy on Wix (its own
`ssr-caching` cookie, on 249 pages) and `not-in-sitemap` on tag archives; both are true to their words.

### What changed

All in `d936448c`. "Seen on" is where the fault was, and the count there before and after. For a rule's fix that is
the same crawl analysed again: all 45 crawls were, and the counts in this table are the only ones that changed on any
of them. For a fix to the parse or to how sitemaps are read it is a new crawl of the site (marked *crawled again*;
the pages a limited crawl reads differ a little from run to run).

| Check | What was wrong | Now | Seen on, before → after |
| --- | --- | --- | --- |
| `sitemap-url-repeated` | A page in the page sitemap and again in an image or news sitemap is no repeat: that's what Google's sitemap extensions are | A sitemap at least nine in ten of whose entries carry an `image:`, `video:` or `news:` tag isn't counted (a new column, `sitemaps.extensions_only`) | wordpress.org 48,195 → 1, cnx 10 → 0 (*crawled again*). gov.uk's 183,443, the same URL in two page sitemaps, stand. theregister.com's 244 are its news sitemap's, one entry of 246 without a tag, which is why the rule is nine in ten |
| `url-repetitive-path` | Judged every URL the crawl knew of, with no link to fix for one only a sitemap lists | Only a URL a page of the crawl links to or uses | gov.uk 25,446 → 4; info.cern.ch's 4, the real fault (a relative link building `/hypertext/WWW/hypertext/WWW/`), stand |
| `sitemap-url-other-host` | A sitemap was held to the host it was asked for at, not the one that served it after a redirect | The file's final URL is kept (`sitemaps.final_url`), and its entries belong on that host | cs.stanford.edu/~knuth 584 → 0 (*crawled again*) |
| `sitemap-non-indexable` | "Blocked by robots.txt" where robots.txt refuses this crawler and allows Googlebot | Such a URL wasn't checked, as one behind a firewall isn't (`GooglebotAccess` reads the stored robots.txt as Googlebot) | theregister.com 299 → 0 |
| `sitemap-missing` | Reported where the guess at `/sitemap.xml` met a firewall's 403, a timeout or a server error | Only where the guess was answered: not when the home page was blocked, nor on no answer, 429 or 5xx | w3.org 1 → 0 |
| `uncrawlable-links` | A `<div href>` holding a gallery's full-size photo isn't a link to a page | An href that names an image file isn't counted | waldenfloral.com 130 → 0 (*crawled again*); paulgraham.com's 19 (`<xa href>`) stand |
| `near-duplicate-content`, `low-word-count`, `boilerplate-heavy` | A header's menu written without `<nav>` (`role="navigation"`, Max Mega Menu, Weebly's `wsite-menu`) counted as the page's own words: unrelated pages looked alike, thin pages looked full, and `boilerplate-heavy` couldn't fire | Such a menu in a header or sidebar is navigation, as a `<nav>` there is | kingarthurbaking.com near duplicates 41 → 11, low word count 0 → 45, boilerplate 0 → 8; kilmingtonvillage.com near duplicates 13 → 0 (*crawled again*) |
| `readability-low`, and every word count | A `<select>`'s options, a `<textarea>`'s content and Shopify's country picker were words of the page | A form control's text is no words at all | debian.org's two forms 2 → 0; wordpress.org/news/podcast -23 → not flagged; brooklyncandlestudio.com: see after the table (*crawled again*) |
| `h1-duplicate` | Pages 2 and 9 of one archive, read with a gap by a crawl that hit its URL limit, "duplicated" each other's H1 | Paginated pages whose URLs number them from the same first page are one series | wordpress.org/news 24 → 0, cnx 10 → 0 |
| `pagination-sequence-error`, `pagination-not-linked` | An AMP copy of page 1 names the series' page 2, which points back to page 1, the copy's canonical | A copy is pointed back to by its canonical, and a link to a copy of the next page counts | omgubuntu.co.uk 6 and 6 → 0 and 2; cnx 4 and 4 → 0 and 3. What's left is an AMP page whose next AMP page the URL limit kept out, so its canonical isn't known |
| `hreflang-to-non-indexable` | An `x-default` naming a home page that redirects each visitor to their language is what `x-default` is for | An entry that is only `x-default` and redirects temporarily isn't judged; a permanent redirect still is | mozilla.org 59 → 0; softmaker.com's 192 (an old `http://` address) stand |
| `social-image-too-small`, `social-image-too-large` | Measured every `og:image` a page names; networks show the first | Only the first of each tag | kilmingtonvillage.com 43 → 21; the 21 left have a first image of 1,013 × 120 |
| `structured-data-schema-errors` | "No @context" for an empty `{}` block | An empty block says nothing | waldenfloral.com 163 → 139 |
| `viewport-invalid` | `initial-scale=1` and `initial-scale=1.0` "differ" | Two tags differ when their settings do | debian.org 4 → 0 |
| `broken-fragment-links` | `id="%e0%b9%81"` wasn't found by `href="#%e0%b9%81"` beside it: a stored fragment's escapes are in capitals | An id with escapes is matched in capitals too | cnx 2 → 0 |
| `content-type-mismatch` | A stray `<link rel="stylesheet" href="#">` made the page itself a stylesheet sent as HTML | A link to the page itself is no file | crawler-test.com 4 → 0 |
| `image-missing-dimensions` | The `<img>` an AMP optimiser writes inside a sized `<amp-img>` has no size of its own | It takes its `<amp-img>`'s | omgubuntu.co.uk 216 → 181 (*crawled again*) |
| `security-headers-missing`, `mixed-content` | A policy set in a meta tag read as missing | `<meta name="referrer">` is a referrer policy and a meta Content-Security-Policy a policy (not for `frame-ancestors`, which only a header sets); `upgrade-insecure-requests` in one leaves no mixed content | cnx-software.com: "Missing Content-Security-Policy, Referrer-Policy" → "Missing Content-Security-Policy" (*crawled again*) |
| `redirect-chain` (missed) | A chain entered at a URL of the crawl's list had no way in | A listed URL is one | httpbin.org and httpbingo.org 0 → 3 each (`/redirect/5`, `/absolute-redirect/3`, `/status/302`) |
| `generic-anchor-text` (missed) | "More →" and "Read more …" kept the space before the symbol | Trimmed | 53 links on videolan.org, softmaker.com and oo-software.com; 7 more pages flagged |
| `form-not-secure` (missed) | A password field outside any `<form>` on an HTTP page | Counted | http-password.badssl.com |
| `meta-refresh` (evidence) | A script's redirect read "Moves to … after 0 seconds", on a page with no such tag | "A script moves to … as the page loads", and the description says scripts count | crawler-test.com 4 |
| `canonical-multiple` (evidence) | The 120-character cut took the second URL | A URL on the page's host is shown as its path | crawler-test.com 4 |
| `head-tags-in-body` (evidence) | The list of tags came first and the cut took the cause; one template fault fell into five groups | The cause is kept whole and the list gives way | cnx 97: one group |
| `structured-data-invalid` (evidence) | "…which is not supported in this mode. Change the reader options." | Says the block has a trailing comma | brooklyncandlestudio.com 2 |
| `large-page`, `html-past-crawl-limit` (evidence) | "2.0 MB, past the 2 MB Google reads" | A size that rounds to its limit is given to the byte | waldenfloral.com 57 of 120 |
| `readability-low` (evidence) | "Reading ease 30, under 30" for 29.72 | "29.7, under 30" | oo-software.com 1 |

Words changed on 8 checks (`meta-refresh`, `sitemap-non-indexable`, `sitemap-missing`, `url-repetitive-path`,
`social-image-too-large`, `social-image-too-small`, `hreflang-to-non-indexable`, `security-headers-missing`), each to
say what its rule now does; the app's copy of the catalog is captured again. On the synthetic site: `/plants/real-markup`,
a clean page holding seven of the shapes above, and `/sitemaps/images.xml`, an image sitemap that lists the home page
and two products again.

**brooklyncandlestudio.com, crawled again** (60 URLs asked for, 17 pages read: the shop refuses most to an unsigned
crawler): `readability-low` 14 of 26 pages → 0 of 17, `near-duplicate-content` 12 → 0. Every page had scored the
country picker's 670 words. Its mega menu's lists, under class names of the theme's own, still count as each page's
words.

**A crawl made before these fixes, analysed again**, gets every rule's change. What the parse changed takes a new
crawl: the words of a form control or a header's menu, an image file on a `<div href>`, an AMP image's size, a
password field outside a form, a meta Content-Security-Policy; and a sitemap read before has neither of the two new
columns, so its repeats and its host are judged as they were.

### Still never seen on a real page, and why

27 checks. None of the crawled pages has the fault, by an independent scan where one can be made.

The fourth pass, below, went to each of these faults and found a real page for all but one (`hreflang-unlinked`).

- **The fault is rare, and none of forty sites has it:** `soft-404` (every site crawled answers 404 for a missing
  page, tried by hand; paulgraham.com redirects one to its home page, which this check doesn't cover),
  `redirect-temporary-cached`, `robots-txt-unreachable`, `site-hidden-from-search`, `unavailable-after-passed`,
  `translation-blocked`, `indexifembedded-without-noindex`, `canonical-invalid`, `canonical-loop`,
  `canonical-on-redirect`, `canonical-fragment`, `canonical-invalid-attribute`, `sitemap-empty`,
  `sitemap-lastmod-invalid` (1.6 million entries read, none wrong), `image-inline-large` (the largest inline image is
  4.6 KB), `hreflang-outside-head`, `hreflang-lang-mismatch`, `social-tags-multiple`, `lang-invalid`,
  `pagination-broken`, `resource-past-crawl-limit` (no script or stylesheet on any site is over 2 MB, the largest
  1.43 MB decompressed).
- **Only crawler-test.com has the case, and it can't fire there:** `url-multiple-slashes` (its cases are disallowed in
  its robots.txt), `canonical-unlinked` (its cases name 404s, which is `canonical-broken`'s), `hreflang-broken-target`
  (its target is never fetched: the crawl doesn't follow hreflang), and `unavailable-after-passed` and
  `translation-blocked` again (its one page with them is noindex, and both judge indexable pages).
- **Needs a whole crawl of a site, which a few hundred URLs isn't:** `non-indexable-inlinks-only`, `hreflang-unlinked`,
  `hreflang-inconsistent-code` (skipped, and said so, on every multilingual crawl for its URL limit; on the small sites
  crawled whole, none).

### Left, and why

- **The fetcher's, outside this audit's lane (`Fetching/`):**
  - **A page that declares `charset=unicode` in a meta tag is read as UTF-16** (`CharsetDecoder`: only labels starting
    `utf-16` are taken as UTF-8, as the HTML standard has it for every UTF-16 label in a meta tag: `unicode`, `ucs-2`,
    `csunicode`, `unicodefeff`, `unicodefffe`, `iso-10646-ucs-2`). berkshirehathaway.com's ASCII home page became
    garbage: no title, no H1, no links, so the crawl stopped at one page and reported them all missing. It should be
    one `charset-invalid`.
  - **A 1xx answer is a "5xx"** (`HttpFetcher.Classify` sends everything outside 200 to 499 to server errors:
    crawler-test's `status_101`), **and 300, 304, 305 and 306 are redirects** (`links-to-redirects` counts four such
    targets there). Seen on crawler-test.com only.
  - **A connection given up after 10 seconds says "No response within 20 seconds"** (the connect timeout is the lesser
    of 10 and the request's; the message prints the request's): three badssl.com hosts.
  - **`resource-past-crawl-limit` still stores the compressed size**, or none. Measured here: of 1,158 scripts and
    stylesheets fetched, 175 have no size at all, and the stored size is 3.5 to 3.8 times under the real one
    (maps.googleapis.com's script 401,885 stored, 1,428,268 decompressed). No file on any site is over the limit, so
    nothing was missed, and nothing proves the check either.
- **`redirect-broken` on the crawler's own probe of the www address** (cs.stanford.edu: `http://www…/~knuth/` ends at
  a 404). Kept: it's the rule as designed and tested ("a redirect to a broken URL is reported on the version"), and
  the address is broken for whoever types it. Nothing on the site links there, which is why a reader took it for
  noise.
- **`canonical-multiple`: no real site declares two different canonicals.** Only crawler-test.com's two cases fired
  (4 flags), so the owner's decision on which canonical should count has no real page to weigh yet.
- **`image-missing-alt` doesn't judge a `data:` image** (sqlite.org's logo, on 5 pages). Tried and taken out: the
  check's views name each image by its URL, and such an image has none to name.
- **`<amp-img>` itself isn't read**, only the `<img>` inside it: an AMP image's alt goes unjudged where the optimiser
  wrote no `<img>` (one long alt on omgubuntu.co.uk).
- **`redirect-loop` doesn't see a page whose instant meta refresh names itself** (crawler-test's
  `infinite_meta_redirect`): only `meta-refresh` fires.
- **`uncrawlable-links` reads only script written in the attribute**: `onclick="myFunction()"` isn't caught
  (crawler-test), and can't be from the markup.
- **A sitemap that robots.txt disallows is read all the same**, and nothing says so (crawler-test's
  `sitemap_disallowed.xml`); **a sitemap whose connection dropped once isn't asked for again** (its `sitemap.txt`).
- **`near-duplicate-content` names the first page of the group**, which isn't always the nearest (softmaker 7 of 50).
- **`mobile-alternate` doesn't read one sent in a `Link` header** (crawler-test), as its words say.
- **`protocol-relative-links` is silenced by any HSTS policy**, a five-minute one included (kingarthurbaking.com,
  `max-age=300`, 167 pages), where `security-headers-weak` already calls that policy too short.
- **`html-past-crawl-limit` takes Google's "2MB" as 2,097,152 bytes.** sqlite.org's `lang_select.html` is 2,033,807:
  past 2,000,000, not flagged. Google's page doesn't say which it means.
- **`description-too-long` and `-too-short`: the words are right.** They measure pixels and say so, and 1,818
  descriptions measured again agree to the pixel. "About 160 characters" is a little generous: descriptions within 3%
  of the limit average 155. The Why cites Screaming Frog for 985 px where the description calls it Google's desktop
  width. Left for the owner.
- **`x-card-missing` and `x-card-incomplete`**: as the second audit left them.

### Seen on the sites, not faults of a check

- **True and noisy:** `noindex-near-home` on tag archives, booking and profile pages that are noindex on purpose
  (omgubuntu 32, waldenfloral 28); `too-many-links`, where a site's own shop subdomain counts as another site
  (kingarthur: 210 of 227 "external" links); `slow-external-links`, where one slow answer flags every page linking to
  it (mozilla: 36 pages, one URL); `internal-nofollow-links` on a hidden bot-trap link (carrabelle: all 174 pages);
  `hreflang-self-only` on Wix, which writes `x-default` and `en-us` on a one-language site (184); `h1-multiple` on
  hidden dialog headings; `structured-data-missing-recommended` on a Microdata `Article` shell beside a complete
  JSON-LD one; `url-uppercase` on locale folders (mozilla's `/en-US/`).
- **Wix writes `"Offers"` and `"Availability"` with capitals** (130 product pages): unknown properties to schema.org,
  and `structured-data-missing-required` says the product has no `offers`. Whether Google's reader forgives the
  capital wasn't learnt.
- **`responses.indexability_reason` says Canonicalised for a page whose canonical is broken**, where the analysis
  judges it indexable: anything reading the stored column, not the analysis's, disagrees with the issues.
- **create-react-app.dev names every page without its trailing slash**, in its sitemap, its canonicals and its
  hreflang, and each such URL redirects to the one with the slash: `sitemap-non-indexable` 46, and
  `canonical-to-redirect` and `hreflang-to-non-indexable` on the page read, confirmed live.
- **A Shopify shop refuses half its pages to an unsigned crawler** (brooklyncandlestudio.com: 26 pages read of 250
  URLs), as the first audit found.

### Checks worth adding

Not built here, by the audit's rule:

- `<meta name="google" content="notranslate">`: Google's own tag for it, which `translation-blocked` doesn't read
  (it reads the robots rule).
- A sitemap blocked by robots.txt (Search Console's "Sitemap blocked by robots.txt").
- A missing URL answered with a redirect to the home page (paulgraham.com): the soft 404 Google reports most.
- Hreflang entries written as relative URLs (crawler-test), which Google requires in full.
- A URL listed twice in one sitemap file (gov.uk's `sitemap_10.xml` has two).
- `blocked-by-robots` saying when Googlebot is allowed where this crawler isn't (theregister.com's 300).

## The fourth pass, 2026-11-24: the last 29

The third audit left 29 checks with no true firing on a real page: 27 that had never fired, and two
(`content-type-mismatch`, `pagination-not-linked`) that had fired only wrongly. A broad crawl doesn't find these; the
faults are rare, or sit where a crawl of a few hundred URLs never looks. This pass went to each fault instead: found
real pages that have it, confirmed each by hand, and only then crawled them. The fixes are on `fix/checks`.

### In short

- **28 of the 29 are now proven on a real page, every one on an ordinary site**: none needed a target made for
  testing crawlers. One, `hreflang-unlinked`, is still unproven: what it needs and what was tried is below.
- **Of the 170, 169 are proven on a real page** (the 141 of the third audit and these 28).
- **One check flagged what was right.** `sitemap-empty` called an RSS feed empty: All in One SEO declares a
  `sitemap.rss` in robots.txt beside its `sitemap.xml`, Google takes a feed as a sitemap, and the reader looked only
  for `<loc>`. Two of the first four real firings were this (freebsdfoundation.org, servercore.com, 50 pages listed in
  each). The plugin is among WordPress's most installed.
- **Four checks missed a real fault, three of them for one cause.** A URL that only an hreflang or a
  `rel="next"`/`"prev"` link names was never asked for, since the crawl follows neither unless told to: so
  `hreflang-broken-target` couldn't see a version that is gone unless a link led there too (goodsmile.info's `/zh/`),
  nor `pagination-broken` a next page that is gone, nor `pagination-not-linked` a next page nothing links to, which
  is the fault it is for. The crawl now asks how such a URL answers. And `unavailable-after-passed` couldn't read
  a date with a European zone's letters (dargaud.com: `Saturday, 04-Oct-25 00:00:00 CEST`).
- **One check ran where it couldn't know.** A crawl of a URL list judged what links to a page (`hreflang-unlinked`,
  `canonical-unlinked`, `non-indexable-inlinks-only`), with only the listed pages' links read. Skipped now, saying why.
- **One check's evidence read wrong**: `social-tags-multiple` quoted two descriptions that start alike as the same
  text twice.
- **The CLI gained `--crawl-hreflang` and `--crawl-pagination`**, options the app has had: without the first,
  `hreflang-unlinked` couldn't be reached from the command line at all.
- `AnalysisFreshness.VERSION` 28. No catalog text changed, so the app's copy of the catalog stands. One skip reason
  is new (`url-list`).

### How it was checked

**Finding the pages.** A public list of the million most-visited domains (Tranco), sampled at random below the top
5,000, and each sampled site looked at once, by a script, at the crawler's own pace for one site: no site was crawled
to find a fault.

| Look | Sites | What was read of each |
| --- | ---: | --- |
| Home pages | 48,000 | The home page with its redirects and headers, robots.txt, the first sitemap, one made-up address, up to three language versions the page names, the page its `rel="next"` names |
| Inner pages | 20,000 | The home page and up to four pages it links to (dated kinds first: news, events, jobs), and how the home page's first eight scripts and stylesheets are sent |
| Canonicals | 7,800 | The home page and the page its canonical names, where the home page has no link to it |

The looks named candidates, by regular expression, and were wrong often enough that none counts for anything here: a
canonical inside an HTML comment (olamgroup.com), a canonical that was a script's text and not the page's
(derichebourg.com). For `unavailable-after-passed` nothing short of reading every event page of a site that sends the rule found a
date that had passed (dargaud.com: 193 agenda pages asked for their headers, ten with the rule, one of them past).

**Crawling them.** Some 140 crawls with the CLI at its polite defaults, robots.txt obeyed, `--store-html`, none past a
block and none asked again after one:

- a crawl of a URL list, of the page or the pair of pages, for a check that judges a page (`--list`);
- a crawl of 25 to 60 URLs from the home page for one that judges the site (sitemaps, robots.txt, the home page's
  noindex) or a file a page uses;
- a whole crawl, to 1,500 URLs with images, files and other sites' links left out, for the checks that need every
  page: `canonical-unlinked`, `non-indexable-inlinks-only`, `hreflang-unlinked`, and the three the crawl now asks
  unlinked URLs for.

**Reading every firing by hand**: the live page fetched again with `curl`, its markup or its headers against the
check's words and against what the detail says; counts taken again without the engine where a check counts (the
sitemap dates, the inline images' decoded sizes, the scripts' sizes decompressed, the pages of a site with a link to
the page said to have none).

### The 29

Every page is an ordinary site's. "Fixed" is a fault found here and mended (below); a number is the URLs flagged.

| Check | Seen on | What the page has | Verdict |
| --- | --- | --- | --- |
| `soft-404` | aacc.edu `/404/`, kmp.com `/404-seite-nicht-gefunden/`, localwineevents.com `/error-404`, navitaire.com, siris.nl, thelostwords.org | 200 OK, indexable, a title or H1 such as "404 - Anne Arundel Community College", 33 to 156 words. Each is what the site answers a made-up address with: three redirect it to an error page of their own, three serve the error at the address asked for | right |
| `redirect-temporary-cached` | remotevs.com, autosecurite.be, malgari.com | remotevs.com's `/` answers 302 to a language's folder (`/en/` to the crawl, `/ko/` to a later request) with `Cache-Control: max-age=15552000`: "302 Found, cached for 180 days". The other two 30 days | right |
| `robots-txt-unreachable` | kalingauniversity.edu.in, indiancountrytodaymedianetwork.com, lifeagents.ai | robots.txt answers 500 (to a browser's and Googlebot's user agent too, tried on the first) while the home page answers 200 | right |
| `site-hidden-from-search` | technomancy.us, targetscan.org, northamptonshire.gov.uk, opendatakit.org, its-dienst.de, arrakis.se, question-orthographe.fr | technomancy.us: `<meta name="googlebot" content="noindex">` on 428 of 450 pages. targetscan.org: a research database whose home page is `noindex,nofollow`, so nothing past it was read. The rest: the home page is noindex and the only page read (northamptonshire.gov.uk's and opendatakit.org's say the site is retired) | right |
| `unavailable-after-passed` | dargaud.com `/agenda/antoine-pedron-paris` | `X-Robots-Tag: unavailable_after: Saturday, 04-Oct-25 00:00:00 CEST`, over a year past, on an event page still served | fixed: not read before |
| `translation-blocked` | mapress.com (2 pages), spiko.io | `X-Robots-Tag: notranslate`; `<meta name="googlebot" content="notranslate">` | right |
| `indexifembedded-without-noindex` | clackamas.edu `/student-life/events`, instantmarkets.com | `<meta name="googlebot" content="indexifembedded">` beside `index,follow` | right |
| `canonical-invalid` | twu.edu, wasabi.com, airportinformation.com, superviral.in | `<link rel="canonical" href="">` on three home pages; `href="httpss://superviral.in/"` | right |
| `canonical-loop` | e-thrift.gr | `/` names `/index.php?route=common/home`, which names `/`: "A loop of 2: / → /index.php?route=common/home → back to /" | right |
| `canonical-on-redirect` | gov.br `/cvm` | 301 to `/cvm/` with `Link: <http://www.gov.br/cvm>; rel="canonical"` | right |
| `canonical-fragment` | naamayehuda.com, an attachment page | `href="…/boy-with-fish/#main"` | right |
| `canonical-invalid-attribute` | rosepassion.com `/en`, elparking.com (2 pages) | `<link rel="canonical" href="…" hreflang="es" />` | right |
| `sitemap-empty` | caat.org.uk `/wp-sitemap.xml`, immregistries.org `/sitemap.xml` | A `<urlset>` with no URL in it; a 200 answer with no bytes | fixed: two more were feeds, not empty |
| `sitemap-lastmod-invalid` | hi.org 6, adwmg.com 4, colocall.net 57, headed2.com 7, effi.org 151, vu.edu.bd 326, ghostwriter-ai.com 1; eichendorff21.de 1, necrologicorato.it 22 | Not dates: `0-00-00T00:00:00+00:00`, `2026-02-30T22:00:11+01:00`, `10.07.2017`, `Thu, 19 Sep 2013 23:01:48 -0400`, `-0001-11-30T00:00:00+00:00`, `2025-1-11`. In the future: a page dated 15 December, notices dated two to four days ahead. Every count matches the file read without the engine | right |
| `image-inline-large` | bragi.com, carelonbehavioralhealth.com, eichendorff21.de (3 pages), powersoftt.com (9) | `data:` images of 14,462 and 17,572 bytes decoded, said as "14 KB" and "17 KB"; up to 156 KB | right |
| `hreflang-outside-head` | mile.app `/en`, vlcc.com | All 23 (and 6) entries after `<body>`, with the canonical and the robots tag. Both sites are built with Next.js, which can stream a page's metadata into the body | right; see below |
| `hreflang-lang-mismatch` | josko.com `/cs/`, garantieavantageplus.ca `/en/`, wisepilgrim.com `/de`, `/fr` | The Czech page says `lang="de"`, the English one `lang="fr"`, the German and French ones `lang="en"` | right |
| `social-tags-multiple` | mghclaycenter.org, tusk.org, americanalpineclub.org, homewellcares.com, newmediacampaigns.com, securetechalliance.org, bid416.com, morgan-motor.com 35, awebsitethatworksforyou.com.au 133 | Two `og:title` ("Home" and "Home - Tusk"), two `twitter:card` ("summary" and "summary_large_image"): a theme and a plugin each writing them | fixed: evidence |
| `lang-invalid` | bofrost.at, epson.ca, wwe-media.de, operatorratunkowy.pl | `lang="de_AT"`, `en_CA`, `de_DE`, `pl_PL` | right |
| `pagination-broken` | morgan-motor.com `/news/`, verano.com `/blog/` | `rel="next"` names `/page/2/`, a 404 no link leads to: "rel="next" returns 404 Not Found" | fixed: not reachable before |
| `resource-past-crawl-limit` | docs.bokeh.org, plotly.com | `searchindex.js`, 3,958,868 bytes decompressed: "3.8 MB once decompressed (676 KB as sent)". plotly's own library, 4,815,814, and MathJax's `tex-svg.js`, 2,108,580: "2,108,580 bytes once decompressed (503 KB as sent)" | right |
| `url-multiple-slashes` | vapeitalia.it, tiemposur.com.ar, bid416.com | The home page links `//ritardispedizioni`, `//contenidos/portada`, `//page/Payments`, each a 200 page with no canonical to the plain address | right; narrow, see below |
| `canonical-unlinked` | plankreis.at, powersoftt.com 18, awebsitethatworksforyou.com.au 3, tenstorrent.com 162 | plankreis.at: `/` names `/start`, a page of its own that none of the site's pages links to. powersoftt.com: links say `?service=Insights+and+reports`, the canonical `?service=Insights%20and%20reports` | right |
| `hreflang-broken-target` | goodsmile.info, powersoftt.com 44, monkeyapp.org 48 | goodsmile.info names `/zh/` as its zh-TW version, a 404 with no link to it. powersoftt.com: four versions (`/in/`, `/ae/`, `/qa/`, `/sa/`) all 404, named on every page, linked on none | fixed: not reachable before |
| `non-indexable-inlinks-only` | technomancy.us 15 | `/talks/fediverse/` has no noindex and its one link is from `/talks/`, which has | right |
| `hreflang-unlinked` | – | – | not seen; see below |
| `hreflang-inconsistent-code` | atos.com | `/en-it/` names `/en-na/` as `en-us`; `/en-na/` calls itself `en`: "Names /en-na/ as "en-us"; /en-na/ calls itself "en"" | right |
| `content-type-mismatch` | sotcom.ru, ancestrees.com, careofcarl.se | `main.js` and `main.css` sent as `application/octet-stream`; a stylesheet's address answered with an HTML page; another site's script sent as `text/html` | right |
| `pagination-not-linked` | montreuxjazzfestival.com `/fr/news/`, afghan-wireless.com 3 | `rel="next"` names `/fr/news/page/2/`, a 200 page, and the page has no `<a>` to it | fixed: not reachable before |

### What changed

"Seen on" is where the fault was, before and after.

| Check | What was wrong | Now | Seen on, before → after |
| --- | --- | --- | --- |
| `sitemap-empty` (wrong flag), and every sitemap check | An RSS 2.0 or Atom feed listed as a sitemap was read for `<loc>` alone, so it listed nothing | `SitemapReader` reads a feed as Google does: each item's `<link>` (each entry's, in Atom), with its date. The entries are marked as an image sitemap's are, as a feed lists again what the page sitemap lists, so they are no repeats; a feed's RFC 822 date is no unreadable lastmod | freebsdfoundation.org 1 → 0, servercore.com 1 → 0 (*crawled again*); caat.org.uk's and immregistries.org's stand |
| `hreflang-broken-target`, `pagination-broken` (missed) | A URL only hreflang or `rel="next"`/`"prev"` names was skipped, the crawl following neither by default, so one that is gone was found only where a link led to it as well | The crawl asks how it answers, as it does an image: a status check, never read, at no click depth, after every linked URL and within the URL limit. A link to it, found before or after, reads it as any page (`CrawlWriter`) | powersoftt.com 0 → 44 pages, each naming four versions that are 404; morgan-motor.com `/news/` and verano.com `/blog/` 0 → 1 |
| `pagination-not-linked` (missed) | Judged only a next page the crawl had read, which by default is one something links to: the fault itself kept it out | A next page that answers a status check as HTML is judged too | montreuxjazzfestival.com `/fr/news/` |
| `unavailable-after-passed` (missed) | A date with its zone in letters was read for the American zones and GMT alone | CET, CEST, WET, WEST, EET, EEST, BST, MSK, JST, AEST, AEDT, NZST and NZDT too: what PHP's `DATE_RFC850` writes on a European server | dargaud.com 0 → 1 |
| `hreflang-unlinked`, `canonical-unlinked`, `non-indexable-inlinks-only`, `orphan-page` (ran where it couldn't know) | A crawl of a URL list reads the listed pages and follows no link, and these ran on it all the same | Skipped on a list crawl, reason `url-list`: "The crawl read a list of URLs, so links from the site's other pages weren't read." | wisepilgrim.com (3 addresses listed): `hreflang-unlinked` 2 → skipped. vlm.be: `non-indexable-inlinks-only` 1 → skipped |
| `social-tags-multiple` (evidence) | Two values that start alike were cut at the same place: "Quality in-home c…" · "Quality in-home c…", on a check that says they differ | Each is shown from the word they part at: "…are committed t…" · "…know there's" | homewellcares.com; morgan-motor.com: "…global" · "…global audience,…" |
| The CLI | No way to turn on two crawl options the app has | `--crawl-hreflang`, `--crawl-pagination` | – |

On the synthetic site: `/plants/hreflang-broken` names a second 404 that nothing links to;
`/plants/series-gone-only` and `/plants/series-load-more/` name next pages nothing links to, one gone and one there;
`/plants/unavailable-after-header` sends dargaud.com's header; `/sitemaps/feed.rss` is a feed in the sitemap index,
listing the home page and two products again.

**What a URL-limited crawl still doesn't see.** The new status checks come last, so that they never take a linked
page's place: a crawl that stops at its URL limit doesn't reach them (goodsmile.info at 60 URLs: `/zh/` not asked for,
nothing flagged). A whole crawl does.

**A page only hreflang names has no click depth now.** It used to be given the depth of the page naming it plus one,
though no link leads there. Where a sitemap lists it too it is read as a page, after the linked ones, and shown with
no depth.

### Still unproven

**One check, `hreflang-unlinked`.** It needs four things at once: a language version that is an indexable page; that
no page of the site links to and no sitemap lists; a crawl told to follow hreflang, since otherwise the version is
never read and the check is skipped, saying so (tenstorrent.com: "171 language versions they name weren't read"); and
a crawl of the whole site, under its URL limit. What was tried:

- **The home-page look**: of 315 multilingual sites, 152 have a home page that names a version it has no `<a>` to (a
  language picker written by a script, mostly), and on 112 of those the first sitemap doesn't list the version either.
  A home page without the link is far from a site without it.
- **Eleven of those crawled whole, hreflang followed** (`--crawl-hreflang`, 1,500 URLs, images, files and other
  sites' links left out). Three finished under the limit, and none has such a version, by a count made without the
  engine too: meltingspot.io (1,440 pages, 442 versions named), jsl.com.br (351 pages, 66 named) and
  syncpayments.com.br (50 pages: its `?lang=en` version names the plain address as its canonical, so it's no indexable
  page and rightly isn't judged). Five ran past 1,500 URLs and were skipped for it, each saying so: cosmonova.net,
  poleasingowe.pl, medicare-group.hu, optika-fielmann.ua and afghan-wireless.com. wisepilgrim.com was stopped with
  over 54,000 URLs queued (a list crawl of three of its addresses had flagged `/de` and `/fr`, which is the wrong run
  now skipped), taittowers.com as too slow to finish, and pzps.pl's robots.txt answered 503.
- **So what would prove it** is a small site whose versions are named by hreflang alone on every page, not only its
  home page, and left out of its sitemap: none was found among 48,000 home pages. It stays proven on the synthetic
  site (`/plants/hreflang-only-b`) alone.

### Left, and why

- **`soft-404` passes over an error page of 300 words or more, and one that names a canonical.** safarilounge.jp
  answers a made-up address 200 with the title "404 Not Found | Safari Lounge" and 660 words, most of them its menus;
  gbvlearningnetwork.ca's error page (title "404 Error", 109 words) names `/page-not-found.html` as its canonical, so
  it isn't indexable and isn't judged. Both are the rule as written.
- **`url-multiple-slashes` only reports a `//` address that is an indexable page.** Ten sites that link such addresses
  were tried: on seven the address redirects to the plain one or names it as canonical (sait.ca, ispydiy.com, momo.com,
  artsana.com, people.co.jp, hokugin.co.jp, tusk.org), which `links-to-redirects` and `canonicalised-page` report
  instead. The link is as wrong on those.
- **`<link rel="canonical" href="#">`** (sanmarusan.net) is a template's placeholder and isn't flagged: it resolves to
  the page itself, and the fragment check passes over an empty fragment.
- **`hreflang-outside-head` and `canonical-outside-head` on a Next.js site that streams its metadata** (mile.app,
  vlcc.com): true of the HTML as served, to this crawler and to a request with Googlebot's user agent alike. Next's
  script is meant to move the tags into the head in a browser; whether Google's rendering then takes them wasn't
  learnt. The site's own remedy is Next's `htmlLimitedBots` setting.
- **`sitemap-lastmod-invalid` says "lastmod not a date" without quoting it**: the crawl keeps a lastmod as a time or
  as unreadable, not as written. The detail names the file and the first entry, which finds it.
- **`canonical-multiple` now has a real page for the owner's decision**: vlm.be's `/nl` and `/nl/` each carry two
  canonical links, `/nl/` and `/nl`, in that order.
- **`robots-txt-unreachable` makes for a slow crawl**: the crawler asks again every two minutes before it gives up,
  so the crawl of indiancountrytodaymedianetwork.com, which read nothing, took four and a half minutes.
- **The app's words for "Crawl hreflang targets" and "Crawl pagination targets"** say such a URL is fetched only with
  the option on. It's read as a page only then; it is asked how it answers either way. Not changed here
  (`app/src/crawl/options/words.ts`).

### Seen on the sites, not faults of a check

- **A language redirect cached for half a year** (remotevs.com): the home page sends each visitor to their language
  with a 302 any proxy may keep for 180 days, which is the fault `redirect-temporary-cached` is for.
- **A site that hides from Google alone** (technomancy.us): `googlebot` noindex on 428 of its 450 pages, on purpose by
  the look of it. `site-hidden-from-search` is true to its words; the owner knows.
- **tenstorrent.com names `/en/…` as every page's canonical and links none of them** (162), and its 171 language
  versions are named by hreflang alone: `hreflang-unlinked` was skipped there, saying so.
