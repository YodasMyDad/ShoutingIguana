# Notes: the choices, the doubts and what didn't survive

Beside `plain-words.json`. Everything here is for the report back.

## Round 2: after the review (read this first; it overtakes parts of what follows)

The review (`../plain-words-review/review.json`, 57 entries) is applied by `apply_review.py`; the file as first
reported is `plain-words.before-review.json`.

- **Taken as written:** 41 text changes, the 4 reference good-news lines, the 9 `plainLooksFor`.
- **Written differently, 3, all `hreflang-invalid-code`** (title, sentence 1, good-news line). The review's "the wrong
  code" mends "a code that doesn't exist" but is wider than the rule the other way: a French page labelled "de" has
  the wrong code and passes, so "No language version is labelled with the wrong code" would be untrue. The rule tests
  whether a code is valid, so the texts say "an invalid code".
- **`empty-anchor-text`, the sentence its Notice lacked.** No honest "then there is nothing to do" exists: a link with
  no words is always a fault for screen-reader users. It says when it can wait: "Pages are still found either way, so
  it can wait." A fifth change to a reference; `check.py` lists it with the four good-news lines.
- **`plainLooksFor`, 12 checks.** The review's nine, and three more whose titles read as a finding before "Not
  checked": `canonical-outside-head`, `canonical-relative`, `hreflang-duplicate-code`. For the two canonical ones a
  plural title ("Main addresses written …") would do the same job with no second field; the titles were left alone
  because the review let them stand. `python check.py --skipped` prints all 87 as that list names them.
- **Limits are tokens,** `{limit:<setting>}`, 11 places in 10 checks, naming 10 of the 12 `AnalysisSettings`
  properties (not `duplicateMinWordCount` or `nearDuplicateMaxBits`: no text names them). The review counted 12
  places; its own replacement took the number out of `image-alt-too-long`'s third sentence. Fixed numbers stay:
  Google's 2 MB and 10 steps, the sitemap protocol's 50,000, the crawler's 1,000 sitemaps and 3 layers.
- **Overtaken below:** "Simple against exact" 1 (limits are tokens now) and 7 (`hreflang-invalid-code`); "Rules that
  didn't survive" 5 (tokens), 6 (reference 8's good-news line is changed) and 8 (`plainLooksFor`); and in 2, the
  Notices that say "can wait" are now six, with `empty-anchor-text`.
- **For the engine:** a token is filled with the number and its unit from the settings the analysis ran with. The
  texts are written around that ("a title of more than {limit:titleMaxLength}"). Sentence 1 of three checks is over
  120 characters as written and inside it once filled; a token's fill is always shorter than the token.
- **`reference-changes.json`** is built by `reference_changes.py` from the JSON and the document: 9 differences in 8
  of the 10 references (4 good-news lines, 1 added sentence, 2 tokens, 2 `plainLooksFor`).

## Field shapes chosen where the design is silent

- Category ids are the API's (`responseCodes`, `metaDescriptions`, `structuredData`, `urls`, `html`…), not the enum's
  names.
- `plainWhat` and `plainFix` are arrays of strings, one sentence or step an item. (`words.mjs` put two sentences in
  one item in places.)
- `plainOneFix` is on 26 checks: only where the check's grouping can mark a likely template (RelatedUrl, PageDetail).
  Nine of the ten references have none in the document, so none was added to them.
- `site-hidden-from-search` sits after `robots-txt-missing`, at the end of Indexability.
- Straight quotes, as the document writes them.

## Simple against exact: what was chosen

1. **Limits are the defaults.** Ten texts carry a number an Advanced user can change (title 60 and 30, description
   155 and 70, 200 words, alt 125, image 200 KB, 1 second, 1 MB of code, address 115). The references do the same.
2. Titles and descriptions count wide characters and emoji as two: left out.
3. `redirect-temporary`: names "302"; 303 and 307 are flagged too.
4. `robots-txt-missing`: the title says the site "has no rules file". The rule also fires when the file is refused
   (401, 403), loops, or is missing on another part of the site only. Sentence 1 says "not found, or are refused",
   and step 1 opens "the address the list names".
