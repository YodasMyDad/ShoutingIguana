# Accuracy audit: onpage

26 checks: Titles, MetaDescriptions, Html, Content, Urls, Headings, StructuredData. Read-only on `ui/step-3` at aa2802b.

## Evidence and method

| Site | Crawl | HTML pages (indexable) | Notes |
| --- | --- | --- | --- |
| a = www.aptitude.co.uk | done, analysed 06:51 | 33 (26) | 7 canonicalised `?filter=` / `?type=` variants |
| j = jitsunami.com | done (`exit 0`), analysed 07:25 by the crawl itself, no copy needed | 10 (10) | Shopify. All 7 product URLs and 5 others got **429** (`internal-blocked` 12), so no product page was read: the structured-data and product-template checks saw only home, collection, blog, cart, contact, FAQ and policy pages |
| g = www.gardenfurnituresets.co.uk | done (`exit 0`), analysed 07:25 | 260 (257) | 6,051 URLs, mostly images; 202 pages carry Product JSON-LD |

How each check was verified (scripts in `audit3b/scratch-onpage/`):

- **Every flag and every non-flag, not a sample**, for the 17 page-level checks (titles, descriptions, H1s, viewport,
  lang, charset, Open Graph, low word count, URL case/underscores): `verify.py` re-parses every stored raw HTML page
  with Python's `html.parser` (independent of AngleSharp), applies the rule as the catalog words it, and diffs the
  result against both the stored `PageFacts` and the `issues` table. Result on all three crawls: **0 fact
  mismatches, 0 wrong flags, 0 misses** (303 pages).
- Duplicates (`groups.py`): title, description, H1 and content-hash groups rebuilt from the facts of indexable pages
  and compared with the `issues` rows: exact match on all three sites.
- Near duplicates (`neardup.py`): all pairs brute-forced over the four stored 64-bit lanes (32,896 pairs on g). No
  pair within 24 bits on any site, so the bucketed search missed nothing. Closest g pair: ids 274/278 at 37 bits; an
  independent 3-word-shingle cosine on their text gives 0.85, so not flagging them is right.
- JSON-LD: every block parsed with Python `json` (0 invalid, as the engine says) and the types read by hand.
- Site versions: the probe rows (ids 2 to 4) read on each crawl.
- Snippets below are from the stored HTML (`dump.cs ... html`).

## title-duplicate

1. **Fired**: a 0 · j 0 · g 2.
2. **Wrong flags**: none. g 9 `/login/` and g 290 `/login/?register=true`, both indexable, both `<title>Login</title>`.
3. **Misses**: none. Every title group of indexable pages rebuilt from facts on all three sites; g's only group is this
   one. a's canonicalised variants share titles with their canonicals (e.g. a 23/24 = a 11 "Contact us | Aptitude")
   and are rightly left out (not indexable).
4. **Words**: clear in both modes. Plain "two addresses show the same page" fits this exact case.
5. **Severity**: Warning, right.
6. **Verdict**: right.

## title-missing

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none. All 303 pages have a non-empty first `<title>` in the raw HTML (verify.py).
4. **Words**: good. Plain "the text on the browser tab, and the link people click in search results" is the right gloss.
5. **Severity**: Error, right (Google asks for one on every page; SF Issue/High).
6. **Verdict**: right. **Not seen on a real site.**

## title-multiple

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none. Raw parse (SVG and `<template>` titles excluded, as the rule does) finds one title on every page.
4. **Words**: good.
5. **Severity**: Warning, right.
6. **Verdict**: right. **Not seen on a real site.**

## title-too-long

1. **Fired**: a 14 · j 0 · g 84.
2. **Wrong flags**: none against the rule. Checked all 98: widths match an independent count. Examples: a 48 "The
   Strategy Behind Google’s Most Expensive Acquisitions (Infographic) | Aptitude" = 81; a 51 "Video Games Then and
   Now: How Visuals Have Evolved | Aptitude" = 61; g 105 (blog) = 87; g 278 (product) "Wentworth Rattan Garden Deluxe
   Gas Reclining Chair Set & Table - Royalcraft" = 75; g 123 (product) "Truro Adjustable Folding Sun Lounger -
   Multi-Position Recline" = 61. a 30 (60 exactly) and a 1 (60) correctly not flagged (`>`, TitleRules.cs:31).
