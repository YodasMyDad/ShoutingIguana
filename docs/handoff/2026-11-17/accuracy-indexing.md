# Accuracy audit: indexing

31 checks: canonicals (10), indexability and robots.txt (8), hreflang (8), sitemaps (5). Counts are written a/j/g
(aptitude / jitsunami / garden).

## How this was checked

- Crawls, all finished (log ends `exit 0`): aptitude 33 pages; jitsunami 10 pages (75 URLs in 35 min: every
  product URL answered 429 Too Many Requests); garden 260 pages (4,360 URLs). Before jitsunami finished I analysed a
  copy of it (`audit3b/scratch-indexing/jitsunami.com.sicrawl`, its status set to Stopped so `si analyze` would run).
  It's superseded: every number below comes from the finished crawl's own analysis.
- Every stored response_details and response_html row was dumped (a batch version of `tools/dump.cs`:
  `audit3b/scratch-indexing/tools/batch.cs`). Two scripts in `audit3b/scratch-indexing/` then did the checking:
  - `audit.py` reads the raw HTML with regexes, independently of the engine's parser. It finds every
    `<link rel=canonical>` and `<link rel=alternate hreflang>` (and whether it comes before the first `</head>` or
    `<body`), every robots/googlebot meta tag, and every X-Robots-Tag and Link header, then compares them with the stored
    PageFacts. **Result on all 303 pages of the three sites: no differences.**
  - `expect.py` works out each check of the group again from SQL plus the facts, by the catalog's definition, and
    compares the result with the `issues` table. **Result: the expected set equals the flagged set for every check on
    every site, related URLs included.**
- I then read the flags and a sample of unflagged pages by hand against the HTML (snippets below).

What the sites contain, for the checks that rely on it:

| | aptitude | jitsunami | garden |
| --- | --- | --- | --- |
| Pages (internal HTML 200, parsed) | 33 | 10 | 260 |
| Canonical: names itself / names another URL / none | 26 / 7 / 0 | 10 / 0 / 0 | 249 / 3 / 8 |
| Canonicals in the HTTP Link header (pages) | 0 | 0 (14 CSS/JS files have one, see the note under canonicalised-page) | 0 |
| Canonicals that are relative, outside the head, or more than one | 0 | 0 | 0 |
| Robots meta / X-Robots-Tag | none / none | none / none | `noodp` on 239 pages, `noodp, max-image-preview:large` on 21 / none |
| hreflang (facts; raw `hreflang=` in the HTML) | 0; 0 | 0; 0 | 0; 0 |
| robots.txt of the site's own origins | 200, `Allow: /` + `Sitemap:` | 200, Shopify's standard 122 lines | 200, **empty body** (all 4 versions) |
| Sitemaps | index + 1 child, 26 URLs, all 200 | index + 5 children, 14 URLs, all 200 | none declared; `/sitemap.xml` found by the probe, 240 URLs |
| Internal URLs blocked by robots.txt | 0 | 4 | 0 |

## Canonicals

### canonicalised-page (Notice)
1. Fired 7 / 0 / 3.
2. Wrong flags: none. All 10 checked (every flag, as fewer than 10 per site). In each, the head has one absolute
   canonical naming another URL, which is 200 and indexable:
   - aptitude 23 and 24 (`/contact-us/?type=investment|build`) → `<link rel="canonical" href="https://www.aptitude.co.uk/contact-us/">`
   - aptitude 77, 78, 138, 343, 344 (`/blog/?filter=…`) → `/blog/`
   - garden 406 `/garden-sheds/?p=2` → `<link rel="canonical" href="https://www.gardenfurnituresets.co.uk/garden-sheds/" />`.
     The same holds for 434 `/greenhouses/?p=2` → `/greenhouses/` and 1011 `/blog/?p=2` → `/blog/`.
3. Misses: none. `expect.py` found no page whose first honoured canonical names another URL that went unflagged. The
   other 249 + 26 + 10 canonicals name the page itself exactly.