5. `blocked-by-robots`: sentence 1 says the rules file puts the addresses off limits. It also fires when the file
   couldn't be read at all; step 4 says so.
6. `sitemap-non-indexable`: five reasons become "forwards elsewhere, fails, or is kept out of search some other way".
7. `hreflang-invalid-code`: "a code that doesn't exist". `es-419` exists; Google doesn't support it.
8. `hreflang-to-non-indexable` and `canonical-to-non-indexable`: "can't itself appear in search results" covers
   every reason; the second sentence lists them.
9. `duplicate-content`: "word for word" (case and punctuation are ignored), and the good-news line says "No two
   pages", though pages under 50 words aren't compared.
10. `near-duplicate-content`: "only a few words differ" for about 96% alike.
11. Checks that only look at pages that can appear in search say "pages", as the references do ("Every page has a
    title").
12. `image-missing-alt`: "no description" is no alt at all; an image marked as decoration passes.
13. `charset-missing`: "don't tell browsers" is untrue of a page that declares it with a byte-order mark, which the
    rule doesn't look for.
14. Structured data is "details for search engines" and its result "extras"; the terms ("structured data",
    "JSON-LD", "rich result") come once each. "Such as a product's price" stands for every type.
15. `internal-unreachable`: "the wait ran out", with no number: the timeout is a setting.
16. `robots-txt-unreachable-other-origin`: "until the file answers again" for Google's 12 hours and 30 days.
17. `soft-404`: "Pages that read like an error page", not "error pages": the rule goes by wording.
18. `meta-refresh`: that Google treats an instant one as permanent and a delayed one as temporary is left out.
19. `canonical-relative`: three forms become "a short form, such as /page".
20. `snippet-blocked`: "show no text"; the video preview is left out, and "AI Overviews or AI Mode" is "AI answers".
21. `mixed-content`: "such as images or scripts" for six kinds of file.
22. `redirect-chain`: "Google gives up after 10 steps"; where browsers stop is left out.
23. `noindex-page`, `nofollow-page`: "an instruction", whether it is in the page or sent with it.
24. Titles changed from the drafts because the draft said more than the rule finds: `internal-blocked` (files too),
    `redirect-broken` (may forward nowhere), `orphan-page` (sitemap pages only), `image-missing-dimensions` (may not
    jump), `soft-404`, `hreflang-duplicate-code` (one page, two addresses), `hreflang-to-non-indexable` (forwarding
    too), `sitemap-url-other-host` (its own "www" twin too), `sitemap-error` (one of several), `snippet-blocked`
    ("description" is the search description's word), `broken-external-links` ("no longer" assumes it once worked),
    and the structured data pair ("rich-result details").
25. Good-news lines changed from the drafts for the same reason, or to stay true with nothing to look at: "Every
    page and file answers", "Every image loads", "Every link to another website works", "The site has a rules
    file", "Every main address works", "Every page has its own search description", "Every page is set up for
    phones", "The site is secure (HTTPS)", "The recommended security settings are in place", "The site has a
    sitemap".

## Technical texts I think are wrong or misleading (not changed)

1. `robots-txt-missing`, title "No robots.txt": the rule fires on 401 and 403 too, where a file may well exist
   behind the firewall, and on too many redirects. The title says more than the rule knows.
2. `charset-missing`: its own `why` says a byte-order mark declares the encoding and the check doesn't look for one,
   so such a page is reported as "Missing character encoding". A false positive the text admits.
3. `near-duplicate-content`, description: "at the default of 24 differing bits … (the default, which can be changed
   …)" says "the default" twice in one sentence.
4. `image-missing-alt`, title "Images without alt text": an empty alt has no text and passes. The description is
   right; the title is wider than the rule.
5. `orphan-page`, title: only sitemap pages are tested, so a page nothing links to that is in no sitemap is never
   found. The description is right; the title is wider.
6. `redirect-broken`, title "Redirect leads to a broken URL": it also fires for a redirect with no usable Location,
   which leads nowhere. `blocked-by-robots` likewise also covers "robots.txt couldn't be read".
7. `hreflang-missing-self`: the rule compares with the page's URL as crawled; the fix says "written exactly as its
   canonical URL". The same on most pages, not the same thing.
8. `security-headers-missing` has `Subject = Page` and is reported once, so Simple will print "1 of 265 pages" under
   a text about the whole site. It isn't in the design's `siteWide` list. My text says only the home page is checked.
9. In the design: `words.mjs` gives `earlier-version` the sentence for `newer-than-analysis` ("This check is newer
   than your last site check"), and has 5 of the 15 reason codes. The document's sentence for `no-sitemap` ("Your
   site has no sitemap") is untrue when a sitemap exists and can't be read, which the same reason covers.

## Rules that didn't survive 87 checks, and what I did

1. **"Each step starts with a verb."** Eleven steps in the ten references start "If …,". Allowed: a verb, or "If".
2. **"A Notice always says when it is fine, and that there is then nothing to do."** Reference 4
   (`empty-anchor-text`) never says it, and reference 10 says it only as a step. Five Notices have no honest
   "nothing to do": `h1-missing`, `h1-multiple`, `security-headers-missing`, `lang-missing`, `charset-missing`. They
   say when it can wait.
3. **"Where the fix is a developer's, the first step is something the reader can check themselves."** Some faults
   have nothing a non-expert can see (`hreflang-missing-self`, `html-past-crawl-limit`, `canonical-relative`, and the
   design's own draft of `security-headers-missing`). Their first step is "Send the list to your developer: …".
4. **"Never bare."** The references use a term again once it has been given in quotes ("canonical link", "redirect
   settings", "/robots.txt"). Applied as: the first use is in quotes, later ones may be bare.
5. **"Numbers carry their unit and their limit."** They do, but the limits are settings and the texts are fixed:
   change one in Advanced and ten plain texts are wrong. Needs a placeholder the engine fills, or a line in Simple
   when a limit isn't the default.
6. **`plainPassed`, "Every page has its own title"** (reference 8) reads wrong beside a failing "Pages with no
   title". Kept word for word; the other duplicates say "No two pages share …".
7. **`plainOneFix`, "That's almost certainly …"** is too strong where the engine's hint is weak (`h1-missing`,
   `snippet-blocked`: the API says the hint "says no more than that most of its pages share the fault"). Written as
   a condition there.
8. **Nothing in the template says what a check looks for**, which the design's skipped and passed pages show. A
   title mostly serves ("Looks for: pages with no title"). Twelve titles are sentences and don't
   ("Your site isn't secure (no HTTPS)", "No sitemap found", "Sitemap lists addresses …").
9. **Reading age.** By a rough Flesch-Kincaid grade the references run 5.5 to 7.8 (mean 6.8), mine 4.7 to 8.9 (mean
   7.2). Hreflang, structured data and security stay near 8.5: their plain names are long words ("language
   versions", "description", "developer").
10. **The category "Rich results"** (draft) is "Extras in search results": the draft's word needs looking up.

## Least sure of

1. `site-hidden-from-search`: written from one row of a table, for a check that doesn't exist yet. "It's often one
   setting left on from when the site was being built" and the option names in step 1 are my assumptions.
2. `structured-data-invalid`: "Some pages carry details for search engines, such as a product's price, in code that
   has a mistake in it." The plain name for structured data is mine.
3. `charset-missing`: "These pages don't tell browsers how their letters and symbols were saved."
4. `canonical-outside-head` and `hreflang-outside-head`, step 1: "Note any plugin, tracking code or banner added to
   the site lately: one of them is the likely cause." A guess at the cause, from the catalog's "a script or plugin".
5. `sitemap-url-other-host`: "Your sitemap lists addresses on another website, or on another version of your own,
   such as with or without "www"."
