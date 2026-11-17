# Accuracy audit: responses

30 checks: Links (7), Redirects (6), Response codes (5), Images (5), Security (4), Performance (3).
a = www.aptitude.co.uk, j = jitsunami.com, g = www.gardenfurnituresets.co.uk. Counts are flagged pages (or URLs
for URL checks) in each crawl's own analysis.

## How this was checked

- **Crawls.** All three finished (`exit 0`): a 602 URLs / 33 pages; g 6,051 URLs found, 4,360 fetched / 260 pages (1,691 nofollow lightbox links not followed); j 75 URLs / 10 pages
  in 35 minutes, Shopify refusing every product page with 429. While j and g ran I analysed backup-API copies
  (`scratch-responses/jitsunami.copy.sicrawl`, `garden.snap.sicrawl`, `crawl.status` set to Stopped on the COPY so
  `si analyze` would run); every figure below is from the **final** crawl files and their own analyses, re-run
  through the same scripts.
- **Recomputation.** For every check in the group I re-derived the expected flags (page and target ids) from the raw
  tables in Python, independently of the engine (`scratch-responses/model.py`, `diff_links.py`, `diff_site.py`,
  `diff_facts.py`, driven by `run_all.sh`), and diffed them with `issues` / `issue_targets`. On all three final crawls
  every check matched exactly (0 missing, 0 extra, 0 target differences). So the rules do what their code says;
  the findings below are about whether that is right on real pages.
- **Flags** were checked against the stored HTML, headers and facts (`tools/dump.cs`, and a bulk dumper,
  `scratch-responses/tool/dumpall.cs`, into `apt/`, `jitF/`, `gF/`). **Misses** were hunted with scans that don't use
  the engine's parser: a raw regex pass over every stored page for `<img>` without alt or with alt over 125 characters
  (`rawimg.py`), for `http://` in resource `src`/`href`/`srcset`, for `http-equiv="refresh"`, for error wording in
  titles and H1s (`soft404_scan.py`), for unsized images by class (`dims_css.py`), plus SQL over every non-200
  response and every image over 200 KB.
- Screaming Frog severities come from `docs/v2-comparison.md` where it says so; anything else about SF is from
  memory and marked so.

## broken-external-links

1. **Fired:** a 8 pages (52 target URLs), j 0, g 1 page (1 target).
2. **Wrong flags: none found.** Checked all 8 pages on a and every one of their 52 targets:
   - 37 on url 37 (fonts post): 31 `http://unsplash.com/post/...` that 301 to `https://unsplash.com/post/...` 404
     (e.g. 160 -> 315), and 6 `https://www.google.com/fonts/specimen/<Font>` that 404 (173, 185, 189, 193, 197, 201).
   - url 30: stonetemple 404 (93); blog.creativelive 301 -> https whose certificate fails (98, RemoteCertificateNameMismatch,
     a browser would also refuse it).
   - url 36: topsy.com DNS has no record (286); soloseo.com timed out once at 20 s (306).
   - url 42: 500 (384). url 44: DNS lookup failed (426). url 45: blogspot.co.uk 302 -> .com 404 (453 -> 556).
     url 52: socialmediatoday 301 -> 404 (489 -> 576).
   - url 29: `http://www.matthewbarby.com/` and one post answer **409** (110, 111). 409 isn't a usual bot refusal and
     the host's robots.txt also answered 409, so the site looks dead, but external headers aren't stored, so *who*
     sent the 409 (origin or CDN) is **unverified**.
   - g: url 106 (Oxford fabric post): `<a rel="noopener" href="https://www.copperfieldshop.co.uk/blogs/news/a-brief-history-of-the-oxford-shirt" target="_blank">` (1056), DNS lookup failed. Real.
