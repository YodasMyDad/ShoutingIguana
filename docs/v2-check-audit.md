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