3. **Misses**: none (every indexable page measured).
4. **Words**: the Simple title overclaims. "Titles too long to show in full" asserts a cut-off, but **60 of g's 84 flags
   and 4 of a's 14 are 61 to 65 characters**, which usually fit Google's roughly 600 px title link (g 123 above is 61).
   The catalog's own Why says 60 is "a rough guide ... not a rule". Proposed (plain-words.json:686, :698):
   - plainTitle: "Titles that may be cut off in search results"
   - plainPassed: "No title is likely to be cut off in search results"
   - Detail: give the limit, "61 characters (limit 60)", so the near-misses read as near-misses (also in the
     evidence inventory).
5. **Severity**: Notice, right (SF: Opportunity/Low, same 60 threshold).
6. **Verdict**: reword.

## title-too-short

1. **Fired**: a 3 · j 6 · g 10.
2. **Wrong flags**: none. All 19 checked: a 10 "Blog | Aptitude" 15, a 11 21, a 32 25; j 54 "Belts – Jitsunami" 17,
   j 55 "News – Jitsunami" 16, policies 25 to 28; g 9/290 "Login" 5, g 289 "Forgot Password" 15, g 10 "Garden
   Furniture" 16, g 88 "Privacy Policy" 14.
3. **Misses**: none.
4. **Words**: accurate. One gap: 3 of g's 10 are login/password pages and 4 of j's 6 are contact/policy pages, which
   need no work. description-missing's plain words already say "Skip pages nobody searches for, such as login and
   basket pages." Add the same as a 4th fix step here (plain-words.json:700-710).
5. **Severity**: Notice, right.
6. **Verdict**: right (small reword optional).

## description-duplicate

1. **Fired**: a 0 · j 0 · g 4.
2. **Wrong flags**: none. Two pairs of different products sharing one description: g 50/120 (`<meta name="description"
   content="This furniture cover is UV &amp; water-resistant. It&#39;s lightweight ...">` on both, titles "Grey Outdoor
   UV & Water Resistant Furniture Cover" vs "PE Outdoor Rectangle Furniture Cover - Water & UV Resistant") and g 118/1060.
3. **Misses**: none (groups rebuilt from facts on all three sites).
4. **Words**: good in both modes.
5. **Severity**: **change Warning to Notice** (IssueCatalog.cs:609). A missing description is a Notice
   (IssueCatalog.cs:565) and its own Why says descriptions don't affect ranking; a duplicate one has the same
   consequence (Google writes its own snippet). As a Warning it outranks every Notice in "Fix first"
   (ScoreCalculator.cs:55) and costs up to 4 points when a template default covers most pages.
6. **Verdict**: severity change.

## description-missing

1. **Fired**: a 0 · j 6 · g 12.
2. **Wrong flags**: none. j: contact, cart, three policy pages, blog (no `<meta name="description">` in the raw
   HTML). g: FAQ, login x2, forgot-password, three category pages (g 10, 25, 32, 96), privacy, terms, returns, blog.
3. **Misses**: none.
4. **Words**: good; the plain "Skip pages nobody searches for, such as login and basket pages" is exactly what g's and
   j's lists need.
5. **Severity**: Notice, right.
6. **Verdict**: right.

## description-multiple

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none; one `name="description"` per page in the raw HTML everywhere.
4. **Words**: good.
5. **Severity**: Warning, right.
6. **Verdict**: right. **Not seen on a real site.**

## description-too-long

1. **Fired**: a 0 · j 2 · g 203.
2. **Wrong flags**: none against the rule (all measured). j 1 home 201, j 53 Shopify's CCPA page 320; g product
   descriptions up to 681 (g 262). One g detail reads "458 characters (459 counting full-width characters as two)"
   (g 245): the description contains "㎡" (U+33A1, East Asian Wide), so the count is right by UAX #11, but the
   parenthetical is noise when it doesn't change the verdict. Optional: in RuleText.cs:20 add the "counting
   full-width" part only when Characters is within the limit and Width isn't.
3. **Misses**: none.
4. **Words**: same overclaim as titles. **114 of g's 203 flags are 156 to 165 characters** (g 104, 105, 144, 153, 171
   are 156). Proposed (plain-words.json:760, :772): plainTitle "Search descriptions that may be cut off", plainPassed
   "No search description is likely to be cut off"; detail "156 characters (limit 155)".