3. **Bot walls (the brief's question).** The crawler stores the reason with outcome Blocked, and the check
   (`UrlStatus.IsBroken`, UrlStatus.cs:54) never counts Blocked or 401/403/405/429/503/999 on other sites. On a,
   13 external URLs were walled and none flagged: Cloudflare challenge 403 (neilpatel.com 101, quora.com 289,
   millionshort.com 285, visual.ly 551, smartinsights.com 545/546, w3.org 547), DataDome 403 (forbes.com 376,
   fastcompany.com 589), AWS WAF challenge 202 (fontsquirrel.com x7, 415-466). g: 2 Cloudflare-challenge 403s, not
   flagged. So a 403/429 from a CDN bot wall is correctly **not** a broken link. External response headers are not
   stored (0 `response_details` rows for external URLs), so the block reason is the only stored evidence; it is
   enough here.
4. **Misses:** none that the data can prove. Unchecked rather than missed: a `getsidekick.com` 403 with a 138-byte
   body (510) is excluded as a possible bot refusal; 44 external URLs were never fetched because their own robots.txt
   disallows it (twitter.com, facebook.com) or couldn't be read (6 `t.co` links: robots.txt 520; 1 robots.txt
   "unreachable: 200 OK"). None of that is said anywhere a site owner reads.
5. **Words.** Clear. The What lists the excluded statuses but not the two other ways a link goes unchecked.
   Proposed What (IssueCatalog.cs:160), append: "Links refused by the other site's bot protection (such as
   Cloudflare), or that its robots.txt kept the crawler from checking, aren't counted either." Plain words fine.
6. **Severity:** Warning is right (visitors hit a dead end; not a ranking factor, which the Why says).
7. **Verdict: reword** (What only).

## broken-fragment-links

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** a has 172 fragment links: `/#ventures`, `/#approach` from all 33 pages and `#main` on every page;
   the home's stored anchor ids include `ventures`, `approach`, `contact`, `main`, and every page has `main`.
   `#toc-you-can-t-beat-free` and `#!featured` point at external pages (not checked by design). g: `#one`..`#six`
   on 107 all have matching `id="one"`..`id="six"` in its HTML. j: no fragment links.
4. **Words:** clear and honest (JS-added ids caveat). No change.
5. **Severity:** Notice, right.
6. **Verdict: right, not seen on a real site.**

## broken-internal-links

1. **Fired:** a 0, j 1, g 4 pages (8 targets).
2. **Wrong flags: none found.**
   - j url 54 (`/collections/belts`): a sidebar promo `<a href="/collections/mens" class="pt-layout-outside pt-promo-02
     sidebarpromo">` (HTML line 810) -> 63, 404. Real.
   - g: blog and category copy linking to products that no longer exist, all 404: 15 `<a rel="noopener" href="/outdoor-pe-rattan-dining-set-with-fire-pit-table/" target="_blank">deluxe 7-piece outdoor furniture set` (325); 97 `<a rel="noopener" href="/berlin-cushion-box/" target="_blank">store the cushions away` (1018); 106 -> 1064; 107 five buttons such as `<a ... href="/henbury-6-seater-dining-set/" target="_blank" class="btn btn-brand ...">` (1072-1076). All real.
3. **Misses:** a has no internal 4xx/5xx at all. The recomputation found no link to a broken internal URL that went
   unflagged on any site.
4. **Words:** clear. The plain "Every link between your pages leads to a working page" is good.
5. **Severity:** Error, right (SF: Internal Client Error is Issue/High, from memory).
6. **Verdict: right.**

## empty-anchor-text

1. **Fired:** a 0, j 9, g 260 (every page).
2. **Wrong flags: none found.** j: every page's logo, `<a href="/"><img src=".../jitsunami1-final_120x.png" alt=""></a>`
   (four copies in the header, 20.html lines 464, 480, 626, 638), empty alt, so the link has no name. g: the header
   logo `<a href="/" class="flex h-full justify-center items-center"><svg class="h-6 md:h-8" ...>` with no `<title>`,
   aria-label or text (35.html; checked: the 9.5 KB link holds no `<title` and no `aria`). The breadcrumb's home link
   has `<span class="sr-only">Home</span>` and is correctly not counted. g's home (url 1) is flagged for its promo
   tiles, e.g. `<a href=".../garden-sheds/" class="block relative"><img src="/media/7406/gfs-home-garden-sheds-...jpg?width=950"
   ... alt="" ...></a>` (targets 29, 31, 35, 37): image links with empty alt. Real.