4. Words. The catalog text is right, and its why even names the garden case ("a template points every canonical at
   … the first page of a series"). **Simple contradicts it**: bullet 2 says canonicalising is "right for copies, such
   as page 2 of a list or a print version" (`plain-words.json:421`). Page 2 of a list isn't a copy: Google's
   pagination guidance says to give each page in a sequence its own canonical, not page 1's. Garden's page 2s have
   their own content (word count 1,196 vs 1,419 on page 1, different content hash), so the products they list are found
   only through a page that asks to be dropped. A non-expert reading Simple would decide there's nothing to do.
   Reword bullet 2: "Google then shows that other page instead, which is right for true copies, such as a sorted or
   filtered version of a list, or a print version." Add a bullet: "Page 2 or 3 of a list isn't a copy: it should
   name its own address."
5. Severity: Notice is right for the parameter variants (aptitude's 7 are textbook). The paginated case deserves a
   Warning. Screaming Frog has "Canonicalised" as Warning/High (docs/v2-comparison.md), too harsh for aptitude's.
   Better: give pagination its own detail, or the backlog check "paginated pages canonicalised to page 1"
   (docs/v2-comparison.md, Pagination row), at Warning. Garden is the real-site plant for it.
6. Verdict: **reword** (Simple), plus a backlog check for paginated pages canonicalised to page 1.
   Side note, outside this check: on jitsunami, Shopify sends `Link: <https://cdn.shopify.com/…/theme.css>;
   rel="canonical"` on 14 internal CSS/JS files. Those files are stored with `indexability_reason=2` (Canonicalised,
   `PageProcessor.cs:224`), so `si stats` "Not indexable: Canonicalised" counts files, not pages. This check is a page
   rule and isn't affected.

### canonical-missing (Notice)
1. Fired 0 / 0 / 8.
2. Wrong flags: none. All 8 checked: garden 7 `/frequently-asked-questions/`, 8 `/contact-us/`, 9 `/login/`,
   88 `/privacy-policy/`, 89 `/terms-conditions/`, 91 `/return-refund-policy/`, 289 `/forgot-password/`,
   290 `/login/?register=true`. In each, the head's only `<link>` elements are the stylesheet and
   `<link rel="shortcut icon" …>`, with `<meta name="robots" content="noodp">` and no Link header. A
   parameter URL (290) without a canonical is exactly what the check is for.
3. Misses: none. Every indexable page without a canonical is flagged, and no noindex page exists on these sites to
   be wrongly left out.
4. Words: clear in both modes. Simple's "Google usually picks well, so there's nothing to do unless each page can be
   reached at many addresses" is honest and right.
5. Severity: Notice is right.
6. Verdict: **right**.

### canonical-broken (Error), canonical-invalid (Warning), canonical-multiple (Warning), canonical-outside-head (Warning), canonical-relative (Notice), canonical-to-redirect (Warning), noindex-with-canonical (Warning)
1. Fired 0 / 0 / 0 for each.
2. Wrong flags: none possible.
3. Misses: none. All 295 canonicals on the three sites (one per page that has one) are single, absolute, in the head as the regex reads it, and
   resolve to a 200 page. The one extra `rel=canonical` the regex found (aptitude 45) is prose in a blog post ("you
   don't do the rel=canonical and all those sorts of things"), which the parser rightly ignores. No page is noindex.
4. Words: all clear and accurate. canonical-to-redirect's "what" and "fix" tell the owner what to do when the end of
   the redirects wasn't checked. Nothing to change.
5. Severity: all right. canonical-broken as Error is defensible: the effect is the same as no canonical, but it is
   nearly always a template fault, such as a staging host.
6. Verdict: **right, not seen on a real site** (all seven).

### canonical-to-non-indexable (Warning)
1. Fired 0 / 0 / 0.
2. Wrong flags: none.
3. Misses: none. No canonical names a non-indexable URL.
4. Words: the catalog says "blocked by robots.txt or a firewall". See 6.
5. Severity: Warning is right.
6. Verdict: **fix, not seen on a real site.** `CanonicalRules.cs:88` counts a target the crawler was refused at
   (`FetchOutcome.Blocked`: a firewall, a bot challenge, or a 429 rate limit) as non-indexable, and the detail then reads
   "The canonical URL is blocked by a firewall" (`RuleText.cs:32`). `SitemapRules.cs:65` treats the same outcome as
   unverified and says nothing, which is right: the URL is already flagged `internal-blocked`, and being refused tells us
   nothing about Googlebot. Jitsunami shows how easily it happens. All 12 product URLs, including
   `/collections/belts/products/…` (ids 65-73), answered 429 to this polite crawl (2 req/s). Had the collection page
   versions loaded while `/products/…` was rate-limited, each would have been flagged here, because of our own crawl
   rate. Change line 88 to `(target.IsInternal && target.Outcome == FetchOutcome.RobotsDisallowed) ||` and drop "or a
   firewall" from the catalog "what" (`IssueCatalog.cs:390`). The test will need a planted canonical to a 429 target
   to stay unflagged.

### canonical checks, Simple words
Read as a site owner, they are good: "main address" is a sound gloss for canonical, and each says who fixes it.
Only canonicalised-page needs a change (above).

## Indexability and robots.txt

### blocked-by-robots (Notice)
1. Fired 0 / 4 / 0.
2. Wrong flags: none. All 4 were checked against jitsunami's robots.txt (`User-agent: *` group; the only other group
   is `adsbot-google`):
   - 9 `/checkouts/internal/preloads.js?locale=en-GB`: `Disallow: /checkouts/`
   - 24 `/checkout`: `Disallow: /checkout`
   - 26 `/cart/change?id=0&quantity=0`: `Disallow: /cart/`
   - 44 `/account/register`: `Disallow: /account` (`Allow: /account/login` doesn't match it)
3. Misses: none. Internal `RobotsDisallowed` responses are 0 / 4 / 0, and all are flagged. Aptitude's 44
   robots-blocked URLs are all on other sites, correctly left out.
4. Words: clear. Simple's "That's right for baskets and site searches, and then there's nothing to do" fits all
   four exactly. One limit (unverified on these sites, none of which has a Googlebot group): rules are matched for
   the crawler's own token, falling back to `*` (`RobotsTxtParser.cs:7-9`). The words say "search engines", so a
   robots.txt with a `User-agent: Googlebot` group that differs from `*` would be described wrongly. Worth a backlog
   note: match for `googlebot` too when judging.
5. Severity: Notice is right. Screaming Frog's Warning/High would have put jitsunami's cart and checkout in the
   to-dos.
6. Verdict: **right**.

### noindex-page (Notice), nofollow-page (Warning), snippet-blocked (Notice), site-hidden-from-search (Error)
1. Fired 0 / 0 / 0 for each.
2. Wrong flags: none possible.
3. Misses: none. The regex found no `noindex`, `nofollow`, `none`, `nosnippet` or `max-snippet` in any robots or
   googlebot meta tag, and no X-Robots-Tag header, on any of the 303 pages. A directive close to snippet-blocked
   is correctly not flagged: garden's 21 pages with `content="noodp, max-image-preview:large"` (e.g. the home page)
   don't block the snippet. `noodp` (obsolete, 260 garden pages) is rightly ignored.
4. Words: all clear. nofollow-page's Simple ("rarely meant: usually left over from when the site was built") is right
   for a normal page. On a `noindex, nofollow` login or basket page it's often deliberate, and the words don't say so.
5. Severity: right, with one refinement for nofollow-page (unverified on a real site). When the page is also noindex,
   it is a utility page by choice, so a Notice would fit better than a Warning. Small change: in
   `IndexabilityRules.cs:16`, report nofollow on a noindex page as a Notice, or add "On a page that's also kept out of
   search, such as a login or basket page, it's usually deliberate" to the why and the Simple bullet 3.
6. Verdict: **right, not seen on a real site** (all four). nofollow-page has an optional severity refinement.

### robots-txt-unreachable (Error), robots-txt-unreachable-other-origin (Warning), robots-txt-missing (Notice)
1. Fired 0 / 0 / 0 for each.
2. Wrong flags: none possible.
3. Misses: none. Every one of the site's own origins (4 per site: the start origin and its www/http versions)
   answered 200 (`robots` table). Failures were all on other sites: aptitude `challenges.cloudflare.com` 404,
   `blog.creativelive.com` TLS error, `http://t.co` 520; garden `instant.page` 404, `www.copperfieldshop.co.uk` no
   response. They are rightly left out (`RobotsTxtRules.cs:33-34`). `app.jitsunami.com` is out of scope (subdomains
   off), so it isn't "another of the site's origins".
4. Words: clear and accurate in both modes. They match Google's documented handling of 4xx, 5xx and 429.
5. Severity: right (Error, Warning, Notice in order of effect).
6. Verdict: **right, not seen on a real site** (all three).
   A gap seen on a real site, not a fault of these checks: garden's robots.txt is an empty 200, and its only sitemap
   was found by guessing `/sitemap.xml` (`sitemaps.is_probe=1`). Nothing tells the owner that robots.txt doesn't name
   the sitemap, though robots-txt-missing's own why calls that the main use of the file. Backlog proposal: a Notice
   "robots.txt doesn't name your sitemap". It would fire when no robots.txt on a main origin has a `Sitemap:` line and
   a sitemap was found only by the probe or from the settings.

## Hreflang

None of the three sites declares hreflang: there are 0 hreflang facts and no `hreflang=` anywhere in the raw HTML of
the 303 pages. So all eight checks below fired 0 / 0 / 0, there can be no wrong flags or misses, and each is "not
seen on a real site". What follows comes from reading the code (`Analysis/Rules/HreflangRules.cs`,
`HreflangCodes.cs`) and the words. The rule runs only on indexable pages (`:21`), counts only head and Link-header
entries (`:26`), and judges targets through `HonouredTargets` (`:143-145`: head or header links, not the page
itself), as Google does. Return links are checked only between the site's own indexable pages (`:125`), which is the
documented scope.

### hreflang-to-non-indexable (Warning)
1-3. Fired 0 / 0 / 0. No wrong flags or misses possible.
4. Words. "(or, on this site, by a firewall)" (`IssueCatalog.cs:812`) reads as "this website" to an owner. Simple's
   bullet 2 (`plain-words.json:1060`) leaves out robots.txt, though the check includes it: "That address forwards
   elsewhere, is told to stay out of search, names another page as its main version, or is off limits in your rules
   file for search engines."
5. Severity: Warning is right.
6. Verdict: **fix, not seen on a real site.** Two logic points:
   - `HreflangRules.cs:78` counts an internal `Blocked` alternate (a firewall, or a 429 caused by our crawl rate) as
     non-indexable. This is the same fault as canonical-to-non-indexable above, against `SitemapRules.cs:65`, and
     jitsunami's 429s show the risk. Drop that line.
   - `HreflangRules.cs:77` counts `RobotsDisallowed` with no `IsInternal` guard, so an alternate on another site whose
     robots.txt refuses our crawler's token is flagged. `CanonicalRules.cs:87-88` deliberately excludes that ("Another
     site … disallowing the crawler says nothing about whether Google can see the page"). Use
     `(status.Outcome == FetchOutcome.RobotsDisallowed && status.IsInternal)`.
   After the fix, the catalog "what" becomes: "…redirect, are noindex, name another URL as canonical, or are blocked by
   your robots.txt."

### hreflang-broken-target (Error), hreflang-duplicate-code (Warning), hreflang-invalid-code (Error), hreflang-missing-return (Warning), hreflang-missing-self (Warning), hreflang-missing-x-default (Notice), hreflang-outside-head (Warning)
1-3. Fired 0 / 0 / 0 for each. No wrong flags or misses possible.
4. Words: accurate and clear in both modes. Simple glosses hreflang well ("language versions"). invalid-code's
   "A common slip is 'en-UK'" is the right example. `HreflangCodes.cs` checks `uk` only as a language (Ukrainian), so
   `en-UK` is invalid, as the words say.
5. Severity: right. invalid-code and broken-target are Errors because the entry does nothing. x-default is a Notice,
   as it's optional.
6. Verdict: **right, not seen on a real site** (all seven).

## Sitemaps

### not-in-sitemap (Notice)
1. Fired 0 / 4 / 18.
2. Wrong flags: none by the definition (indexable page, sitemaps all read, crawl finished). All 22 checked, by URL
   type:
   - garden products (13: ids 369 `/rio-03/`, 1017, 1021, 1023, 1029, 1033, 1040, 1042, 1045, 1060, 1062, 1084, 1086).
     Each is 200, canonical to itself, and absent from the 240-entry `/sitemap.xml`. No other form of the URL (http, no
     slash) is in the sitemap either (`urls` searched by slug). This is a real find: the sitemap is missing products.
   - garden utility pages (5): 9 `/login/`, 289 `/forgot-password/`, 290 `/login/?register=true`, 88 `/privacy-policy/`,
     89 `/terms-conditions/`.
   - jitsunami (4): 25 `/cart` (`<link rel="canonical" href="https://jitsunami.com/cart">`, no robots meta) and 36-38
     `/policies/…`. Shopify's pages sitemap lists only faqs, contact and ccpa-opt-out.
   Aptitude: all 26 indexable pages are in its sitemap, so the result is 0, which is correct.
3. Misses: none. `expect.py` matches exactly on every site.
4. Words: accurate, but they point the owner toward the wrong fix for 4 of garden's 18 and 1 of jitsunami's 4. Login,
   password-reset, register and basket pages don't belong in a sitemap: they belong out of search. Simple's "Leaving
   out some kinds of page on purpose is fine" doesn't name them. Add a Simple bullet: "Login, basket and account pages
   don't belong in a sitemap: if any are listed, they'd be better kept out of search altogether." Add to the why:
   "Login, account and basket pages are better made noindex than added."
5. Severity: Notice is right.
6. Verdict: **reword**.

### sitemap-non-indexable (Warning)
1. Fired 0 / 0 / 1.
2. Wrong flags: none. Garden 114 `/product-videos/` is in the sitemap and answered `500` (`responses.status_code`), with
   the detail "The URL returns 500 Internal Server Error". Whether that 500 is lasting can't be told from the crawl.
   Recheck would settle it.
3. Misses: none. The other 239 garden entries are 200, indexable HTML. All 26 aptitude entries are indexable.
   Jitsunami's 8 product entries all answered 429 and are left out by design (`SitemapRules.cs:65`, "unverified"),
   and `internal-blocked` reports them. `agents.md` (jitsunami 47, a 200 non-HTML file in
   `sitemap_agentic_discovery.xml`) is rightly not flagged.
4. Words: clear. "It's often listed as a problem of its own" fits: 114 is also a 5xx.
5. Severity: Warning is right.
6. Verdict: **right**.

### sitemap-error (Error)
1. Fired 0 / 0 / 0.
2. Wrong flags: none possible.
3. Misses: none. All 9 sitemaps on the three sites answered 200 with no error (`sitemaps` table).
4. Words: clear.
5. Severity: **Error is too strong for the scale** ("Broken for search engines or users now: fix first",
   `IssueSeverity.cs`). The check's own why says "well-linked pages are still found" and "a referenced sitemap that
   returns 404 is usually stale and only needs removing". Make it a Warning (`IssueCatalog.cs:470`).
6. Verdict: **severity change, not seen on a real site**.

### sitemap-missing (Notice)
1. Fired 0 / 0 / 0.
2. Wrong flags: none possible.
3. Misses: none. Every site has a sitemap. Garden's was found only by the `/sitemap.xml` probe, which rightly counts
   as found (`SitemapRules.cs:92`). The gap that remains is the robots.txt `Sitemap:` line (see the robots.txt
   section).
4. Words: Simple's bullet 3, "Check the site again: two more checks can then run." (`plain-words.json:634`), is vague
   to an owner. Reword: "Check the site again: it can then also find pages missing from your sitemap, and pages in it
   that nothing links to."
5. Severity: Notice is right.
6. Verdict: **reword (minor), not seen on a real site**.

### sitemap-url-other-host (Warning)
1. Fired 0 / 0 / 0.
2. Wrong flags: none possible.
3. Misses: none. All 280 sitemap entries share their sitemap's scheme and host (checked with `urlsplit`). An http
   entry in an https sitemap, which the docs list as a known limitation, doesn't occur here.
4. Words: clear.
5. Severity: Warning is right.
6. Verdict: **right, not seen on a real site**.

## Summary

| check | fired a/j/g | wrong flags found | misses found | verdict | one-line change |
| --- | --- | --- | --- | --- | --- |
| canonicalised-page | 7/0/3 | 0 | 0 | reword | Simple bullet 2: page 2 of a list isn't a copy; backlog: paginated-to-page-1 check (Warning) |
| canonical-missing | 0/0/8 | 0 | 0 | right | none |
| canonical-broken | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-invalid | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-multiple | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-outside-head | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-relative | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-to-redirect | 0/0/0 | 0 | 0 | right (not seen) | none |
| canonical-to-non-indexable | 0/0/0 | 0 | 0 | fix (not seen) | `CanonicalRules.cs:88`: don't count `Blocked` (429/firewall) targets; drop "or a firewall" |
| noindex-with-canonical | 0/0/0 | 0 | 0 | right (not seen) | none |
| blocked-by-robots | 0/4/0 | 0 | 0 | right | backlog: judge Googlebot's group too |
| nofollow-page | 0/0/0 | 0 | 0 | right (not seen) | optional: Notice when the page is also noindex |
| noindex-page | 0/0/0 | 0 | 0 | right (not seen) | none |
| snippet-blocked | 0/0/0 | 0 | 0 | right (not seen) | none |
| site-hidden-from-search | 0/0/0 | 0 | 0 | right (not seen) | none |
| robots-txt-unreachable | 0/0/0 | 0 | 0 | right (not seen) | none |
| robots-txt-unreachable-other-origin | 0/0/0 | 0 | 0 | right (not seen) | none |
| robots-txt-missing | 0/0/0 | 0 | 0 | right (not seen) | backlog: "robots.txt doesn't name your sitemap" (garden) |
| hreflang-broken-target | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-duplicate-code | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-invalid-code | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-missing-return | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-missing-self | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-missing-x-default | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-outside-head | 0/0/0 | 0 | 0 | right (not seen) | none |
| hreflang-to-non-indexable | 0/0/0 | 0 | 0 | fix (not seen) | `HreflangRules.cs:77-78`: only internal robots blocks count, `Blocked` doesn't; reword "on this site"; Simple adds robots.txt |
| not-in-sitemap | 0/4/18 | 0 | 0 | reword | say login/basket/account pages belong out of search, not in the sitemap |
| sitemap-non-indexable | 0/0/1 | 0 | 0 | right | none |
| sitemap-error | 0/0/0 | 0 | 0 | severity change (not seen) | Error → Warning |
| sitemap-missing | 0/0/0 | 0 | 0 | reword, minor (not seen) | Simple bullet 3: name what the recheck adds |
| sitemap-url-other-host | 0/0/0 | 0 | 0 | right (not seen) | none |

Verdicts: 25 right (22 of them not seen on a real site), 2 fix (both not seen), 3 reword (1 not seen), 1 severity
change (not seen), 0 drop. Only 5 of the 31 fired on any of the three sites (canonicalised-page, canonical-missing,
blocked-by-robots, not-in-sitemap, sitemap-non-indexable): 45 flags in all, every one correct, and no misses.
None of the sites uses hreflang, noindex or nofollow, so those checks rest on the synthetic site's plants and this
code read alone.
