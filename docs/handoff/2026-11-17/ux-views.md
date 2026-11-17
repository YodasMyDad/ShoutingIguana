# UX audit: views and flows, judged by the site owner who has to fix the site

Scope: the 45 release-build shots (`gallery6-fixes/all3/*-light.png`, captions from `index.json`), plus the code behind
each screen on `ui/step-3` (`app/src/simple/**`, `app/src/screens/**`, `app/src/inspector/**`), plus engine code where a
screen's limits come from the engine. I left out per-check evidence (which facts each of the 86 checks shows); another
auditor has that. Where a screen problem is really "the screen has no place to show a fact", I flag the slot that's
missing and leave the per-check content to them.

Ratings:
- **Blocks**: the owner can't decide or act from this view.
- **Slows**: they can, but only by detouring, guessing or doing the work twice.
- **Polish**: it works, but could read faster.

Data caveat: the shots use a garden-furniture crawl and a synthetic test site, so I judge the structure, not the
numbers.

---

## 1. Problems that run across views

### X1. Nothing shows before and after, so the loop never closes. **Blocks**
- Simple's problem page ends with "Fixed it? … Check again, and this drops off the list if it's gone"
  (`simple/ProblemScreen.tsx:364`). After the recheck, the new crawl replaces the site's row and the list starts over.
  Nothing says what was fixed, what's new, or what's still there.
- Home says "1 earlier check" (`simple/home/SiteRow.tsx:179`) with no score change. The Advanced Library folds older
  crawls under the site, with no change columns.
- The owner can't confirm a fix, can't spot a regression, and can't show a client progress. A problem "dropping off"
  is invisible, because there is nothing to compare against unless they remember it.
- The docs push crawl comparison to "later" (`docs/v2-ui-simple.md:326`, `docs/v2-rebuild.md:103`). The fix-it loop
  needs only a check-level diff, not the full feature. Both crawl files already hold everything:
  - **Home / Library:** "85 ▲ from 71" beside the ring, and "3 fixed · 1 new" under To fix.
  - **Summary:** a line under the sentence: "Since 31 Oct: 3 problems fixed, 1 new, score 71 → 85". Each item opens
    its list.
  - **What to fix / Issues:** a "New", "Fewer (9 → 4)" or "Same" chip on each row, and a folded **Fixed since last
    check** group with a tick.
  - **Where it is / affected URLs:** mark each cause "still there" or "new". Fixed causes get a struck-through row of
    their own.
  - A check-level diff (per-check counts) is cheap and covers most of the value. A cause-level diff (group key =
    related URL or detail) is the next step.

