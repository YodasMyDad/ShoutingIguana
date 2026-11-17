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
| `broken-external-links` | Links, Warning | 8/0/1 | 0 (bot walls rightly left out; who sent a's two 409s is unverified) | 0 | reworded | What says refused and robots-disallowed links aren't counted, in both modes | `b84ee69` |
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
| `title-too-long` | Titles, Notice | 14/0/84 | 0 | 0 | reworded | Simple: "Titles that may be cut off in search results" | `b84ee69` |
| `title-too-short` | Titles, Notice | 3/6/10 | 0 | 0 | reworded | Simple step: skip login and basket pages | `b84ee69` |
| `title-duplicate` | Titles, Warning | 0/0/2 | 0 | 0 | right | – | – |
| `description-missing` | Meta descriptions, Notice | 0/6/12 | 0 | 0 | right | – | – |
| `description-multiple` | Meta descriptions, Warning | 0/0/0 | – | 0 | right, not seen | – | – |
| `description-too-long` | Meta descriptions, Notice | 0/2/203 | 0 | 0 | reworded | Simple: "Search descriptions that may be cut off" | `b84ee69` |
| `description-too-short` | Meta descriptions, Notice | 1/1/0 | 0 | 0 | right | – | – |
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
  the verdict: details belong to `ui/3b-evidence`, which is rewriting them.
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
- **"Paginated pages canonicalised to page 1"** (Warning). Garden's `/garden-sheds/?p=2`, `/greenhouses/?p=2` and
  `/blog/?p=2` name page 1 as canonical though each lists its own products or posts, which are then reached only
  through pages asking to be dropped. `canonicalised-page` reports them today, as a Notice beside textbook parameter
  variants.