3. **Misses (minor):** the home's own logo links are never counted (j: url 1 has four `<a href="/">` with flags
   SamePage|ImageLink and no text, and the home isn't flagged; g's home is flagged only for its tiles, not its logo):
   LinkRules.cs:18 skips every link to the page itself before the empty-text test, so the home is left out of the
   logo's group. Low impact; a possible fix is to test empty text before the self-link `continue` when the link has
   no fragment (so `href="#"` buttons stay out).
4. **Words:** good; the plain OneFix ("almost certainly your logo") was exactly right on both sites.
5. **Severity:** Notice, right.
6. **Verdict: right** (optional minor fix above).

## internal-nofollow-links

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** The only internal `rel="nofollow"` links on any site are g's product lightbox links,
   `<a href="/media/6264/...jpg?width=1400&upscale=false" rel="nofollow" class="block lightbox">` (1,691 such URLs
   skipped as nofollow), which the check excludes as resources (by extension, as they weren't fetched), as its What
   says. Correct.
4. **Words:** clear. No change.
5. **Severity:** Warning. Fine.
6. **Verdict: right, not seen on a real site.**

## links-to-redirects

1. **Fired:** a 0, j 10, g 0.
2. **Wrong flags: 10 of 10 on j.** Every one is the theme's "Sign In" link, `<a href="/account/login" class="btn
   btn-dark">Sign In</a>` (20.html line 988), to 43, which Shopify answers with **302** to
   `https://shopify.com/78530740512/account?locale=en&...&buyer_flags=<signed token>`. The fix the check gives,
   "Change the links to point straight at the final URL", is wrong here: the final URL is Shopify's hosted login
   with a per-request token, a store owner can't and shouldn't link to it, and every Shopify store will get this.
   More generally, a temporary redirect means the linked URL stays the right one (Google treats 302/307 as at most a
   weak canonical signal, as redirect-temporary's own Why says), so linking to it is correct.
3. **Misses:** none. a and g have no internal redirects other than the crawler's site-version probes (ids 2-4),
   which nothing links to.
4. **Words:** What says "redirect ... to a working page"; here the end is another site's login page.
5. **Severity:** Notice, right.
6. **Verdict: fix.** LinkRules.cs:51: count only permanent moves:
   `else if (target is { Fetched: true, Outcome: FetchOutcome.Redirect } && target.StatusCode is not (302 or 303 or 307))`
   (a page turned redirect by an instant meta refresh keeps status 200, so it still counts). What
   (IssueCatalog.cs:172): "The page links to internal URLs that redirect permanently (301 or 308, or an instant meta
   refresh) to a working page. Links to temporary redirects, such as a login page, don't count: the linked URL is
   still the one to use." Needs a planted temporary-redirect link that must not be flagged.

## orphan-page

1. **Fired:** a 0, j 3, g 1.
2. **Flags.**
   - g url 96 `/metal-patio-sets/metal-sofa-sets/` ("Metal Sofa Sets", 121 words, self-canonical, in the sitemap):
     the only link to it anywhere is its own canonical (`links` kind 7 from 96), and a raw grep of all 260 stored pages
     finds the path only in 96 itself. The live section is `/metal-furniture/metal-patio-sets/`, so this is an old URL
     left in the sitemap. **Right.**
   - j 53 `/pages/ccpa-opt-out`, 54 `/collections/belts`, 55 `/blogs/news`: none of the 10 pages that were read links
     to them (a raw grep of their HTML for the three paths finds nothing). But 12 of j's 22 internal HTML URLs, every
     product page (27, 29, 48-52, 65, 67, 69, 71, 73), were refused with 429, so their links were never read, and a
     product page's breadcrumb or "back to collection" link is exactly how `/collections/belts` would be reached
     (it is the page that lists those 5 products). So the three are **unproven**: plausible for `/blogs/news` and the
     CCPA page, doubtful for `/collections/belts`. Not counted as wrong, but the check shouldn't have claimed them.
3. **Misses:** none. a: all 26 sitemap URLs are linked from 6 to 32 pages. The recomputation (inlinks credited
   through redirects, nofollow counted) matches on a, j and g.
4. **Logic gap.** The check only runs on complete crawls because "a partial crawl can't tell an orphan from a page
   whose linking pages weren't fetched" (InlinkRules.cs class summary), and `SeesEveryLinkingPage`
   (CrawlAnalyzer.cs:385-413) also skips it after depth or URL-limit skips. Internal pages the crawler was refused or
   couldn't reach are the same case and aren't considered: j finished "Completed" with every product page unread.
   **Fix:** in `SeesEveryLinkingPage`, also skip `orphan-page` when internal, non-probe URLs that aren't resources
   ended `Blocked`, `Timeout` or `NetworkError`, with the same kind of note: "Orphan pages weren't checked: 12
   internal URLs refused the crawler (429 Too Many Requests), and pages linked only from them would look orphaned."
   (A new `SkippedCheckReasons` code.) I'd leave 5xx out: g's one 500 (`/product-videos/`) would otherwise have
   hidden g's real orphan. Needs a test with a blocked linking page.
5. **Words:** clear. Plain words fine.
6. **Severity:** Warning, right.
7. **Verdict: fix** (logic gap; the one provable flag, on g, is right).

## meta-refresh

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** A raw grep for `http-equiv=refresh` (any quoting) over every stored page of all three sites finds
   nothing.
4. **Words:** clear, and fair about the fallback case. No change.
5. **Severity:** Warning, fine.
6. **Verdict: right, not seen on a real site.**

## redirect-broken

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** The only internal redirects on a and g are the three site-version probes (301 to the home,
   which works); j's are the probes and `/account/login` (302 to a working Shopify page).
4. **Words:** clear. No change.
5. **Severity:** Error, right.
6. **Verdict: right, not seen on a real site.**

## redirect-chain

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none** for the check as defined: nothing linked, in a sitemap or the start URL takes two hops. j's
   probe `http://www.jitsunami.com/` does (-> `https://www.` -> bare domain, ids 4 -> 2 -> 1) and isn't flagged, by
   design, as nothing links to it.
4. **Words:** clear. No change.
5. **Severity:** Warning. SF has it as an Opportunity (from memory, unverified); a Warning for 2 hops is a little
   harsh but the Why says "Two hops is rarely a problem in itself". Keep.
6. **Verdict: right, not seen on a real site.**

## redirect-downgrade

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. No HTTPS URL on any site redirects to HTTP (probes go http -> https). No misses.
4. **Words:** clear. 5. **Severity:** Error, right.
6. **Verdict: right, not seen on a real site.**

## redirect-loop

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. No loops in any `responses.redirect_url_id` chain (traced every internal redirect). No misses.
4. **Words:** clear. 5. **Severity:** Error, right.
6. **Verdict: right, not seen on a real site.**

## redirect-temporary

1. **Fired:** a 0, j 1, g 0.
2. **Wrong flag (in effect): 1 of 1 on j.** url 43 `https://jitsunami.com/account/login`, `302 Found` to
   `https://shopify.com/78530740512/account?...&buyer_flags=<token>`: Shopify's own hand-off to its hosted customer
   accounts, the same on every Shopify store, not a page that moved. The owner can't change it and shouldn't. The
   words do say a login redirect is fine to leave, so it's noise rather than harm, but it will be on every Shopify
   crawl.
3. **Misses:** none. a and g: no temporary redirects at all (probes are 301).
4. **Words:** clear.
5. **Severity:** Notice, right.
6. **Verdict: fix (small).** RedirectRules.cs:137: skip temporary redirects that leave the site:
   `if (url.StatusCode is 302 or 303 or 307 && urls[url.RedirectTargetId].IsInternal)`. A temporary hop to another
   site is a hosted login, checkout, SSO or tracking hand-off, not a move whose canonical the owner should fix. What
   (IssueCatalog.cs:111): "This internal URL redirects to another URL on the site with a temporary status code: 302,
   303 or 307. Redirects to other sites, such as a hosted login page, don't count." Planted case: an internal 302 to
   an external URL, not flagged. (The http -> https probe with a 302 would still be flagged, which is right.)

## internal-blocked

1. **Fired:** a 0, j 12, g 0.
2. **Flags correct as facts, title wrong.** All 12 on j are Shopify **rate limits**, detail "Rate limited (429 Too
   Many Requests)": every product page, 27, 29 (linked from the home), 48-52 (from the sitemaps) and 65, 67, 69, 71,
   73 (from `/collections/belts`). The crawl ran at 2 requests a second, concurrency 2, Crawl-delay respected (options_json)
   and took 35 minutes for 75 URLs, so it wasn't being rude; Shopify refuses this user agent's product requests. No
   firewall is involved, yet the title says "Internal URL blocked by a firewall". (It also undermines the orphan
   check: see orphan-page.)