### X2. A "cause" cell shows the address but not the fact that makes it a cause. **Blocks for Large images; Slows elsewhere**
This is the owner's own example, and the reason is structural:
- **Simple, Where it is.** The left column is the related URL, and the only fact under it is the HTTP status, and only
  when it's 400 or above (`simple/WhereItIs.tsx:252`). For Large images, the image shows with no size, so the owner
  can't tell a 210 KB file from a 2.1 MB one. The same gap hits:
  - redirect targets (where does it end?)
  - duplicate titles and descriptions (the shared text isn't shown, only the cluster's first page)
  - canonical targets (what's wrong with the target?)
- **Advanced, Issues.** A group row is "Related URL · Pages · Share of issues" (`screens/issues/IssueGroups.tsx:439-536`).
  It shows no size, no final URL and no shared text.
- **Ordering.** Groups are ordered by pages, descending (`engine/.../IssueGroupReader.cs:64`). For images, impact is
  size × pages. A 2 MB hero on 3 pages should rank above a 210 KB thumbnail on 40.
- **Fix: a "fact" slot on every group.**
  - The engine returns one measured value of the related URL per group: `sizeBytes`, status → final URL, the
    duplicated text, the target's indexability.
  - Simple prints it under the address: "2.1 MB · PNG · 896 px wide".
  - Advanced adds it as a column after Related URL.
  - Let a check choose its group order. Large images: bytes over the limit × pages. Redirect chains: number of hops.
  - Which fact each check needs belongs to the evidence auditor. The slot and the ordering belong to these screens.

### X3. Resource "inlinks" read 0, which is wrong for images, CSS and JS. **Blocks the "where is this image shown" question in Advanced**
- `inlinks`, `followedInlinks` and `linkingPages` count hyperlinks only
  (`engine/.../Analysis/UrlTable.cs:141,165,175`).
- Shot 29 shows `/css/cach…D970314305C12A.css` and `favicon.ico` with **0 inlinks**, though every page loads them. Every
  image therefore reads "0 inlinks" as well.
- The Performance preset, the one shot 27 uses to sort images by size, has no "where" column at all
  (`screens/urls/urlColumns.tsx:72`). The only route is the row menu → "Show its inlinks in Links".
- Fix:
  - Add a **"Used on" (pages)** column that counts every link kind (img, srcset, css, script, icon) to the URL.
  - Put it in the Performance preset and the image filter's default columns.
  - For a non-HTML URL, show "Used on" where Inlinks would read 0, or rename Inlinks to "Linked from (hyperlinks)".
  - Never show a bare 0 that is really "not counted".

### X4. Lists of affected pages ignore how much each page matters. **Slows**
- **Advanced:** the issue rows grid reads only `url` and `detail` (`screens/issues/IssueRowsGrid.tsx:70`). There's no
  inlinks, depth, indexable or traffic proxy column.
- **Simple:** Where it is lists groups by page count. Pages inside a group come in crawl order.
- **Report:** examples are "the first 5 pages … in crawl order" (`docs/v2-ui-simple.md:690`).
- Result: a missing H1 on the home page ranks level with one on `/p/29?page=7`.
- Fix:
  - Add Inlinks (hyperlinks), Depth and Indexable to the rows grid, and sort by inlinks descending by default.
  - In Simple and the report, list a group's pages in the same order (the home page and main categories first).
  - Say so: "Most-linked pages first".

### X5. Related checks show up as separate jobs. **Slows**
- The garden Summary lists **"Pages or files that can't be found" (9 addresses)** and **"Links that lead to broken
  pages" (5 pages)**. Both are Serious, both cost points (−2 and −4), and one edit clears both.
- The same applies to:
  - redirect-chain / links-to-redirects
  - canonical-broken / internal-client-error
  - image-broken / internal-client-error (for images)
  - hreflang-broken-target / 4xx
- The owner reads two jobs, possibly briefs a developer twice, and can't see that one fix earns 6 points.
- Fix:
  - Add a "relates to" field to the catalog.
  - On the problem page's head, add "Fixing this also clears **Links that lead to broken pages** (−4)".
  - In What to fix, nest the downstream check under the upstream one: one row with two counts and the combined points.

### X6. Counts measure rows, not work. **Slows (Advanced), Polish (Simple)**
- **Advanced Overview and Issues** lead with "19 checks found 933 issues". The Notices card says "841 issues between
  them", and 264 of those are one logo link in the template (shot 11's One-fix callout). The number that matters is
  "how many separate things do I change", and the engine already knows it: `groupCount` and `likelyTemplate`.
  - Fix: "19 checks · about 40 things to change · 3 of them in the shared template". Put causes on each severity card.
- **Simple's Fix these first** mixes denominators on adjacent rows: "9 of 4,521 addresses" next to "5 of 265 pages".
  The serious row looks tiny (0.2%), and the rows can't be compared.
  - Fix: count pages affected wherever possible ("Broken on 9 addresses, linked from 5 pages"), or drop the
    denominator from the row and keep it on the problem page.

### X7. No quick way to confirm one fix. **Slows**
- "Check the site again" means a full crawl. That took 37 min 25 s on the 265-page garden site (shot 7).
- An owner who fixed one footer link waits 37 minutes to see whether it worked, and then can't see the result (X1).
- Fix: put **"Recheck these N pages"** on the problem page and on the Issues check detail. It refetches the affected
  URLs and their targets, reruns that check, and shows "4 of 5 fixed" inline. It's a narrow slice of step 4's list
  mode, and worth pulling forward for this one use.

### X8. Simple's "to-do list" can't be ticked off. **Slows**
- First start promises "You get a score and a to-do list" (shot 1). What to fix has no state, and the doc's choice is
  deliberate: "The app can't mark a fix as made, and doesn't pretend to".
- A tick that the **owner** sets, worded as their claim, isn't pretending:
  - "You marked this fixed on 17 Nov. The next check will confirm."
  - Store it in the app's preferences, keyed by site and check, never in the crawl file.
  - The next crawl's diff (X1) turns it green, or reopens it with "Still on 2 pages".
  - It turns a report into a working list. That's how Sitebulb's hints and Linear's issues hold someone across days.

---

## 2. Flows: where the chain breaks

**Simple: score → what to fix → the exact place → confirm fixed**

1. **Score → why.** Works. Every row in "How the score is worked out" links to its problem (`ScoreBreakdownDialog.tsx`).
   The Summary says "Fixing these takes the score from 85 to X".
   - Gap: "Fix these first" is ordered "most serious, then most pages", not by points won. Shot 8 has "Links that lead
     to broken pages" at −4, but it sits below "Pages or files…" at −2. Each row's points ("+4") only appear on the
     problem page. **Polish/Slows.**
2. **What to fix → one problem.** Works: one click, J/K, plain words, who fixes it, One fix tag.
   - Gap: related checks are separate (X5). **Slows.**
3. **Problem → the place on the site.** This is where the chain breaks.
   - Pages show by title, address and an open button, which is good.
   - For links, Where it is gives the page but **not the link's words or where on the page it sits**. Shot 14 shows
     `/edale-4-seater-sofa-set/` 404 → "GardenFurnitureSets™ Huge Savings… /". The steps then say "Open each page… and
     find the link", in a page with ~100 links.
   - Advanced already has the facts ("Content · 'Broken link' · Followed", shot 25). Simple's `GroupRow` only adds
     found-on words for `references === 'sources'` checks, and even those carry no anchor text.
   - For Large images it has no size (X2). **Slows, and close to Blocks for a non-expert.**
4. **Fixed → confirm.** Broken. A full recrawl (X7), then nothing that says it worked (X1). **Blocks.**

**Advanced: Overview → Fix first → Issues → the page → act**

1. **Overview → Issues.** Works. Totals, status codes and indexability reasons all link to filtered lists
   (`overview/StatusCodes.tsx:159`, `CrawlSections.tsx`).
2. **Issues: choose a check.** The list is in catalog order only (`issues/checkList.ts:2,28`). With 77 checks firing
   (shot 24), there's no sort by count, points, severity or One fix. Severity tabs filter but don't rank. **Slows.**
3. **Check → affected URLs.** The explanation sits above the fold and the evidence below it (shot 15: "What's wrong /
   Why / How" before "Affected URLs"). An SEO wants the evidence first. Groups lack a fact (X2), and rows lack
   importance (X4). Found-on (shot 25) is excellent. **Slows.**
4. **Page → inspector.** Good: indexability is tested step by step, with limits beside the values.
   - Gap: no Images tab. Outlinks has Status / Anchor / Kind / Position / Follow, with no target size
     (`inspector/LinksSection.tsx:152-190`). "Which images on this page are heavy?" can't be answered here.
     **Slows.**
5. **Confirm.** Same break as Simple (X1, X7).

**Hand-off (the owner sends it to a developer).** Good: Copy the list and Save as a spreadsheet on every problem, a
Summary page and a full report.
- Gap: report examples come in crawl order (X4) and carry no fact for causes (X2). The developer gets addresses
  without "2.1 MB" or "the link says 'Sale'". **Slows.**

---

## 3. Simple mode, view by view

### 01 First start
- **The owner's question:** what does this do, and how do I start?
- **What works:** one field, three honest steps, and a way in for an existing file.
- **Gaps:**
  - Step 3 promises "a to-do list". Make it one (X8), or call it "a list of what to fix". **Polish.**

### 02 Home: Your sites
- **The owner's question:** how is each of my sites doing, and is it getting better?
- **What works:** score ring and word, serious / worth-fixing counts, last checked, Continue for a stopped check.
- **Gaps:**
  - No trend. "1 earlier check" folds a list but shows no change (X1). Add "▲14 since 31 Oct" beside the ring, and
    "3 fixed · 1 new" in To fix. **Blocks** (on the progress question).
  - No age warning. "2 weeks ago" sits in a muted column. Results more than ~30 days old should say "Results may be
    out of date: check again", with the button on the row. Today "Check again" is hidden in the ··· menu
    (`SiteRow.tsx:380`). **Polish.**
  - "No score / Not worked out yet" for the stopped site doesn't give the size of what's found so far. "12,044 pages
    read" is there, but the next action is Continue only. Add "or see the results so far" when they exist.
    **Polish.**

### 03 Check a website (Options)
- **The owner's question:** how do I check without hurting my hosting?
- **What works:** three plain questions with clear consequences.
- **Gaps:**
  - "Stop after ___ addresses" doesn't say a limit makes the score partial until you're past it. It says "The score
    then covers only what was read", which is fine.
  - Nothing to act on here. No findings.

### 04–05 Checking (live / paused)
- **The owner's question:** how long, is anything wrong, can I leave?
- **What works:** a floor for the time left, "Is anything in the way?", and Pause / Resume explained.
- **Gaps:**
  - "Results appear when the check finishes" leaves 37 minutes of nothing on a mid-size site. Showing early signs,
    worded so they don't invite a premature fix, is cheap: "Found so far: 9 addresses that don't work, 3 slow pages".
    The figures already exist live (Advanced Overview shows status codes live). **Polish.**
  - "Still to check 98,218" grows while the bar shrinks. The sentence covers it, so this is fine.

### 06 Finished toast
- **The owner's question:** how did it go, and what now?
- **What works:** the score plus two next steps.
- **Gaps:**
  - For a second check of a site, the toast is where the change belongs: "Score 85 (▲14). 3 problems fixed, 1 new"
    (X1). **Slows.**

### 07 Summary
- **The owner's question:** how good is my site, and what do I do first?
- **What works:** score, word, one sentence, five counts, top five with "takes the score from 85 to X", and Check
  again plus Open the report in the head.
- **Gaps:**
  - No change since the last check (X1). **Blocks** for a returning user.
  - "Fix these first" is ordered by severity then pages, not points (see flow 1). Show "+4" on each row, the points
    fixing it gives back. **Polish.**
  - Mixed denominators (X6). **Polish.**
  - Related checks are separate (X5). **Slows.**
  - "Checked on 31 October 2026" has no age cue. Past a month, add "Your site may have changed since". **Polish.**

### 08 How the score is worked out
- **The owner's question:** why this number, and what moves it?
- **What works:** rows link to the problem, the points add up, and the 89 cap is stated.
- **Gaps:**
  - Points are a column but not the order: sort by points so the biggest win leads. **Polish.**
  - No "if you fix all six: 100" or "fix these two: 91" ceiling lines. The cap at 89 matters: "Fix the two serious
    problems first: nothing else can take you past 89". **Polish.**

### 09 Summary, stopped early
- **The owner's question:** is this enough to act on?
- **What works:** dashed "so far" ring, what it covers, Continue in the banner and the top bar.
- **Gaps:**
  - It doesn't say whether the problems found so far are safe to fix now. They are. Add "Problems below are real:
    you can start fixing them while the rest is checked". **Polish.**

### 10 What to fix
- **The owner's question:** the whole list, in the order to work through it.
- **What works:** grouped by severity, a plain line each, counts, Copy this list, Open the report.
- **Gaps:**
  - No state: can't tick, nothing new or fixed since last time (X1, X8). **Slows.**
  - No per-row points or effort. "One fix" is the only effort signal, which is good, but a row needs "+4 points ·
    one fix" to rank by. **Polish.**
  - Related checks aren't merged (X5). **Slows.**

### 11 / 14 One problem
- **The owner's question:** what exactly, where exactly, how do I fix it, and did it work?
- **What works:** head facts (count, points, who fixes it), why plus steps, One fix callout, Where it is with titles
  and addresses, Copy / spreadsheet, See it in Advanced.
- **Gaps:**
  - **Where it is has no fact on the cause** (X2): image size, redirect end, the shared title, the canonical target's
    problem. **Blocks** for Large images.
  - **For link checks, no anchor text or position** under each page ("the link that says 'Edale 4 seater' · in the
    page's content"). Advanced has both (shot 25). The steps tell the owner to "find the link" without the words to
    search for. Add one muted line per page: *Link text "Edale 4 Seater Sofa Set" · in the content*. For a template
    group: *in the menu on every page*. **Slows.**
  - **Pages inside a group come in crawl order** (X4). **Slows.**
  - **"Fixed it?" leads to a full recrawl** (X7) and then nothing visible (X1). Add **Recheck these 5 pages**, then
    "4 fixed, 1 still broken". **Blocks** (on confirming).
  - **The head reads "265 of 265 pages" in large type next to "One fix clears 264"** (shot 11). The scary number
    leads, and the actionable one is second. Put the One-fix sentence in the head when `likelyTemplate` covers more
    than 90%: "1 fix, in your site's shared design (265 pages)". **Polish.**

### 12 Report (Simple)
- **The owner's question:** something I can send or keep.
- **What works:** paper preview, Full / Summary, spreadsheet, Save, PDF.
- **Gaps:**
  - Examples are the first five in crawl order with no cause facts (X2, X4). **Slows** for whoever receives it.
  - No "since last time" page (X1). A recurring client report needs it. **Slows.**

### 13 Settings (Simple)
- Nothing to act on for fixing the site. Fine.

### 15 The switch into Advanced
- **What works:** lands on the same check. Good.

---

## 4. Advanced mode, view by view

### 16 URLs at 200% zoom
- Works. Nothing missing for decisions beyond what the URLs findings below cover.

### 17 Home with a crawl running
- **The question:** is it healthy and how far along is it?
- **What works:** counts, rate sparkline, Pause / Stop / Open.
- **Gaps:**
  - No early signs of trouble on the card (4xx / 5xx so far, blocked). Overview has them. Add "94 × 404 so far" with
    a link. **Polish.**

### 18–19 Library and delete
- **The question:** which site needs attention, and is it improving?
- **What works:** status, URLs, pages, checks with issues by severity, older crawls folded, a clear delete.
- **Gaps:**
  - No score column, though Simple shows one and the engine computes it. **Polish.**
  - "Checks with issues" counts checks, not causes or pages (X6). **Polish.**
  - No change against the older crawl folded right under it, and no **Compare with…** in the crawl menu
    (Open / Crawl again / Analyse again / Show in folder / Remove / Delete). (X1) **Blocks** on the trend question.

### 20 New crawl (Advanced)
- Configuration only. The proxy test is good. Nothing to act on.

### 21 Overview, live
- **The question:** is the crawl going right, and is anything badly wrong already?
- **What works:** live figures, found vs crawled, status codes with the not-2xx part magnified, and rows linking to
  lists.
- **Gaps:**
  - Nothing major. You could add "top 404s so far" (the target with the most linking pages) for a fix-while-you-crawl
    workflow. **Polish.**

### 22 Overview, done
- **The question:** how bad is it, what first, what wasn't checked?
- **What works:** score, totals, severity cards, Fix first, Not checked with reasons, and every figure linking through.
- **Gaps:**
  - The "933 issues" headline counts rows (X6). **Slows.**
  - No change since the previous crawl of this site (X1). **Blocks** on the trend question.
  - Fix first rows show count and share but not points or One fix. Add both. **Polish.**

### 23 Crawl switcher
- **What works:** recency, Interrupted / Stopped told apart.
- **Gaps:**
  - No score in the list, so you can't pick the good or bad crawl. **Polish.**

### 24 / 26 Issues, the checks list and the hide menu
- **The question:** which checks matter most here?
- **What works:** severity counts, filter, share or "Whole site", hide for this crawl or every crawl.
- **Gaps:**
  - **Catalog order only.** Add Sort: Fix-first order (the default, matching Overview), Count, Points, Severity,
    Category. Mark One-fix checks in the list. **Slows.**
  - The detail leads with three paragraphs, and the affected list starts below the fold (shots 15, 24). Move
    **Affected URLs** up beside the stats, and fold Why / How into a two-line strip with "More". **Slows.**
  - "Pages affected · Open them in URLs" is good, but the rows grid has no importance columns (X4). **Slows.**

### 25 Issues, found on
- **The question:** where is each broken URL linked from, and how do I find the link?
- **What works:** the best view in the app. Found on N pages, the source, position, anchor text, Show the link in
  Links, and open-in-browser.
- **Gaps:**
  - When a broken URL is found on 200 pages, the group should say "Likely template: in the footer" before listing
    pages. The engine has `likelyTemplate`, and Simple uses it. Check it shows here. **Polish.**

### 27–29 URLs (Performance image sort, columns, open in browser)
- **The question:** which images or pages are heaviest or slowest, and where are they used?
- **What works:** presets, filter chips, size sort, row menu (inspect, open, filter by value, show inlinks), pinning.
- **Gaps:**
  - **No "used on" for resources** (X3). An image sorted by size can't tell you how many pages carry it. Inlinks reads
    0 for every image and stylesheet, which is wrong. **Blocks** on "where is this image shown".
  - **No image facts** beyond bytes: no pixel dimensions, format, or largest displayed size. The engine records width
    and height attributes for the missing-dimensions check. If intrinsic size isn't read, say so; it's a gap for the
    evidence audit. **Slows.**
  - The Overview preset has Inlinks and Issues, but Performance has neither. Add Used on and Issues to Performance.
    **Slows.**
  - The URL column mid-truncates (`/rattan-f…rattan-patio-sets/`). With the pinned-title trick (shot 28) that's
    acceptable. **Polish.**

### 30 Inspector
- **The question:** why is this page (not) fine, and what's on it?
- **What works:** indexability tested step by step, limits beside values ("Limit 1 s", "Minimum 200"), title length
  scale, issues count.
- **Gaps:**
  - **No Images tab.** A page's images with size, alt, dimensions, lazy or not, and above the fold. Outlinks has no
    target size column (`inspector/LinksSection.tsx:152-190`). **Slows.**
  - The Summary should carry "Used on N pages" for a resource URL, and for a page its importance (inlinks and depth,
    likely already there). **Polish.**
  - The tab overflow ("7 more") hides Inlinks and Outlinks, the two you need to act on a link problem. Keep Inlinks
    visible ahead of Headers and Structured data. **Polish.**

### 31 / 35 Links (and export)
- **The question:** where is this link, and which broken or redirected targets matter most?
- **What works:** presets (Broken, Redirects, Nofollow, No anchor, External), kind, position, anchor, status, Show in
  URLs, export with progress and Cancel.
- **Gaps:**
  - **Flat list only.** One broken footer link is 265 rows. Add **Group by target** (one row per target: status,
    linking pages, top anchor, positions, "Likely template"). That's the screen the owner actually works from. Issues
    does this for checks, and Links should have it for any filter. **Slows.**
  - **No "Images" preset.** Kind is Image, with Source page, Image, Alt, **to.sizeBytes** (the engine supports `to.*`
    columns, `Storage/LinkColumns.cs:88-91`), to.status and position, sorted by size. That is the complete answer to
    "large images: how big, and where shown". Make the image checks' "Show in Links" land on it. **Slows.**
  - Target URL is mid-truncated (`/garde…furniture/`) while the Source URL column has spare room. Rebalance widths.
    **Polish.**

### 32 Sitemaps
- **The question:** are my sitemaps read, complete, and clean?
- **What works:** index tree, read / failed, listed / crawled / blocked / not crawled / non-indexable as links,
  "28 of 417 pages aren't in any sitemap".
- **Gaps:**
  - The failure reason is cut ("Couldn't be read: 404 Not…") and a hover away at this width. Wrap it, since it's the
    one action-bearing cell. **Polish.**
  - No **"Pages to add" / "URLs to remove"** export, the list the owner hands to whoever regenerates the sitemap. The
    links lead to filtered URLs, where Export works, which is one detour. **Polish.**

### 33 robots.txt
- **The question:** is my robots.txt blocking something it shouldn't?
- **What works:** the file as read, the lines obeyed, Blocked URLs by rule, Copy / Export.
- **Gaps:**
  - **No consequence columns.** Blocked URL and Blocked by only (`robots/RobotsFileView.tsx:528-536`). Add **Linked
    from (pages)**, **In a sitemap**, and the referrer's indexability. A blocked URL that is in a sitemap
    (`/private/listed` here) or linked from 200 pages is the decision point. Flag it: "In your sitemap but blocked".
    **Slows.**
  - No "test a URL" box against the rules. **Polish.**

### 34 Report (Advanced)
- Same as 12: X2 and X4 on the examples, X1 for a "since last time" section.

### 36 Crawl settings (this crawl)
- **What works:** what this crawl used, read-only, Crawl again with these.
- **Gaps:**
  - The "Continue: only a stopped crawl can be continued" card leads the page on a finished crawl, a dead box above
    the content. Hide it unless the crawl can be continued. **Polish.**

### 37–41, 43 Settings / Shortcuts
- **38 Analysis:** changing a limit makes crawls out of date until analysed again, but the page offers no **Analyse
  the open crawl again** button. That's in the Library's ··· menu. Put it inline after a change. **Polish.**
- The rest is configuration. Nothing to act on.

### 42 Command palette
- **What works:** checks by name.
- **Gaps:**
  - Results don't show their count on this crawl ("Title too long · 41 pages"), so the palette can't triage.
  - It doesn't find URLs by path. Typing `/rattan` should offer the URL to inspect. **Polish.**

### 44–45 Window title, drop overlay
- Fine. Nothing to act on.

---

## 5. The top 12, ranked

1. **X1: no before/after anywhere.** Add a check-level diff against the site's previous crawl:
   - score change on Home, Library, Summary and Overview
   - New / Fewer / Fixed chips on What to fix and Issues
   - a "Fixed since last check" group
   **Blocks.**
2. **X2: a cause shows its address, not its fact.** Add a fact slot per group: size for images, final URL for
   redirects, shared text for duplicates, target status for canonicals. Order groups by impact (size × pages), in
   Simple's Where it is and Advanced's groups. **Blocks** for Large images.
3. **X3: images, CSS and JS read 0 inlinks.** Add a "Used on (pages)" column that counts every link kind, and put it in
   the Performance preset. Never show a 0 that means "not counted". **Blocks** on "where is this image shown".
4. **Simple Where it is for links has no anchor text or position.** Add "Link text '…' · in the menu / content /
   footer" under each page. Advanced already has the data. **Slows.**
5. **X7: confirming one fix takes a 37-minute recrawl.** Add "Recheck these N pages" on the problem and check views.
   **Slows.**
6. **Issues list only in catalog order.** Add Sort (Fix-first, count, points, severity), and move the affected list
   above Why / How. **Slows.**
7. **X5: related checks are separate jobs.** Add "Fixing this also clears X (−4)", and nest them in What to fix.
   **Slows.**
8. **X4: affected lists ignore page importance.** Add Inlinks / Depth / Indexable columns, sort by inlinks by default,
   and use the same order in Simple and the report. **Slows.**
9. **X8: the to-do list can't be ticked.** Add an owner-set "I've fixed this" that the next check confirms or reopens.
   **Slows.**
10. **Links: no group-by-target and no Images preset.** Group by target; add an Images preset with `to.sizeBytes` and
    alt; add an Images tab or size column in the inspector. **Slows.**
11. **robots.txt: blocked URLs have no consequence columns.** Add Linked from, In sitemap and a conflict flag. **Slows.**
12. **X6: headline counts measure rows.** Lead with causes, the separate things to change, including how many are in
    the template. Make Simple's Fix-first denominators match. **Slows / Polish.**
