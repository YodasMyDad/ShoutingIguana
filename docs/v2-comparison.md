# v2 comparison: Screaming Frog SEO Spider and Sitebulb

Research only, 2026-11-11. Everything about their products was read on their own sites, guides and screenshots
([Sources](#sources)). Neither tool was run. Versions read: Screaming Frog SEO Spider 25.1 (4 November 2026) and
Sitebulb 10 (10.0 is dated 9 September 2026). Our side is branch `ui/step-3`: the engine, the 86 checks and the
gallery screenshots of 10 November.

Verdicts: **step 3** (the UI), **the check audit** (step 3b), **step 4** (rendering, include and exclude, logins,
list mode, extraction), **step 5** (integrations), **later**, **not doing**.

## The answer

- **Behind.** No JavaScript rendering: 113 of their 348 issues need a browser. Fewer checks: we cover 71 of the 348,
  and 7 more in part. No list mode, include and exclude, extraction, comparison, scheduling, visualisations or
  integrations. (Written on 2026-11-02. Since then step 3 has built export from every grid, the workbook and the
  report, and a URL grid with column presets and a chooser, header sort, filters and saved views: see
  [Since this was written](#since-this-was-written).)
- **Level.** The plain HTTP crawl: robots.txt, sitemaps, redirects, canonicals, hreflang, duplicates, orphans,
  resume and speed. Storage on disk. A "what" and a "how to fix" for every issue. Light and dark. A command line.
- **Ahead.** The first minute. The answer screen with "Fix first". Issues grouped by cause. Skipped checks named
  with their reason. Live crawl feedback. The keyboard. Forced colours. 17 checks they have no counterpart for.
- **Do 1: export and the URL grid, in step 3.** Columns, sort, a filter builder and compact rows are where
  Screaming Frog is plainly better for an expert.
- **Do 2: about 50 checks in the check audit.** 100 of theirs need only the HTML and headers we already fetch: 50
  are worth adding, 37 can wait, 13 aren't worth having ([Checks](#2-checks)).
- **Do 3: keep JavaScript rendering first in step 4.** It is the largest gap, and their accessibility and
  Lighthouse checks stand on it.

For Simple: take Sitebulb's two-sentence hints and its count with a percentage. Don't take its score.

### Since this was written

Step 3 (to round 5, 2026-11-14) has built, of what this page found missing or took:

- **Export** on every grid (CSV and Excel of the view, its filters and columns), the workbook of everything, the
  report as HTML and PDF, and `si export` on the command line.
- **The URL grid**: column presets and a chooser, header sort (up to three keys), filters as chips and a filter
  builder, saved views, 28 px rows in the compact density.
- **"% of Total"** beside every check's count in Issues and Overview's Fix first, of the pages analysed or the URLs
  crawled, said whole in the row's name; a fault of the whole site is "Whole site" (round 5).
- **A row of totals** before anything else on Overview: Crawled, Internal, External, Pages and Found, each opening its
  URLs (round 5).
- **The library on Home grouped by site** in Advanced as in Simple, with the running crawls as cards and every action
  in a row's menu (round 5).
- **A report for someone else**: Simple's report, in both modes (round 4).

Still later, and in [the IA's room for what comes later](v2-ui-ia.md#room-for-what-comes-later): the SERP snippet,
the duplicates diff and Insight.

## 1. Features

### They have it, we don't

| Area | Screaming Frog | Verdict |
| --- | --- | --- |
| JavaScript rendering | "Text Only" is the default. "JavaScript" mode renders in headless Chrome. With it: rendered screenshots, stored rendered HTML, JavaScript errors, 16 JavaScript issues | **step 4** |
| List mode | Checks a list of URLs: pasted, or from .txt, .xls, .xlsx, .csv or .xml, so a sitemap can be crawled on its own. Since v22 also a Google Sheet or several sitemaps | **step 4**. Ours reads sitemaps only as part of a crawl |
| SERP mode | Upload titles and descriptions to measure pixel widths. No crawl | **not doing**: a preview tool, not an audit. Pixel widths are in the check audit |
| Compare mode | Two stored crawls: URLs "Added", "New", "Removed" and "Missing" for every tab and filter. Change detection for titles, descriptions, h1s, word count, depth, links, structured data. URL mapping for staging against live. Since v24, scheduled crawls compare themselves with the last one. Needs a licence and database storage | **later**: already planned as Overview's "Compare with…" |
| APIs mode | Pulls API data for uploaded URLs without crawling | **not doing** |
| robots.txt tester | "Custom robots.txt": download, edit and test a file, and see what it blocks. Also a setting to ignore robots.txt "but report status" | **later**. Our robots.txt screen (built, not merged) shows each file, the blocked URLs and the rule that matched |
| URL rewriting | Remove parameters, regex replace | **step 4** |
| Include and exclude | Include by partial regex. Exclude by URL, or since v25 by where a link sits on the page ("Link Paths") | **step 4** |
| Fine crawl limits | Per depth, folder depth, query strings, per subdomain, redirects followed, URL length, links per URL, page size, by path | **step 4**, with include and exclude. We have total URLs, depth and HTML size |
| Authentication | Basic and digest. Web form login in a built-in Chromium ("requires a licence"). Authentication profiles | **step 4**. Today: an `Authorization` header |
| Custom extraction | Up to 100 extractors: XPath, CSS Path or regex | **step 4** |
| Custom search | Up to 100 filters: text or regex, "contain" or "does not contain" | **step 4** |
| Custom JavaScript | Snippets run on each page during a crawl | **later**, after rendering |
| Spelling and grammar | "over 25 different languages". Off by default | **later** |
| Link Score | 0 to 100, "similar to Google's own PageRank". Needs "crawl analysis" | **later** |
| XML sitemap generation | XML and image sitemaps, split into files past 49,999 URLs | **later**: the old app's generator is on the backlog |
| Visualisations | Force-directed crawl diagram (2D and 3D), crawl tree graph, directory tree diagrams, word clouds, content cluster diagram | **later**. 3D: **not doing** |
| Site Structure | A directory tree with URLs, indexable and non-indexable counts for each path | **later**: cheap from what we store, and useful in Simple |
| Scheduling | In the app: one-off or at intervals, with exports and email. Since v24 the email carries the issues table and attachments | **later**. `si`'s exit codes already suit an OS scheduler |
| Exports | "Export" on every tab and filter: CSV, Excel, Google Sheets. Bulk Export (all inlinks, by status). Multi Export (v22). Filename patterns (v25) | **step 3**, round 3 (E5) |
| Reports menu | Crawl Overview, redirect chains, canonicals, hreflang, insecure content, orphan pages and more, as files | **step 3**: the Simple report. The rest are exports |
| Google integrations | GA4. Search Console: Search Analytics and URL Inspection. PageSpeed Insights: Lighthouse and CrUX | **step 5**. PageSpeed Insights isn't on step 5's list: add it |
| Link data | Majestic, Ahrefs, Moz | **not doing**: paid third-party accounts, and not a crawler's job |
| AI | Up to 100 prompts against OpenAI, Gemini, Anthropic or Ollama. Embeddings for "Semantically Similar", "Low Relevance Content" and semantic search. An MCP server (v24), and config through it (v25) | **not doing** now: the app flags and explains, and doesn't draft fixes. An MCP server over our API: **later** |
| Accessibility | The axe rule set: 92 issues. Needs JavaScript rendering | **later**, after step 4 |
| Mobile and PageSpeed | From Lighthouse: 19 PageSpeed issues and 4 of the 6 Mobile ones | **step 5**, with PageSpeed Insights |
| AMP | Crawls and validates with "the official integrated AMP Validator": 16 issues | **not doing**: 16 checks for a format few sites use. The owner's call |
| Segments | Segment a crawl by any of its data. Needs database storage | **later**. Saved views (step 3) cover part |
| Data Studio report | An automated crawl report (Looker Studio, named "Data Studio" again since v24) | **not doing**: our report covers it |
| Custom dashboards | v25: a Dashboard tab built from widgets, several dashboards, export | **not doing**: one designed Overview is the point |
| Configurable issues | v25: disable an issue, or change its type and priority, for a crawl | **later**. We hide a check. Editable thresholds are step 3 (Settings > Analysis) |
| Log files | A separate product, Log File Analyser 7.2, priced on its own (€129 a year when fetched). Free to 1,000 log lines | **not doing** |
| Interface languages | English, Spanish, German, French, Italian, Dutch, Turkish | **later** |

### Both have it

| Area | Screaming Frog | Us | Verdict |
| --- | --- | --- | --- |
| Spider crawl | A start URL. One subdomain or all. A subfolder | A start URL. Subdomains on or off. Stay in the start folder | Level |
| Speed | 5 threads by default, with an optional URLs-per-second cap. Since v25 it slows for 429, 503 and dropped connections, and pauses if the host stops answering | Polite, Standard and Fast presets over a rate and a concurrency. Backs off on 429 and Retry-After, obeys Crawl-delay, stops itself when the site stops answering | Level |
| robots.txt | Respected by default. Blocked URLs listed with the matching line | The same, and 3 checks on the file itself | Level |
| Sitemaps | Crawls linked or given sitemaps. 6 issues | Reads them without being asked. 5 checks, covering 3 of their 6 | **the check audit** |
| Canonicals, hreflang | 10 and 14 issues | 10 and 8 checks, covering 6 and 9 of theirs | **the check audit** |
| Structured data | JSON-LD, Microdata and RDFa. Schema.org validation. Google rich result validation: errors for required properties, warnings for recommended. Off by default | JSON-LD only: invalid JSON, and required properties. Always on | **the check audit** |
| Duplicates | Exact by default. Near duplicates must be switched on: 90% by minhash, after "crawl analysis". A view that highlights the differences. A content area setting | Exact and near (256-bit SimHash), always on. No diff, no content area | Level on detection. The diff: **later** |
| Orphan pages | From sitemaps, GA and Search Console. Needs configuration and "crawl analysis" | From sitemaps, without being asked. Skipped with a reason when the crawl can't support it | Level. GA and Search Console sources: **step 5** |
| Redirects | Chains, loops, meta and HTTP refresh. Chain reports. List mode with "always follow redirects" for migrations | Chain, loop, broken, temporary, HTTPS to HTTP. Hop by hop in the inspector | Level for a crawl. A migration needs list mode: **step 4** |
| Crawl depth | A column, a chart and "Pages With High Crawl Depth" | A column and a chart. No check | **the check audit** |
| Pagination | rel="next" and rel="prev" stored, and crawled on request. 7 issues | The links are followed, as a crawl setting. No checks | **later** |
| Storage and scale | Database storage by default. Their rough guide: an SSD and 4 GB allocated for about 2 million URLs. Memory mode: 10k to 100k URLs at 2 GB. Java, with memory to allocate by hand | One SQLite file per crawl. A 100k-page synthetic site at about 2,900 pages a second in 168 to 195 MB, flat. The UI isn't measured at 1M | Level in design. Ours is unproven on large real sites |
| Saved crawls | Saved as they run in database mode. "File > Crawls", project folders, retention (v23). The free version can't save | Every crawl is a file. The library on Home, grouped by site in both modes (Advanced's since round 5; it was a flat list before) | Level |
| Command line | Launch, configure, crawl and export almost anything, headless | `si crawl`, `analyze`, `issues`, `stats`, and `si export` (a grid as CSV or Excel, the workbook, the report) | Level for crawling. Exports: built in step 3 |
| User agent, headers | Presets and a custom string. Any request header | The same | Level |
| Theme | Light, dark, an accent colour | Light, dark, follows the OS, forced colours | Level |
| Platforms | Windows, macOS, Ubuntu, Fedora | The shell has run on Windows only. The engine's tests run on three systems | **later** |
| Price | Free to 500 URLs with 7 features, no saving and no configuration. A licence is per user, for a year: "£199 per year" in the page's text (the price box showed €245 when fetched) | Not set in the docs I read | The owner's call |

### We have it, they don't

As far as their guide and issues library show:

- **Nothing to switch on.** Near duplicates, structured data and orphans run on every crawl. Theirs need a setting,
  and some a "crawl analysis" pass after the crawl.
- **Skipped checks are named, with why.** Their Issues tab can list "All Issues", including ones not triggered, and
  filters that still need "crawl analysis" say so. Nothing I read shows a check that was switched off as not run.
- **Blocked by a firewall** is a check and a live alert, and names the service.
- **The site's other versions** (www or not, http or https) are probed without being asked.
- **robots.txt health**: unreachable, missing, unreachable on another origin.
- **A check's rows grouped by cause**, so a template fault is one row with a count.
- **An answer screen**: one sentence, then "Fix first".
- **The start**: paste a URL and press Enter. A library with each crawl's status and severity counts.
- **The tray**: a crawl carries on with the window closed, and a toast offers Resume or View issues.
- **A command palette**, a shortcut sheet, and the keyboard through every grid.

## 2. Checks

Their issues library lists **348 issues** in 25 groups, each with a type (Issue, Warning, Opportunity) and a priority
(High, Medium, Low). 92 are accessibility rules. I matched each to our 86 by name and definition.

| | Count |
| --- | --- |
| We cover | 71 |
| We cover in part | 7 |
| We lack | 270 |
| of those: need only the HTML and headers we already fetch | 100 |
| of those: need a browser | 113 |
| of those: need Lighthouse, GA or Search Console | 37 |
| of those: AMP | 16 |
| of those: need an AI key or a dictionary | 4 |
| Ours with no counterpart in their library | 17 of 86 |

**In part (7).** "Internal Redirection (3XX)": we flag temporary redirects and links to redirects, not every
redirect. Page Titles "Over 561 Pixels" and "Below 200 Pixels", Meta Description "Over 985 Pixels" and "Below 400
Pixels": we estimate width from characters. Canonicals "Multiple": we flag only canonicals that conflict. Social
"Incomplete OG Tags": `open-graph-missing` covers it, with our own list of three tags.

### Theirs we lack: from the HTML and headers we already fetch (100)

| Their group | Theirs we lack, by their names | Our nearest | Verdict |
| --- | --- | --- | --- |
| Response Codes | Internal Redirection (HTTP Refresh) | `meta-refresh` | **the check audit** |
| Security | Form URL Insecure; Form On HTTP URL; Bad Content Type; Protocol-Relative Resource Links; Unsafe Cross Origin Links | `mixed-content`, `http-page` | **the check audit**: the first three. Protocol-relative: **later**. Unsafe Cross Origin Links: **not doing**, as their own text says it matters only to legacy browsers |
| URL | Multiple Slashes; Contains A Space; Parameters; GA Tracking Parameters; Non ASCII Characters; Repetitive Path; Internal Search | `url-uppercase`, `url-underscores`, `url-too-long` | **the check audit**: the first four. The rest: **later** |
| Page Titles | Outside \<head>; Same as H1 | `title-missing`. Backlog: an H1 the same as the title | **the check audit** |
| Meta Description | Outside \<head> | `description-missing` | **the check audit** |
| H1 | Non-sequential; Alt Text in h1; Over 70 Characters | `h1-missing`, `h1-multiple`. Backlog: skipped heading levels | **the check audit**: Non-sequential. The rest: **later** |
| H2 | Missing; Multiple; Non-sequential; Duplicate; Over 70 Characters | None | **not doing**: H2 counts say little, and one heading-order check covers the fault |
| Content | Lorem Ipsum Placeholder; Readability Difficult; Readability Very Difficult | `low-word-count` | **the check audit**: Lorem Ipsum. Readability: **later** |
| Images | Missing Alt Text; Missing Image Title | `image-missing-alt` is their "Missing Alt Attribute" | **not doing**: an empty alt is the right mark for a decorative image, and image titles went with the old app's heuristics |
| Canonicals | Invalid Attribute In Annotation; Contains Fragment URL; Unlinked | `canonical-invalid` | **the check audit** |
| Pagination | Pagination URL Not In Anchor Tag; Non-200 Pagination URLs; Unlinked Pagination URLs; Multiple Pagination URLs; Pagination Loop; Sequence Error; Non-Indexable | None. Backlog: paginated pages canonicalised to page 1 | **later** |
| Directives | Outside \<head>; NoImageIndex; Unavailable_After; NoODP; NoYDIR; NoTranslate | `noindex-page`, `snippet-blocked`. Backlog: noimageindex, unavailable_after | **the check audit**: the first three. The last three: **not doing**, as they change nothing a site owner would fix |
| Hreflang | Inconsistent Language & Region Confirmation Links; Self Reference Only; Not Using Canonical; Unlinked Hreflang URLs; Mismatch with HTML Lang Attribute | `hreflang-missing-return`, `hreflang-to-non-indexable` | **the check audit** |
| Links | Pages Without Internal Outlinks; Non-Indexable Page Inlinks Only; Internal Nofollow Inlinks Only; Pages With High Crawl Depth; Non-Descriptive Anchor Text In Internal Outlinks; Pages With Uncrawlable Internal Outlinks; Pages With Empty Anchor Tags; Outlinks To Localhost; Pages With High External Outlinks; Pages With High Internal Outlinks; Follow & Nofollow Internal Inlinks To Page | `internal-nofollow-links`, `empty-anchor-text`, `orphan-page`. Backlog: no outlinks, generic anchor text | **the check audit**: the first eight, the largest group worth adding. The last three: **later** |
| Structured Data | Validation Errors; Validation Warnings; Rich Result Validation Warnings; Missing | `structured-data-missing-required` | **the check audit**: the first three, with Microdata. Missing: **not doing**, as most pages need none |
| Social | Missing X Cards; Incomplete X Cards; Non-200 OG Image URL; Relative OG Image URL; Non-Canonical OG URL; Invalid OG Image; OG Image Over 5MB; OG Tags Outside \<head>; X Cards Using Property; Non-200 X Card Image URL; Invalid X Card Image; Relative X Card Image URL; X Card Image Over 5MB; X Cards Outside \<head>; Duplicate OG Title; Duplicate OG Description; Multiple OG Tags; OG Image Incorrect Size or Ratio; Duplicate X Card Title; Duplicate X Card Description; Multiple X Cards; X Card Image Incorrect Size or Ratio | `open-graph-missing`. Backlog: Twitter Card | **the check audit**: the first five. The rest: **later**. The group is new in v25 |
| Sitemaps | XML Sitemap With Over 50k URLs; XML Sitemap Over 50mb; URLs In Multiple Sitemaps | `sitemap-error`. Backlog: sitemaps at the 50,000 limit | **the check audit** |
| Validation | Missing \<head> Tag; Multiple \<head> Tags; Missing \<body> Tag; Multiple \<body> Tags; Invalid HTML Elements In \<head>; \<body> Element Preceding \<html>; \<head> Not First In \<html> Element; Resource Over 2MB; High Carbon Rating | `canonical-outside-head`, `hreflang-outside-head`, `html-past-crawl-limit` | **the check audit**: the first seven, one look at the document's structure. It is the cause behind every "Outside \<head>". Resource Over 2MB: **later**. Carbon: **not doing** |
| Mobile | Mobile Alternate Link | None | **later** |

That is 50 for the check audit, 37 for later and 13 not doing. In the audit's order: Links (8), the document's
structure and the "Outside \<head>" family (10), Hreflang (5), Canonicals (3), Sitemaps (3), then the rest.

### Theirs we lack: needing something we don't have (170)

| Needs | Their group | Count | Their names | Verdict |
| --- | --- | --- | --- | --- |
| A browser | JavaScript | 16 | Noindex Only in Original HTML; Nofollow Only in Original HTML; Canonical Mismatch; Uses Old AJAX Crawling Scheme URLs; Uses Old AJAX Crawling Scheme Meta Fragment Tag; Pages with Blocked Resources; Contains JavaScript Links; Contains JavaScript Content; Page Title Only in Rendered HTML; Page Title Updated by JavaScript; Meta Description Only in Rendered HTML; Meta Description Updated by JavaScript; H1 Only in Rendered HTML; H1 Updated by JavaScript; Canonical Only in Rendered HTML; Pages With JavaScript Errors | **step 4** |
| A browser | Response Codes | 3 | Internal Blocked Resource; External Blocked Resource; Internal Redirection (JavaScript) | **step 4** |
| A browser | Images | 2 | Background Images; Incorrectly Sized Images | **step 4** |
| A browser | Accessibility | 92 | The axe rules, WCAG 2.0 A to 2.2 AA and best practice. Four overlap checks of ours: lang, alt, title and h1 | **later**, after step 4 |
| Lighthouse | PageSpeed | 19 | Document Request Latency; LCP Request Discovery; Render Blocking Requests; Network Dependency Tree; Use Efficient Cache Lifetimes; Layout Shift Culprits; Improve Image Delivery; Forced Reflow; Legacy JavaScript; Duplicated JavaScript; Avoid Enormous Network Payloads; Minify CSS; Minify JavaScript; Reduce Unused CSS; Reduce Unused JavaScript; Reduce JavaScript Execution Time; Minimize Main-Thread Work; Optimize DOM Size; Font Display | **step 5** |
| Lighthouse | Mobile | 4 | Content Not Sized Correctly; Illegible Font Size; Contains Unsupported Plugins; Target Size | **step 5** |
| Search Console | Search Console | 10 | Page is Not Mobile Friendly; Orphan URLs; URL is on Google, But Has Issues; AMP URL is Invalid; Rich Result is Invalid; Indexable URL Not Indexed; User-Declared Canonical Not Selected; No Search Analytics Data; Non-Indexable with Search Analytics Data; URL Is Not on Google | **step 5** |
| GA4 | Analytics | 4 | Orphan URLs; Bounce Rate Above 70%; No GA Data; Non-Indexable with GA Data | **step 5** |
| An AI key | Content | 2 | Semantically Similar; Low Relevance Content | **not doing** |
| A dictionary | Content | 2 | Spelling Errors; Grammar Errors | **later** |
| AMP | AMP | 16 | Non-200 Response; Missing Non-AMP Return Link; Missing Canonical to Non-AMP; Non-Indexable Canonical; Missing \<html amp> Tag; Missing/Invalid Doctype HTML Tag; Missing Head Tag; Missing Body Tag; Missing Canonical; Missing/Invalid Meta Charset Tag; Missing/Invalid Meta Viewport Tag; Missing/Invalid AMP Script; Missing/Invalid AMP Boilerplate; Contains Disallowed HTML; Other Validation Errors; Indexable | **not doing** |

### Ours they lack (17)

| Our check | The nearest thing of theirs |
| --- | --- |
| `internal-blocked` | None |
| `redirect-broken` | A report, "Redirects to Error" (v23). Not an issue |
| `redirect-downgrade` | None |
| `redirect-temporary` | Inside "Internal Redirection (3XX)" |
| `robots-txt-unreachable`, `robots-txt-unreachable-other-origin`, `robots-txt-missing` | None |
| `canonical-invalid` | None. "Contains Fragment URL" and "Invalid Attribute In Annotation" are other faults |
| `noindex-with-canonical` | None |
| `sitemap-error`, `sitemap-missing`, `sitemap-url-other-host` | None |
| `duplicate-site-version` | None |
| `slow-response` | A Response Times tab, with no issue. Their speed issues need PageSpeed Insights |
| `large-page` | Only "HTML Document Over 2MB", which is our `html-past-crawl-limit` |
| `charset-missing` | Only inside AMP |
| `lang-missing` | An axe rule, which needs rendering |

### For the check audit

- **Thresholds.** The same as theirs for titles (60 and 30), descriptions (155 and 70), URL length (115) and thin
  content (200 words). Different for image size (theirs 100 kb, ours 200 KB) and alt text (100 characters, ours 125).
- **Severity.** Theirs is harsher on several: "Canonicalised", "Noindex" and "Internal Blocked by Robots.txt" are
  Warning and High (ours are Notices), "Exact Duplicates" is Issue and High (ours a Warning), H1 "Missing" is Issue
  and Medium (ours a Notice). Ours is harsher on images without alt (their Issue and Low, our Warning). Each is a
  question for the audit, not a fault.
- **Scope.** `security-headers-missing` reads the home page's response. Their five header issues are counted for
  every URL.
- **How I counted.** One of theirs is "covered" when one of ours flags the same fault, whatever the threshold. The
  mapping is mine, from their names and the definitions in their tabs guide. Neither tool was run on a shared site.

## 3. UI

Our screenshots are 1283 by 752 logical pixels (150% scale). Theirs: three of each product, from their own pages.
Screaming Frog's three-pane screenshot dates from 2020.

| | Screaming Frog | Sitebulb, where it differs | Us | Verdict |
| --- | --- | --- | --- | --- |
| How much one screen holds | Three panes at once: the grid, tabs about the selected URL below it, and Overview or Issues on the right. About 22 rows by 7 columns in the grid of a small window, with both other panes open. The guide lists 57 columns for the Internal tab, and 35 top tabs | Cards, not rows: about three hints fill a screen (their screenshot) | The URL grid: 16 rows by 4 columns when this was written; now column presets and a chooser (step 3). The inspector takes the right third or the bottom half. Rows can be 28, 32 or 40 px | Theirs holds more. Built in step 3: column presets and the chooser, more columns on show |
| From "something is wrong" to "these URLs" | Click an issue on the right and the grid shows its URLs. The Inlinks tab below shows what links to one (inferred from the Issues screenshot) | Each hint has "View URLs" and "Export URLs" | "Fix first" or Issues, then the check's groups by cause, then "Show all N pages in URLs", then the inspector. Every number is a link | Level. Theirs is one click shorter, ours explains the cause. Keep ours |
| What a first-time user sees | A menu bar of 12 menus, a row of tabs, an empty grid, a URL box and Start. Since v25 the first tab is a Dashboard | Audit setup, then an Audit Overview with scores | "Crawl a site. Paste a URL and press Enter", and the library | Ahead |
| How an issue is explained | An "Issue Details" pane: "Description" and "How To Fix", a paragraph each. A page per issue on their site | A card: two sentences (what, why), URLs and percentage, and "Learn more…" to a page of 350 to 450 words, some with examples and code | "What's wrong", "Why it matters", "How to fix it", a Learn more link, and the evidence grouped | Level with Sitebulb, ahead of Screaming Frog in the app. Take Sitebulb's "Examples that trigger this Hint": **the check audit** |
| How issues are prioritised | Type by priority, both sortable. v25 lets a user disable or re-rank an issue | Importance (Critical, High, Medium, Low, Insight) by type (Issue, Potential Issue, Opportunity), and a score | Three severities. "Fix first" sorts by severity, then pages affected | Level. Take "Insight": a class for facts that aren't faults ([For Simple mode](#for-simple-mode)) |
| Filters and search | A filter per tab. A search box with regex, preset filters and combined rules. v25 adds search to Overview and Issues | A URL Explorer with chosen columns and an advanced filter (from a search summary, not read) | Chips that links apply, and one search over URLs and titles. A filter builder, header sort and saved views since step 3 | Level since step 3 |
| One URL in detail | About 16 lower tabs: URL Details, Inlinks, Outlinks, Image Details, Resources, SERP Snippet, Rendered Page, View Source, HTTP Headers, Cookies, Duplicate Details, Structured Data Details, Lighthouse Details, N-grams and more | A panel that slides in from the right (search summary, not read) | Nine tabs: Summary with indexability as four tests, Issues, Inlinks, Outlinks, Redirects hop by hop, Headers, Structured data, Hreflang, Source. Links walk to the next URL, with back and forward | Ours reads better, theirs holds more. SERP snippet and the duplicate diff: **later**. Rendered page: **step 4** |
| Live crawl feedback | A progress bar with a percentage, "Completed 729 of 12,245 (5.95%) 11,516 Remaining", average and current URLs a second. Tabs fill as it runs | Not read | Counts, URLs a second, found against crawled, status codes, alerts with what to do, the status in the top bar of every screen, the tray | Ahead |
| Exports | On every tab and filter, plus Bulk Export, Multi Export and reports | Per hint, bulk, Google Sheets, PDF | Copy URLs, and since step 3 Export (Ctrl+E) on every grid as CSV or Excel, the workbook of everything, the report, and `si export` | Level since step 3, but for Google Sheets and bulk inlinks |
| A report for someone else | Dashboards export as an image (v25). A Data Studio report. No PDF report in what I read | PDF for one report or the whole audit. The user picks the reports, charts and tables | None when this was written; since round 4, the report (HTML, printed to PDF), in both modes | Level with Screaming Frog; behind Sitebulb's choice of sections |
| What wasn't checked | "All Issues" lists issues not triggered. Filters that need "crawl analysis" say so. Several checks are off by default | A section not selected at setup scores 100 | Skipped checks are always listed, each with its reason | Ahead. Simple must keep it |
| Many crawls | "File > Crawls": open, rename, project folders, duplicate, delete, retention | Not read | The library on Home, grouped by site (in Advanced since round 5), with status, counts and every action of a crawl in its menu: open, continue, crawl again, analyse again, show in folder, remove, delete | Level |
| Theming | Light by default, dark, an accent colour | Not found | Light, dark, follows the OS, forced colours | Ahead |
| Accessibility of the app | Nothing on the pages I read | Nothing on the pages I read | axe on every story, forced colours, reduced motion, 200% zoom with two open defects. No real screen reader yet | Can't compare. Ours is tested |
| Keyboard | No shortcut list in the guide. Ctrl+Shift+C opens a config diff (v22) | Not found | Ctrl+K palette, a shortcut sheet, grid keys, J and K in Issues | Ahead |

**What to take from Screaming Frog.**

- Density, as above. An expert reads 20 rows of 7 columns at a glance, and ours shows 16 of 4.
- "% of Total" beside every issue count. Our checks list showed a count only. **Done in round 5**: beside each
  check's count in Issues and Fix first.
- A row of totals before anything else. Their default Dashboard: Crawled, Internal, External, Issues, Warnings,
  Opportunities. **Done in round 5**: Overview opens with Crawled, Internal, External, Pages and Found; issues by
  severity follow it.
- Export on every grid, in the same place. **Done in step 3** (E5).

**What not to take.** The tab row, the 12 menus, and switches a user must find before a check runs.

## For Simple mode

### What Sitebulb does well

- **A fixed sentence pattern.** "Sentence 1: This is what the issue is. Sentence 2: This is why it could cause
  problems on your website." Every hint reads the same way, and can be pasted into a report.
- **Scale beside every hint.** "URLs: 3", "Percentage: 0.9%". Their words: "you can instantly see if an issue is
  widespread or only affects a few pages".
- **Two labels, not one.** Importance says how much it matters. Type says what it is: "Issue" ("an error or problem
  that needs to be fixed"), "Opportunity", or "Potential Issue" ("unlikely to be affecting the site at the moment").
- **"Insight".** A hint that is "neither an issue nor an opportunity". It keeps facts out of the to-do list.
- **One page per hint, in one shape.** "Why is this important?", "What does the Hint check?", "Examples that trigger
  this Hint", "How do you resolve this issue?".
- **The action sits on the hint.** "View URLs" and "Export URLs".
- **A report cut for its reader.** "Only print the scores and graphs, for a high-level report to give to
  executives." "Print a prioritized list of Hints to give to a developer to work through."

### What it does badly for a non-expert

- **The score can't be checked.** It is "based on the number/type/importance of all the triggered hints". No formula
  is published in what I read.
- **Unchecked reads as perfect.** "…if a particular audit option was not actually selected when setting up the
  audit, the score will show as '100.'"
- **The number reassures when it shouldn't.** Their own screenshot shows "Audit Score 90" beside 2 Critical and 14
  High hints.
- **Too much at once.** That screenshot's site has 57 hints under 5 importance levels and 3 types. There are "300+"
  in all.
- **It hands the judgement back.** Hints "typically require further investigation to understand how important they
  are for the particular website". Screaming Frog says the same of its priorities: "no tool can provide that as they
  lack context".
- **The words are an SEO's.** "Canonicalized", "link equity", "crawl budget", "robots directives".
- **Alarm without scale.** "Critical" on "Disallowed Style Sheet" for 4 URLs.
- **The fix is on a website, and often not a fix.** "You will need to look into the page templates/scripts/plugins
  to understand why the title tag is missing".

### What Simple should take

1. **Sitebulb's two sentences, plus a third: what to do, or who to ask.** One plain line each, about 25 words. Our
   three texts exist. Simple needs the short form of each.
2. **A count and a share on every item**: "9 of 265 pages".
3. **To fix, and to know.** Notices such as `noindex-page`, `canonicalised-page`, `blocked-by-robots` and
   `redirect-temporary` are facts on most sites. Simple lists them apart from the to-dos, as Sitebulb's Insights are.
4. **Five things first.** "Fix first" already does this. Everything else sits behind "Show all".
5. **Our own "learn more".** Our links go to Google's documentation, which is written for developers. Simple needs
   the example in the app: the failing markup from this crawl, then the corrected form.
6. **A report with two cuts**, as Sitebulb's:
   - A summary: the score, what was and wasn't checked, the five fixes with their counts.
   - The detail: each fix with what, why and how, its first URLs, and the full list as a file.
   - Both carry the site, the date and the crawl's size.

### The score: my proposal, not something read

- **Built from checks that ran.** A skipped check lowers nothing and raises nothing. The score says "based on 82 of
  86 checks", and lists the rest.
- **Weighted by severity and by the share of pages affected.** Not by how many checks fired.
- **Capped while an Error stands.** A site with a broken home page link can't read 90.
- **Explained one click away, in full.** The formula, and each item's cost in points.
- **Says what would move it.** "Fix these two to gain 12 points."

### What Simple should avoid

- A score of 100 for anything not checked, and a formula nobody can see.
- Severity words with no scale beside them.
- More than two labels on an item.
- A first screen that lists every check that fired.
- Words from the glossary's expert half without a gloss: canonical, indexable, hreflang, directive.
- "Requires further investigation" as an answer.

## Sources

Read on 2026-11-11.

### Screaming Frog

- [SEO Spider: features, free and paid lists](https://www.screamingfrog.co.uk/seo-spider/)
- [Pricing](https://www.screamingfrog.co.uk/seo-spider/pricing/)
- [Issues library](https://www.screamingfrog.co.uk/seo-spider/issues/)
- [User guide](https://www.screamingfrog.co.uk/seo-spider/user-guide/)
- [User guide: general](https://www.screamingfrog.co.uk/seo-spider/user-guide/general/)
- [User guide: configuration](https://www.screamingfrog.co.uk/seo-spider/user-guide/configuration/)
- [User guide: tabs](https://www.screamingfrog.co.uk/seo-spider/user-guide/tabs/)
- [Release history](https://www.screamingfrog.co.uk/seo-spider/release-history/)
- Release posts: [25](https://www.screamingfrog.co.uk/blog/seo-spider-25/),
  [24](https://www.screamingfrog.co.uk/blog/seo-spider-24/),
  [23](https://www.screamingfrog.co.uk/blog/seo-spider-23/),
  [22](https://www.screamingfrog.co.uk/blog/seo-spider-22/)
- [How to compare crawls](https://www.screamingfrog.co.uk/seo-spider/tutorials/how-to-compare-crawls/)
- [Log File Analyser pricing](https://www.screamingfrog.co.uk/log-file-analyser/pricing/)
- Screenshots: [Dashboard tab, v25](https://www.screamingfrog.co.uk/wp-content/uploads/2026/10/dashboard-tab.png),
  [Issues tab](https://www.screamingfrog.co.uk/wp-content/uploads/2022/08/right-hand-issues-tab-updated.png),
  [three panes, dark, 2020](https://www.screamingfrog.co.uk/wp-content/uploads/2020/11/screaming-frog-seo-spider-version-14-dark-mode.jpg)

### Sitebulb

- [Features](https://sitebulb.com/features/)
- [Prioritized Hints](https://sitebulb.com/features/prioritized-hints/)
- [Hint explanations](https://sitebulb.com/hints/), [Indexability hints](https://sitebulb.com/hints/indexability/),
  [On Page hints](https://sitebulb.com/hints/on-page/)
- Two hints: [Title tag is missing](https://sitebulb.com/hints/on-page/title-tag-is-missing/),
  [Canonical points to a noindex URL](https://sitebulb.com/hints/indexability/canonical-points-to-a-noindex-url/)
- [Pricing](https://sitebulb.com/pricing/)
- [Release notes](https://sitebulb.com/release-notes/)
- [Navigating Sitebulb audits](https://support.sitebulb.com/en/articles/9854039-navigating-sitebulb-audits)
- [Build PDF reports](https://support.sitebulb.com/en/articles/9497280-build-pdf-reports-from-sitebulb-audits)
- [Looker Studio connector, for the score's wording](https://support.sitebulb.com/en/articles/9857610-looker-studio-sitebulb-connector)
- Screenshots: [hints with "Critical"](https://sitebulb.com/media/2101/critical-issues.png),
  [one hint](https://sitebulb.com/media/2102/hint-zoom-in.png),
  [Audit Score](https://sitebulb.com/media/h5ve4vce/all-hints.png)

### Ours

- `docs/v2-rebuild.md`, `docs/v2-ui-ia.md`, `docs/v2-ui-brief.md`, `docs/v2-ui-discovery.md`,
  `docs/v2-ui-architecture.md`, `engine/README.md`
- `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs`
- 12 gallery screenshots: Home, Overview (done, breakdowns, live), URLs (filtered, all), the inspector (summary,
  issues, docked at the bottom), Issues (groups, a skipped check), the palette

### What I couldn't check

- **Neither tool was run.** Every UI judgement on theirs rests on three screenshots of each and their guides.
- **Sitebulb's Lite and Pro prices.** The page fills them in by script, and they came back blank. Read in its text:
  Cloud "from £95/month", extra Pro users "from £7 per month", a 14-day trial, no free version.
- **Screaming Frog's price in pounds.** The page's text says "£199 per year". Its price box showed €245 when fetched.
- **Sitebulb's score formula.** Not published in what I read.
- **Sitebulb's URL Details panel, URL lists and filters.** From a search engine's summary of their help pages, not
  the pages themselves. Their documentation index returned almost no text.
- **Screaming Frog's 348 issue pages.** I read the index (names, types, priorities) and about 30 definitions in the
  tabs guide, not each page.
- **Keyboard shortcuts and accessibility of either app.** Nothing found. That isn't proof they have none.
- **Our numbers.** Speed and memory are from the synthetic benchmark, and aren't comparable with their rough guide
  for real sites.