3. **Misses:** none. Every internal response with outcome Blocked on any site is flagged; a and g have none.
4. **Words.** Technical title (IssueCatalog.cs:49) misnames the 429 case; the What already covers it. Proposed
   title: **"Internal URL refused the crawler"**. Plain title "Pages or files your site's security wouldn't show" and
   its first sentence ("Your site's firewall or security service turned the check away") also assume a firewall.
   Proposed plain title: "Pages or files your site wouldn't show the check"; sentence 1: "Your site turned the check
   away from some addresses, either through its security service or because the check asked too fast ("too many
   requests"), so they couldn't be read." The fix step "Ask your hosting company ... to let Shouting Iguana through"
   can't be done on Shopify; the 429 step is the useful one and should come first when the detail says 429. (Plain
   words are edited in `engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json`, then
   regenerated.)
5. **Severity: Warning -> Notice.** The URLs work for visitors (the Why says so), the owner often can't change the
   rule, and a host refusing an unknown crawler says nothing about Googlebot (whether Shopify lets Googlebot through
   is my expectation, unverified); what's wrong is the crawl's coverage.
   As a Warning it lowers the site's score for the crawler's problem. (SF would list a 429 as a 4xx client error,
   from memory.)
6. **Verdict: reword + severity change.**

## internal-client-error