5. **Severity**: Notice, right (SF same threshold).
6. **Verdict**: reword.

## description-too-short

1. **Fired**: a 1 · j 1 · g 0.
2. **Wrong flags**: none. a 32 `content="How Aptitude collects and uses personal information on this website."` = 68;
   j 54 `<meta name="description" content="dsafasdfsdasdasfd">` = 17 (a real find: keyboard-mash left on the shop's only
   collection page).
3. **Misses**: none.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right.

## charset-missing

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none by the rule: every page has `<meta charset="utf-8">`; j and g also send `text/html;
   charset=utf-8`, a sends `text/html` with the meta at byte < 1024. Gap noted, not a miss here: the rule accepts a meta
   anywhere (HtmlRules.cs:26), while its own fix text says "within the first 1024 bytes". j's meta is at **byte 21,898**
   (Shopify injects a script first) and g's at 1,174; both are harmless only because the header declares UTF-8. A page
   with no header charset and a late meta would pass. Low priority.
4. **Words**: good. The Why's last sentence (crawls before the mark was recorded) is fine for Advanced.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.** Doc fix: docs/v2-rebuild.md:1045-1047 still lists "charset-missing
   doesn't treat a byte-order mark as a charset declaration", which HtmlRules.cs:26 now does (the doc's own :479 says
   so). Delete that limitation.

## lang-missing

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: a `lang="en-GB"`, j `lang="en"` (inside IE conditional comments plus a real `<html class="no-js"
   lang="en">`), g `lang="en"`.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.** Comment fix: HtmlRules.cs:7 says these checks run "on indexable
   pages", but only Open Graph is gated (:31); viewport, lang and charset run on noindex and canonicalised pages too,
   which matches their catalog words ("The page's ..."). Fix the comment, not the code.

## open-graph-missing

1. **Fired**: a 0 · j 0 · g 12.
2. **Wrong flags**: none. 9 of 12 are "Empty ..." and right: g 10 has `<meta property="og:title" content="" />` and
   `<meta property="og:description" content=""/>` (the template copies empty SEO fields); g 9, 289, 290 (login and
   password pages) have no OG tags at all.
3. **Misses**: none; a and j have all three tags with content on every indexable page.
4. **Words**: good; the Missing/Empty detail is the most useful detail in this group.
5. **Severity**: Notice, right.
6. **Verdict**: right.

## viewport-missing

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: a `width=device-width, initial-scale=1, viewport-fit=cover`, j `width=device-width,initial-scale=1`,
   g `width=device-width, initial-scale=1.0` on every page.
4. **Words**: good.
5. **Severity**: Warning, right (mobile-first indexing).
6. **Verdict**: right. **Not seen on a real site.**

## duplicate-content

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: no two indexable pages with 50+ words share a content hash on any site. a's five `/blog/?filter=`
   pages repeat /blog/ (all 1,036 words) but are canonicalised to it, so rightly not compared.
4. **Words**: good.
5. **Severity**: Warning, right (SF "Exact Duplicates" Issue/High; Warning is defensible as Google picks one).
6. **Verdict**: right. **Not seen on a real site.**

## duplicate-site-version

1. **Fired**: 0 · 0 · 0. Ran on all three (no skipped_checks rows).
2. **Wrong flags**: n/a.
3. **Misses**: none. Probes: a ids 2-4 (bare https, http, http www) all 301 to id 1; g the same; j `https://www.` 301
   to the bare host, `http://` 301, `http://www.` 301 to `https://www.` then on (a chain, which is redirect-chain's
   business, not a second copy).
4. **Words**: good in both modes.
5. **Severity**: Warning, right.
6. **Verdict**: right. **Not seen on a real site.**

## low-word-count

1. **Fired**: a 1 · j 3 · g 9.
2. **Wrong flags**: none against the rule. a 11 contact form (98 words; text is the form labels and two sentences);
   j cart 67, contact 100, empty blog 142; g login 90/101, forgot-password 74, contact 143, category hubs g 17 (173), g
   20 (180: four sub-category tiles, read from the HTML), g 25, 32, 96.