1. **Fired:** a 0, j 2, g 8.
2. **Wrong flags: none found.** j: 63 `/collections/mens` 404 (linked from the belts sidebar promo) and 64
   `/cdn/shop/t/4/assets/listing-banner.jpg?v=304` 404 (that promo's lazy image, `data-src` at 54.html line 813).
   g: the 8 product URLs above (325, 1018, 1064, 1072-1076), each 404 with `text/html`, each found on the blog/category page that links to it.
3. **Misses:** none: every internal 4xx response (outside the probes) is flagged on every site.
4. **Words:** clear; plain words good.
5. **Severity:** Error, right (SF Issue/High, from memory).
6. **Verdict: right.**

## internal-server-error

1. **Fired:** a 0, j 0, g 1.
2. **Wrong flags: none found.** g url 114 `/product-videos/` (listed in the sitemap, linked from no page) answered
   `500 Internal Server Error` through Cloudflare (`server: cloudflare`, `cf-cache-status: DYNAMIC`) in 491 ms; no
   body is stored for errors. A single measurement (500 isn't retried; 502-504 are), so whether it persists is
   **unverified**, which the words already tell the owner to check.
3. **Misses:** none: it is the only internal 5xx on any site (a's one 500 is external, url 384).
4. **Words:** clear, with the right "one-off error" caveat.
5. **Severity:** Error, right.
6. **Verdict: right.**

## internal-unreachable

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. No internal timeouts or connection failures on any site. No misses.
4. **Words:** clear. 5. **Severity:** Error, right.
6. **Verdict: right, not seen on a real site.**

## soft-404

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none found.** I scanned every stored page's first title and first H1 for a much wider net than the
   rule's (404, not found, can't find, couldn't find, unavailable, no longer, not available, oops, sorry, error,
   removed, deleted, doesn't exist, gone, expired, discontinued, out of stock, sold out): no 200 page on a or j
   matches any of them; g: none of the 260 pages matches either. Untested gap (no real case): common 404 wording such as "We can't find
   the page you're looking for" or "This page isn't available" doesn't match the rule's regex (ContentRules.cs:93-95).
4. **Words:** clear and honest. No change.
5. **Severity:** Warning, fine.
6. **Verdict: right, not seen on a real site.**

## image-alt-too-long

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** The raw-HTML scan (alt attribute values, entities decoded, over 125 characters) finds none on any
   site; g has 0 of 260 pages with one.
4. **Words:** clear. 5. **Severity:** Notice, right (threshold 125 vs SF's 100, per the comparison doc).
6. **Verdict: right, not seen on a real site.**

## image-broken

1. **Fired:** a 1, j 1, g 0.
2. **Wrong flags: none found.** a url 52: `<img src="http://im.ftadviser.com/rw/FT%20Publications/.../Social%20Media.jpg">`
   (488), DNS lookup failed: the host is gone. j url 54: the sidebar promo's `<img class="lazyload"
   data-src="//jitsunami.com/cdn/shop/t/4/assets/listing-banner.jpg?v=304" alt="Collection page banner"/>` (64), 404.
   Both real.
3. **Misses:** none; the recomputation matches and no `<img>` target with a 4xx/5xx/failed response went unflagged.
4. **Words:** clear. 5. **Severity:** Error, right.
6. **Verdict: right.**

## image-missing-alt

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2. **Wrong flags:** none to check.
3. **Misses: none.** Raw scan (comments, `<script>`, `<template>` and `<noscript>` stripped): the only `<img>` without
   alt on any site is a bare `<img>` with no `src` on a url 38, which isn't an image and is rightly skipped
   (`image.Url is null`, ImageRules.cs:14). Untested gap: an `<img>` whose only source is a `data:` URI is skipped the
   same way (PageWalker.cs:578), so a base64 logo without alt would be missed.
4. **Words:** clear. 5. **Severity:** Warning (harsher than SF's Issue/Low, per the comparison doc); fine for
   accessibility.
6. **Verdict: right, not seen on a real site.**

## image-missing-dimensions

1. **Fired:** a 17, j 10, g 225 (of 260 pages).
2. **Wrong flags: none against what the check says it tests** (HTML attributes).
   - a: blog body images such as `<img src="/media/10592/...Skater_thumb_1.jpg" alt="Skater" title="Skater">`
     (37.html, 31 of its 35 images) with only `.prose img{height:auto;margin-inline:auto}` in the CSS: nothing
     reserves their space. Real.
   - j: the theme's logos `<img src="//jitsunami.com/cdn/shop/files/jitsunami1-final_120x.png?v=..." srcset="..." alt="">`
     and lazy (`data-src`) product images, no width/height. Real.
   - g: mostly product galleries, `<img class=" object-contain mx-auto" src=".../media/6789/1.png?width=896" ...
     loading="lazy"/>` (no size attributes, no size class), and gallery thumbs with `width="100"` but no height.
   - CSS-sized (the known limitation): I classified every unsized `<img>` on g's 225 flagged pages by its class
     (`dims_css.py`). 469 of 4,153 have a fixed Tailwind height (`h-5` on the TrustPilot logo and stars in every
     page's footer, `h-64 w-full object-cover` on blog cards), so CSS reserves at least their height. Only 2 pages
     are flagged solely for such images. a and j: none. The limitation is real but small on these sites.
3. **Misses:** none (recomputation and the raw regex scan agree).
4. **Words:** honest about CSS ("images sized in CSS still appear here"; plain: "If your site's design already sets
   the size another way, there's nothing to do"). No change.
5. **Severity:** Notice, right.
6. **Verdict: right.**

## image-too-large

1. **Fired:** a 24 pages (10 distinct images), j 0, g 60 pages (129 page-image pairs).
2. **Wrong flags: none.**
   - a: `keyboard.jpg` 314 KB, `<img src="/media/27839/keyboard.jpg" alt="" width="720" height="320" loading="lazy">`
     on 13 pages (the blog listing 10 among them); `google-acquisitions.jpg` 3.0 MB on 48; `marmite-clear.jpg`
     961 KB. All plain `<img>` embeds.
   - g, 10 random flagged pages across products and blog posts (86, 97, 100, 102, 121, 139, 144, 212, 229, 261): all
     plain `<img>` whose `src` is the large file, e.g. 261 `<img class=" object-contain mx-auto"
     src=".../media/6789/1.png?width=896" ...>` (a 533 KB PNG photo) and 100
     `<img src="/media/4336/poradir-lifestyles-blog-image.jpg" ... width="7440" height="5584" />` (225 KB). No
     `<picture>` with a smaller WebP source anywhere, and the srcset'ed product cards (`?width=480`) aren't large.
3. **Misses: none.** Every image response over 200 KB on g is a flagged target. a's 1.1-2.0 MB album-cover PNGs
   (79-89) are only linked (`<a href="...png">Reveal</a>` on 21), not embedded, so rightly not flagged; many a images
   of 150-199 KB (`header-image.jpg` 189 KB on 11 pages) pass under the 200 KB threshold (SF uses 100 KB).
4. **Words:** clear.
5. **Severity:** Warning, right.
6. **Verdict: right.**

## http-page

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. All three home pages are HTTPS and no internal page answers over http (the http probes all redirect). No misses.
4. **Words:** clear. 5. **Severity:** Warning, fine.
6. **Verdict: right, not seen on a real site.**

## mixed-content

1. **Fired:** a 1, j 0, g 0.
2. **Wrong flags: none.** a url 52: `<img src="http://im.ftadviser.com/...Social%20Media.jpg">` on an HTTPS page. Real
   (and the same image is `image-broken`, its host being dead).
3. **Misses: none.** Raw grep over all stored pages for `src="http://` and for `<link|iframe|script|video|audio|source|
   embed ... (src|href)="http://` (canonical/alternate excepted) and `srcset` with http: the one hit above is the only
   one.
4. **Words:** clear. 
5. **Severity:** Error. Defensible for scripts, styles and frames, which browsers block. For images, audio and video,
   which Chrome and Firefox now upgrade to HTTPS, the visible harm is only when the HTTPS copy fails, which
   `image-broken` already reports (as here: one dead image, two Errors). Keep Error; not a strong case to change.
6. **Verdict: right.**

## no-https

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. All three home pages are HTTPS. No misses.
4. **Words:** clear. 5. **Severity:** Error, right.
6. **Verdict: right, not seen on a real site.**

## security-headers-missing

1. **Fired:** a 1, j 1, g 1 (once per site, on the home).
2. **Wrong flags: none.** Checked each home's stored response headers, and the HTML for `<meta http-equiv=
   "Content-Security-Policy">` / `<meta name="referrer">` (none on any site):
   - a "Missing Strict-Transport-Security, Content-Security-Policy": headers have `x-content-type-options`,
     `referrer-policy`, `x-frame-options`, no HSTS, no CSP. Right.
   - j "Missing Referrer-Policy": Shopify sends HSTS, `x-frame-options: DENY`, `content-security-policy:
     block-all-mixed-content; frame-ancestors 'none'; upgrade-insecure-requests;`, `x-content-type-options`, no
     Referrer-Policy. Right.
   - g "Missing Content-Security-Policy, Referrer-Policy": headers have `x-frame-options`, `x-content-type-options`,
     HSTS `max-age=10886400; preload`, nothing else. Right.
3. **Misses:** none.
4. **Words:** mostly clear. Two notes: (a) the Why says Referrer-Policy "limits which URLs leak to other sites", but
   every current browser already defaults to `strict-origin-when-cross-origin`, so a missing header matters least of
   the five; suggest adding "(browsers already default to a safe policy)" after it. (b) j's CSP counts as present though
   it restricts nothing about scripts; fine for a presence check, worth one clause in the What: "a policy is counted
   whatever it allows". On Shopify the owner can't add headers at all; the plain words ("Send this to your developer
   or hosting company") are still right.
5. **Severity:** Notice, right (SF counts per URL, ours once; the comparison doc notes it).
6. **Verdict: right** (optional reword of the Why).

## html-past-crawl-limit

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. Largest decompressed HTML: a 38 KB, j 182 KB, g 333 KB. No misses.
4. **Words:** clear. 5. **Severity:** Warning, right.
6. **Verdict: right, not seen on a real site.**

## large-page

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. As above, nothing near 1 MB. No misses.
4. **Words:** clear. 5. **Severity:** Notice, right.
6. **Verdict: right, not seen on a real site.**

## slow-response

1. **Fired:** a 0, j 0, g 0. **Not seen on a real site.**
2-3. Slowest page: a 186 ms, j 378 ms (10 pages), g 462 ms; nothing near 1,000 ms. No misses.
4. **Words:** clear, with the right "recheck" caveat. 5. **Severity:** Warning, fine.
6. **Verdict: right, not seen on a real site.**

## Summary

Verdicts: **25 right** (16 of them not seen on a real site), **3 fix** (logic), **1 reword**, **1 reword + severity
change**, **0 drop**. Wrong flags found: 11, all from Shopify's hosted-login redirect (10 links-to-redirects, 1
redirect-temporary); plus 3 unproven orphans on j. Misses: none provable; one minor (the home page's own empty logo
links).

| check | fired a/j/g | wrong flags found | misses found | verdict | one-line change |
| --- | --- | --- | --- | --- | --- |
| broken-external-links | 8/0/1 | 0 (bot walls correctly excluded; a's 409 cause unverified) | 0 | reword | What: say bot-walled and robots-disallowed links aren't checked |
| broken-fragment-links | 0/0/0 | - | 0 | right, not seen | - |
| broken-internal-links | 0/1/4 | 0 | 0 | right | - |
| empty-anchor-text | 0/9/260 | 0 | minor: the home's own logo links (self-links skipped) | right | optional: test empty text before the self-link skip, LinkRules.cs:18 |
| internal-nofollow-links | 0/0/0 | - | 0 | right, not seen | - |
| links-to-redirects | 0/10/0 | 10 (j: Shopify "Sign In" -> 302 hosted login) | 0 | fix | LinkRules.cs:51: skip targets whose redirect is 302/303/307 |
| orphan-page | 0/3/1 | 0 proven wrong; j's 3 unproven | 0 | fix | CrawlAnalyzer.cs:385-413: skip when internal pages were Blocked or unreachable |
| meta-refresh | 0/0/0 | - | 0 | right, not seen | - |
| redirect-broken | 0/0/0 | - | 0 | right, not seen | - |
| redirect-chain | 0/0/0 | - | 0 | right, not seen | - |
| redirect-downgrade | 0/0/0 | - | 0 | right, not seen | - |
| redirect-loop | 0/0/0 | - | 0 | right, not seen | - |
| redirect-temporary | 0/1/0 | 1 (j: /account/login -> shopify.com) | 0 | fix | RedirectRules.cs:137: skip temporary redirects to external URLs |
| internal-blocked | 0/12/0 | 0 (but all 12 are 429s under a "firewall" title) | 0 | reword + severity | Title "Internal URL refused the crawler"; Warning -> Notice |
| internal-client-error | 0/2/8 | 0 | 0 | right | - |
| internal-server-error | 0/0/1 | 0 (persistence unverified) | 0 | right | - |
| internal-unreachable | 0/0/0 | - | 0 | right, not seen | - |
| soft-404 | 0/0/0 | - | 0 (untested gap: "can't find the page", "isn't available") | right, not seen | - |
| image-alt-too-long | 0/0/0 | - | 0 | right, not seen | - |
| image-broken | 1/1/0 | 0 | 0 | right | - |
| image-missing-alt | 0/0/0 | - | 0 (untested gap: data: URI images skipped) | right, not seen | - |
| image-missing-dimensions | 17/10/225 | 0 against its test; CSS-sized images alone on 2 g pages | 0 | right | - |
| image-too-large | 24/0/60 | 0 | 0 | right | - |
| http-page | 0/0/0 | - | 0 | right, not seen | - |
| mixed-content | 1/0/0 | 0 | 0 | right | - |
| no-https | 0/0/0 | - | 0 | right, not seen | - |
| security-headers-missing | 1/1/1 | 0 | 0 | right | optional Why: browsers already default Referrer-Policy |
| html-past-crawl-limit | 0/0/0 | - | 0 | right, not seen | - |
| large-page | 0/0/0 | - | 0 | right, not seen | - |
| slow-response | 0/0/0 | - | 0 | right, not seen | - |