3. **Misses**: none by the rule. Observed inflation: g's cookie banner (`<div class="cookiemessage ...">`, 54 words:
   "Cookies in GardenFurnitureSets We use cookies ...") counts as main content on every g page, because region hints
   match only nav/header/footer/sidebar tokens (PageWalker.cs:44). No g page sits at 200-259 words, so it hid no
   flag here. Optional: add cookie/consent tokens (and `role="dialog"`) to the excluded regions; that also changes
   content hashes and fingerprints, so it needs the bench plants re-baselined.
4. **Words**: good. The "contact page doesn't need an essay" line fits a 11 and j 21 exactly.
5. **Severity**: Notice, right (SF Opportunity, same 200).
6. **Verdict**: right.

## near-duplicate-content

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: all-pairs brute force finds no pair within 24 bits (closest g 274/278 37 bits about 90%; g
   76/1023 40 bits; independent shingle cosine 0.85 and 0.80). g 50/120 (duplicate descriptions) are 0.60 alike.
4. **Words**: the Advanced What is jargon: "at most 24 of the 256 bits of their content fingerprints differ"
   (IssueCatalog.cs:681). Proposed: "Another indexable page has almost the same main content, roughly 96% or more alike
   (the threshold can be changed in the analysis settings). Exact copies are reported as duplicate content instead,
   and pages under that check's minimum word count aren't compared." Keep the bits in the settings help.
5. **Severity**: Notice, right.
6. **Verdict**: reword. **Not seen on a real site.**

## url-too-long

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: longest indexable URL is a 49 at 100 characters; independent decode-and-measure finds none over 115.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.**

## url-underscores

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none among pages. Underscores exist only in non-page URLs (j `/cdn/shop/files/favicon_32x32.png`),
   rightly ignored.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.**

## url-uppercase

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none among pages. Uppercase only in a g stylesheet (`/css/cache/combined_main_569D2B...css`), not a page.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.**

## h1-duplicate

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none (first non-empty H1 grouped over indexable pages, all distinct). g has only 4 rel=next/prev links,
   so the series exclusion wasn't exercised.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.**

## h1-missing

1. **Fired**: a 0 · j 2 · g 1.
2. **Wrong flags**: none. `grep -c '<h1'` = 0 on all three: j 20 FAQ (heading only in `<title>`), j 53 Shopify's CCPA
   page, g 90 /blog/ (first headings are `<h2 id="footerHeading" class="sr-only">` and `<h3>`s).
3. **Misses**: none.
4. **Words**: good. ("Often the headline is there to look at, but isn't marked as a heading" fits g 90.)
5. **Severity**: Notice, right (SF Issue/Medium; Google says it rarely relies on headings).
6. **Verdict**: right.

## h1-multiple

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none; one `<h1>` on every page that has one.
4. **Words**: good.
5. **Severity**: Notice, right.
6. **Verdict**: right. **Not seen on a real site.**

## structured-data-invalid

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none: 23 blocks on a (Yoast-style @graph: BlogPosting, BreadcrumbList, Organization, WebSite), 222 on g
   (202 Product, 19 @graph Article/BreadcrumbList/WebPage, 1 Organization), 0 on j's pages read; all parse in Python too.
4. **Words**: good.
5. **Severity**: **change Error to Warning** (IssueCatalog.cs:857). Its consequence is the same as
   structured-data-missing-required (a Warning): that block can't give a rich result. As an Error, one stray comma on
   one page caps the site score at 89 (ScoreCalculator.cs:27, :114) and leads "Fix first", above real broken pages.
6. **Verdict**: severity change. **Not seen on a real site.**

## structured-data-missing-required

1. **Fired**: 0 · 0 · 0.
2. **Wrong flags**: n/a.
3. **Misses**: none by the rule. g's 202 Products all have name + offers (offers: price, priceCurrency, availability,
   itemCondition, shippingDetails); a's BreadcrumbList items carry position, name and item, g's carry position and an
   item object holding the name (both valid for Google). j's product pages were rate-limited, so Shopify product markup was **not
   tested**. Logic gap, unverified on these sites: `REQUIRED` keys exact type names (RichResultRequirements.cs:12-23,
   lookup :37), so subtypes Google treats the same are never checked: `Store`, `Restaurant`, `FurnitureStore` (any
   LocalBusiness subtype), `MusicEvent`/`SportsEvent` (Event subtypes). Fix: map known subtypes to their parent's
   requirements (a small alias table), with a planted subtype case on the synthetic site.
4. **Words**: unverified claim in the Why (IssueCatalog.cs:873): "Google stopped showing FAQ rich results in May 2026".
   Not checkable from the crawls; confirm the date against Google's announcement or drop it ("FAQPage isn't checked,
   as Google no longer shows FAQ rich results for most sites").
5. **Severity**: Warning, right.
6. **Verdict**: fix (subtype gap) + verify one claim. **Not seen on a real site.**

## Cross-cutting

- **Utility pages pile up flags.** g 9 / 290 (login) each carry title-too-short, title-duplicate, description-missing,
  open-graph-missing and low-word-count; j's cart, contact and policy pages likewise. All correct, all low value to the
  owner. Only description-missing's plain words say to skip them; title-too-short and open-graph-missing should too.
- **Indexability gating is consistent**: titles, descriptions, H1s, content, URLs, Open Graph and missing-required run
  on indexable pages only; viewport, lang, charset and invalid JSON-LD on every HTML page. a's 7 canonicalised
  variants had nothing those four would flag.

## Summary

| check | fired a/j/g | wrong flags found | misses found | verdict | one-line change |
| --- | --- | --- | --- | --- | --- |
| title-duplicate | 0/0/2 | 0 | 0 | right | - |
| title-missing | 0/0/0 | - | 0 | right (not seen) | - |
| title-multiple | 0/0/0 | - | 0 | right (not seen) | - |
| title-too-long | 14/0/84 | 0 | 0 | reword | Simple title "Titles that may be cut off in search results"; detail "61 characters (limit 60)" |
| title-too-short | 3/6/10 | 0 | 0 | right | optional fix step "Skip pages nobody searches for, such as login and basket pages." |
| description-duplicate | 0/0/4 | 0 | 0 | severity change | Warning -> Notice (IssueCatalog.cs:609), as description-missing |
| description-missing | 0/6/12 | 0 | 0 | right | - |
| description-multiple | 0/0/0 | - | 0 | right (not seen) | - |
| description-too-long | 0/2/203 | 0 | 0 | reword | Simple title "Search descriptions that may be cut off"; detail with limit; optional RuleText.cs:20 parenthetical only when it changes the verdict |
| description-too-short | 1/1/0 | 0 | 0 | right | - |
| charset-missing | 0/0/0 | - | 0 | right (not seen) | delete stale limitation docs/v2-rebuild.md:1045-1047; optional 1024-byte position check |
| lang-missing | 0/0/0 | - | 0 | right (not seen) | fix HtmlRules.cs:7 comment ("on indexable pages" is true only of Open Graph) |
| open-graph-missing | 0/0/12 | 0 | 0 | right | - |
| viewport-missing | 0/0/0 | - | 0 | right (not seen) | - |
| duplicate-content | 0/0/0 | - | 0 | right (not seen) | - |
| duplicate-site-version | 0/0/0 | - | 0 | right (not seen) | - |
| low-word-count | 1/3/9 | 0 | 0 | right | optional: exclude cookie-banner text (PageWalker.cs:44), 54 words on every g page |
| near-duplicate-content | 0/0/0 | - | 0 | reword (not seen) | Advanced What without "24 of the 256 bits" (IssueCatalog.cs:681) |
| url-too-long | 0/0/0 | - | 0 | right (not seen) | - |
| url-underscores | 0/0/0 | - | 0 | right (not seen) | - |
| url-uppercase | 0/0/0 | - | 0 | right (not seen) | - |
| h1-duplicate | 0/0/0 | - | 0 | right (not seen) | - |
| h1-missing | 0/2/1 | 0 | 0 | right | - |
| h1-multiple | 0/0/0 | - | 0 | right (not seen) | - |
| structured-data-invalid | 0/0/0 | - | 0 | severity change (not seen) | Error -> Warning (IssueCatalog.cs:857): same effect as missing-required, no score cap |
| structured-data-missing-required | 0/0/0 | - | 0 | fix (not seen) | match schema.org subtypes (RichResultRequirements.cs:12-23, :37); verify "May 2026" FAQ claim (IssueCatalog.cs:873) |

Totals: 10 of 26 fired on at least one site, 16 not seen. 0 wrong flags and 0 misses, every page checked against an
independent parse. Verdicts: right 20 (13 of them not seen), reword 3, severity change 2, fix 1 (plus one doc fix and
one comment fix).
