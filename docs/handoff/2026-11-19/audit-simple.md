# UX audit, 2026-11-19: Simple mode

Zone: all of Simple. Driven with `drive.mjs … simple` at 2560 × 1380 first, then 1280 × 720, on the garden crawl
(`33454603b74b57f1`), the synthetic crawl (`0b71328b5d42d1ed`) and jitsunami (`e6b4b1d9a0bb1541`). Nothing was crawled,
rechecked or changed; outside requests were blocked in the flows that click links.

The one thing written outside this file: the 404 list was exported as a CSV into the scratchpad to read it (one click
on "Save as a spreadsheet", with the path pointed there).

While this ran, someone else had uncommitted edits in this worktree (29 files by 07:17, among them
`IssuePlainTexts.cs`, `IssueCatalog.cs` and `SummaryParts.tsx`, rewording "Later pages of a list that point Google at
the first page" and moving a score). They aren't this audit's and were left alone. The words quoted here are what the
running app served; the `file:line` references were rechecked after those edits.

- Flows: `…/scratchpad/audit/scripts/simple/` (`s01` to `s18`).
- Shots: `…/scratchpad/audit/shots/`, prefixes `simple-w-` (2560), `simple-n-` (1280), `simple-ev-` (close-ups),
  `simple-w-p-<crawl>-<check>` (one per problem page: 29 garden, 25 jitsunami, 150 synthetic).
- Page text and controls of every problem page: `…/scratchpad/audit/data/simple/<crawl>-w-<check>.txt`; the report's
  text in `report-garden.txt`; the exported CSV in `garden-404.csv`.

Verdict in one line: Simple explains each problem well and says how to fix it, but the "Where it is" card names pages
without letting you go to them, leads the broken-link job with the dead address, and the screen's own way back to the
list is broken.

## 1. The tasks tried

| # | What a shop owner wants | Path taken | Clicks | Where it broke |
| --- | --- | --- | --- | --- |
| 1 | "Is my site OK, what do I do first?" | Home shows 85 Good, 3 serious on the row. Row → Summary → "Fix these first" 1 | 2 to the first problem's page | Fine to here. At 1280 × 720 the list of where it is sits wholly below the fold (S-14). |
| 1b | See the first faulty thing on my live site | On the 404 page: hover a row to reveal the icon, click it | 3, and it opens the **dead** address | The page that holds the link can't be opened from here at all (S-02). Detour: header link "Links that lead to broken pages" → hover → icon = 4 clicks and a guess. |
| 2 | Read each of the 29 garden problems top to bottom | Every page walked (table in section 3) | | What's wrong and how to fix: good on nearly all. Where: text only. What to change it to: missing on descriptions and titles (S-13), security settings (S-16). How to confirm: "Fixed it?" is at the very bottom (S-14). |
| 3 | Broken link: what should lead | Compared the 404 page, the folded "Links that lead to broken pages" page, the report | | Proposal in section 4. |
| 4 | Hand the list to a developer | "Copy the list" (clipboard logged), "Save as a spreadsheet" (CSV exported to the scratchpad) | 1 / 3 | The 404 clipboard list has no holding pages (S-05). The spreadsheet has them, in one mixed cell. |
| 5 | Use What to fix as a to-do list | Sorted three ways, expanded all, copied, went into a problem and back | | Back link dead (S-01), expansion and scroll lost (S-10), no search for a page (S-06), what got worse isn't marked (S-12). |
| 6 | "I fixed it, check again" | Read every state of the "Fixed it?" card; opened "Check again" then Cancel | | "Recheck…" runs at once with no dialog, so it was not pressed. Wrong numbers in its words (S-08), odd result words (S-15). |
| 7 | Read the Report as the client | Full report and Summary page read; a link clicked with outside requests blocked | | Links do nothing inside the app (S-11). Rows speak engine, not plain words (S-11). |
| 8 | Words | Collected while reading | | Section 5. |
| 9 | Switch to Advanced and back from every screen | Home, /new, Summary, What to fix, a problem, Report, Settings; "See it in Advanced" on the 404 | | What to fix → Advanced → Simple lands on a problem, not the list. "See it in Advanced" then Back skips the problem (S-10). |

## 2. Findings, ranked

### S-01 · One problem's page, sidebar · **Blocks** · "← What to fix" and the sidebar's "What to fix" do nothing on a problem's page

On `/issues/url-parameters` I clicked "← What to fix" above the title: the URL and screen stayed. Same for the sidebar's
"What to fix" and for Ctrl+2. Both links render `href="/crawls/…/issues/url-parameters"` with `aria-current="page"`:
they point at the page you are on. The breadcrumb's "What to fix", site and date are plain spans. The only ways out are
the toolbar's Back arrow, Summary, or Home. Shots `simple-w-33-wtf-back.png`, `simple-w-38-sidebar-wtf-from-problem.png`.

- Code: `app/src/simple/ProblemScreen.tsx:162-169` and `app/src/layout/Sidebar.tsx:217` link to
  `/crawls/$crawl/issues/{-$check}` with `params={{ crawl }}`; the optional `check` is inherited from the current
  location. Ctrl+2, the shortcut the palette lists for "Go to What to fix", stays put the same way.
- Fix: pass `check: undefined` on every link that means the list (grep `issues/{-$check}` with no `check:` in
  `simple/` and `layout/Sidebar.tsx`). Make the breadcrumb's "What to fix" a link on a problem's page. Add an
  e2e that clicks the back link.

### S-02 · "Pages or files that can't be found" (the first job on the garden crawl) · **Blocks** · The page that holds the broken link is text; only the dead address opens

Each row leads with the missing address, then `Linked from /blog/best-rattan-patio-sets/ (in the main content: “Buy
Now”)`, then "It’s a page". I clicked the address text, the "Linked from" line and the page named in the strip "One
page links to 5 of these 8 addresses": no navigation, no window, and the cursor never becomes a hand. The one control is an icon that
opens the dead address. So the owner can open the thing that doesn't exist and not the page they must edit. Shots
`simple-w-10-404-top.png`, `simple-w-12-404-where-hover.png`.

- Code: `app/src/simple/FoundOnWords.tsx:24` (a `<span>` of words from `plainEvidence.ts:311 plainLinkedFrom`),
  `app/src/simple/ProblemScreen.tsx:559` (the strip's page is a `<span class="font-mono">`),
  `app/src/simple/plainEvidence.ts:386` ("It’s a page").
- Fix: render each "Linked from" page as its own line with the page's title, its path, an always-visible "Open page"
  button, and the link's words. Make the strip's page the same control. Drop "It’s a page" unless it is not a page
  ("It’s an image" earns its place). Layout in section 4.

### S-03 · Every "Where it is" card · **Slows** · Nothing looks clickable: the open icons are invisible until the row is hovered, and titles and paths are not links

With the mouse away, the card on "Links that lead to broken pages" shows 16 addresses and no control
(`simple-w-90-brokenlinks-rows-nohover.png`; the buttons' computed opacity is 0). Hovering a row shows a 24 px icon after
the path. Clicking or double-clicking the page title or path does nothing; right-click gives no menu; thumbnails of
images aren't links either. An owner on a touch screen, or one who doesn't sweep the mouse over the list, never learns
anything opens.

- Code: `app/src/styles/components.css:2254-2270` (`.si-open-url { opacity: 0 }`, shown on `tr:hover`),
  `app/src/simple/WhereItIs.tsx:145-155` (path as a `<span>`, then `OpenUrlButton`).
- Fix: in Simple, always show the control, as words where there's room ("Open page", "Open image"); make the title and
  path one link that does the same. There is 700 px of blank either side of the card at 2560.

### S-04 · Summary, What to fix · **Slows** · The first job leads to the dead-address view; the view built for fixing is folded under it

"Fix these first" 1 is "Pages or files that can't be found, 8 addresses". Under it, unnumbered, "Links that lead to
broken pages, 4 pages. Fixed along with the problem above." The owner clicks 1 and gets S-02. The folded page is the one
that shows the page's title, its path and "The link is in the main content: “Buy Now”" with an icon for the page
(`simple-w-p-garden-broken-internal-links.png`). Even there the left, leading column is "Address linked to", and the page
with five broken links is repeated five times instead of listed once.

- Code: the fold is `app/src/simple/fixRows.ts:26`; the two cards are `WhereItIs.tsx` shapes "What was found | At
  these addresses" and "Address linked to | On these pages" (`app/src/simple/whereWords.ts:47`).
- Fix: when both checks fire, show one job, "Broken links on your site: 8 links on 4 pages", whose card is grouped by
  the page to edit (section 4). Keep "addresses nothing links to" (in a sitemap only) as a second short list under it.

### S-05 · "Copy the list" · **Blocks** (for handing over) · The copied 404 list has no page that holds each link; the broken-links list has no labels and no link words

Clipboard from the 404 page: the title, the four steps, then eight bare dead URLs under "It is missing (404 Not
Found)". Nothing a developer can act on without crawling the site again. From "Links that lead to broken pages": pairs of
URLs, the dead one then an indented one, with nothing saying which is the page and no "Buy Now". The spreadsheet does hold
it: `Address, What was found, Type, Linked from (pages), Linked from, Listed in sitemaps`, with "Linked from" as one cell,
`https://…/blog/best-rattan-patio-sets/ (content, “Buy Now”)`, so it can't be sorted or filtered by page.

- Code: `app/src/simple/handOver.ts:108-130`.
- Fix: copy what the card shows. For a link check, one block per page: `Page: <url>` then `  Link “Buy Now” (main
  content) → <dead url> (404)`. In the spreadsheet split "Linked from" into "Page that links to it", "Where on the page"
  and "Link text", one row per link.

### S-06 · Simple as a whole · **Blocks** · "What's wrong with this page?" can't be asked

There is no search on What to fix (no input on the screen), and the top bar's search finds checks only: typing "rattan"
gives "Nothing matches that." (`simple-w-43-command-rattan.png`). Nothing lists the worst pages, though
`/blog/best-rattan-patio-sets/` alone holds 5 of the 8 broken links and one blog post holds the three biggest images. An
owner who is about to edit a page can't see everything wrong with it.

- Code: `app/src/simple/WhatToFixScreen.tsx` (no page filter); the palette's sources.
- Fix: a "Pages" part on What to fix or the Summary: "Pages with the most to fix", each row a page with its problems,
  and a search box that takes part of an address or title. Let the palette find pages.

### S-07 · "Pages or files that fail with a server error" · **Slows** · It doesn't say where the address was found

The row is `/product-videos/`, "It’s a page", "Checking it took 0.5 seconds" (`simple-ev-500-where.png`). No "Linked
from". The report's row for the same address says "No page read links to it", which is the useful fact (it must come
from the sitemap), and the app doesn't say it. "Checking it took 0.5 seconds" helps nobody fix a 500.

- Code: `app/src/simple/FoundOnWords.tsx:21-23` returns nothing when there are no sources;
  `app/src/simple/plainEvidence.ts:434`.
- Fix: always say where it came from: "No page links to it. It’s listed in your sitemap." with the sitemap as a link.
  Drop the timing line here.

### S-08 · "Fixed it?" on the two image checks · **Slows** (bug) · The recheck's numbers are wrong

"Images that could be smaller in a newer format": button "Recheck the 1,000 most important pages (about 8 minutes)",
and under it "It rechecks the 1,000 most important of 240. The other 90 stay as the check found them." 1,000 of 240, and
240 − 1,000 is not 90. "Images with no width and height set" says "of 225. The other 166". Shot
`simple-n-a8-legacy-bottom.png`.

- Code: `app/src/changes/recheckWords.ts:104` (and the button at `:91`): `taken` counts addresses (pages and images)
  while `plan.judged` and the noun are pages.
- Fix: count one thing. "Recheck 150 of the 240 pages (about 8 minutes). The other 90 stay as the check found them."

### S-09 · "One fix" strip, and the report · **Slows** · It promises one fix where there isn't one

"Pages missing from your sitemap": "One fix clears all 257 of these pages. It’s on 257 of the 260 pages checked. That’s
almost certainly in the part of the site every page shares. Fix it there once" (`simple-ev-sitemap-page.png`). A sitemap
isn't a part of the page. The report says "probably your site’s template". "Headings that jump levels": "45 of these 81
pages have the same fault, in the part of the site every page shares", while its own rows name a different heading on
each page (“Is delivery free?”, “Gillman Metal Shed”, “Skyvault Greenhouse”).

- Code: `app/src/simple/ProblemScreen.tsx:462` (the fallback sentence),
  `engine/src/ShoutingIguana.Engine/Reporting/ReportDocument.cs:782`.
- Fix: say "shared part of the site" only when the evidence is one element (same link, same image file, same header
  region). For a sitemap: "Nearly every page is missing, so the sitemap itself is the fault: it may be out of date or
  not the one your site makes now." For same-kind faults on different elements, say nothing.

### S-10 · Switching mode, Back · **Slows** · My place isn't kept

- What to fix → Advanced → Simple lands on the 404's problem page (`/issues/internal-client-error?group=…`), not the
  list: Advanced selects the first issue and Simple shows that selection as a page. With S-01 there is then no link back
  to the list.
- A problem → "See it in Advanced" lands on the same check, selected (good; `simple-w-63-see-in-advanced.png`). Then the
  toolbar's Back goes to the Summary in Advanced (`simple-w-64-back-after-advanced.png`): the switch adds no history, and
  nothing on the Advanced screen says "Back to Simple".
- What to fix with "Show all 22 things worth a look" open and scrolled to the bottom → a problem → toolbar Back: the
  list is folded to three again and the scroll is elsewhere (1702 → 368).
- Advanced shows the check as "Internal URL returns 4xx" with no trace of "Pages or files that can't be found".
- Code: `app/src/mode/switchMode.ts:12-24`, `app/src/simple/ProblemScreen.tsx:197-204`,
  `app/src/simple/scrollPlace.ts`, the "Show all" state in `WhatToFixScreen.tsx`.
- Fix: "See it in Advanced" navigates (so Back returns to the plain page and to Simple); the switch from What to fix
  remembers it came from the list; keep the expansion in the URL or session; show the plain title as a subtitle in
  Advanced while it was reached from Simple.

### S-11 · Report · **Slows** · Its links are dead in the app, and its rows speak engine

- The report's 134 addresses are real links (`target="_blank"`) and both the dead address and the page that holds it
  are linked, which is better than the problem's page. Inside the app, clicking one did nothing: no window, no
  navigation. The frame is `sandbox="allow-same-origin allow-modals"` and no click is handled.
- "Fix these first" in the report isn't linked to its sections in a 17,700 px document. No problem links to its page in
  the app.
- The client reads "404 Not Found · Type: Page", "1 broken URL", "(404; not indexable; content, “Buy Now”)", "H1 → H4",
  "Empty og:title, og:description", "Inlinks: 1 · Linking pages: 1", "Links to 5 URLs", "(200; header)", and under every
  plain title the engine's "Internal URL returns 4xx · Error". The app's own cards say the same in plain words.
- The Summary page says "2 problems fixed, 2 on more."
- At 2560 the sheet is 760 px wide at 12 to 13 px type with about 700 px blank either side.
- Shots `simple-w-50-report.png`, `simple-w-51-report-404-rows.png`, `simple-w-52-report-summary-page.png`.
- Code: `app/src/screens/report/ReportFrame.tsx:63`,
  `engine/src/ShoutingIguana.Engine/Reporting/ReportDocument.cs` (`:383` "on more", `:825`, `:907`, `:919`).
- Fix: handle clicks in the frame (open outside addresses in the browser; send a problem's heading to its page); link
  the numbered list to the sections; build the rows from the same plain words as `plainEvidence.ts`; "2 got worse";
  let the preview zoom to the pane.

### S-12 · Summary, What to fix · **Slows** · What got worse since the last check isn't shown

Summary: "Since your last check on 17 Nov: score 83 → 85, 2 problems fixed. See what changed". "See what changed" goes
to the top of What to fix with nothing marked (`simple-w-21-see-what-changed.png`). "Pages missing from your sitemap"
went from 18 pages to 257 and shows it as grey "18 on 17 Nov", in the same style as "same as 17 Nov" on every other
row. The report has a "More 2" block for exactly this; Advanced has New/Fixed/Fewer/More filters; Simple has only
"Fixed since".

- Code: `app/src/simple/SummaryChange.tsx:71-96`, `app/src/simple/WhatToFixScreen.tsx` (the row's last line).
- Fix: the sentence names it ("2 fixed, 2 got worse"), each part a link; on a row, "▲ 239 more than on 17 Nov" in the
  warning colour, and nothing at all when it is the same; "See what changed" filters the list to what moved.

### S-13 · Problem pages for a title or description · **Slows** · It doesn't show what to change, only that something is too long

"Search descriptions that may be cut off": `Search description: “Wide selection of fantastic affordable garden sheds
and storage boxes…”` cut at about 70 characters, with no way to open the rest, no length, and no count of how far over
(`simple-n-a7-desc-scroll1.png`). "Titles that may be cut off" gives no length. "Very short titles" says "shorter than
about 20 characters" and then lists "Furniture Accessories. Only 21 characters long" (`simple-ev-titleshort-page.png`).

- Code: `app/src/simple/plainEvidence.ts:448`,
  `engine/src/ShoutingIguana.Engine/Analysis/IssuePlainTexts.cs:1261` (the limit is in pixels, the row in characters).
- Fix: show the whole text with the part Google cuts greyed out, and "212 characters, about 52 too many". For short
  titles say the limit the way the row measures it.

### S-14 · Problem pages · **Slows** · The list and "Fixed it?" are far from where you look

At 1280 × 720 the first screen of every problem is the explanation; "Where it is" starts at y = 484 to 716 of 720
(`simple-n-a4-404.png`). "Fixed it?" is under the list: 4,300 px down on the image checks, 6,000 px on "Images with no
width and height set". Long lists grow 25 at a time ("Showing 25 of 1,420 images. Show 25 more": 56 clicks), with no
filter. Groups show three pages then "And 192 more. Show them".

- Code: `app/src/simple/ProblemScreen.tsx` (order of the cards), `app/src/simple/WhereItIs.tsx:43-46, 366, 606`.
- Fix: put a one-line count with "Jump to the list" and "Fixed it? Recheck" in the header beside the title; fold "What's
  wrong / How to fix it" to its first sentences once the list is long; give the list a filter box and "Show all".

### S-15 · "Fixed it?" results · **Slows** · The result words are hard to read, and one looks wrong

- "It couldn’t finish: 0 of 1 rechecked. 0 fixed. Score 83 → 85. Rechecked yesterday at 06:33."
  (`simple-ev-robots-fixedit.png`): it failed, fixed nothing, and the score went up. The 404's card for the same minute
  says "Score 83 → 83". One of them is wrong or they mean different things.
- "None of the 8 is fixed yet", then five addresses and no "and 3 more". "None of the 2 is fixed yet" for "Neither".
- "Recheck…" starts at once; there is nothing to read first or cancel, which is why it wasn't pressed in this audit.
- "Check again" on the Summary opens the general "Check a website" form: the site's name and sidebar are gone, nothing
  says it will be compared with this check or that it takes about 36 minutes, and it adds "2 Advanced settings are
  changed: Robots and sitemaps and Advanced." (`simple-w-25-check-again-form.png`).
- Code: `app/src/changes/recheckWords.ts:218-241`, `app/src/simple/RecheckCard.tsx:346-408`,
  `app/src/screens/NewCrawlScreen.tsx:425`.
- Fix: "The recheck didn't finish, so nothing changed. Try again." with no score line; list all or say how many more;
  title the form "Check gardenfurnituresets.co.uk again" with the time and "you'll see what changed".

### S-16 · Security and robots rows · **Slows** · The row a developer needs doesn't name the thing

"Recommended security settings are missing" tells you to "Send this to your developer… the list names the missing
settings". The list says "Your site doesn’t send the security settings that limit what the pages may load and limit what
other sites learn of where visitors came from" (`simple-ev-secheaders-page.png`): no header names. "Your rules file
doesn't say where your sitemap is": the "What was found" cell is empty and the line to add isn't given.

- Code: `engine/src/ShoutingIguana.Engine/Analysis/IssuePlainTexts.cs:2073`, `app/src/simple/plainDetails.ts`.
- Fix: plain words, then the name in brackets: "…what the pages may load (Content-Security-Policy)". For robots.txt
  show the line: `Sitemap: https://www.gardenfurnituresets.co.uk/sitemap.xml`, with a copy button.

### S-17 · "Where it is" cards · **Polish** · Lines that say nothing

Seen on nearly every card: "Still there since 17 Nov" on every row when every row is the same; "It’s a page"; "1 kind
of problem on 8 addresses" and the footer "The one kind of problem is shown."; "1 link to it" and "1 page links here"
under "Only 1 page links here…" (`simple-ev-inlinks-page.png`); "2 clicks from the home page" as the only finding on
sitemap rows, and blank on the first; "loads when scrolled to" on large images; "Can appear in search results" on
duplicate pages. The Summary says "23 checks couldn’t run" four times, twice in one card
(`simple-w-03-summary.png`).

- Code: `app/src/changes/changeWords.ts:326`, `app/src/simple/plainEvidence.ts:386-434`,
  `app/src/simple/whereWords.ts:47, 260`, `app/src/simple/SummaryScreen.tsx`.
- Fix: say "since" only on rows that are new or changed; drop the kind and the footer when there is one kind; keep one
  fact per row that helps find or fix it.

### S-18 · "Where it is" cards · **Polish** · Columns squeezed and wrapped beside blank space

"Addresses that forward more than once" (synthetic): the left column is about 80 px, so `/plants/thin` breaks as
"/plants/th / in" and "It forwards (301 Moved Permanently)." runs over four lines, in a 2560 px window with 700 px blank
on each side (`simple-w-p-synth-redirect-chain.png`). On garden, `/patio-swivel-chair-set-with-coffee-table-soft-seat-
cushions/` wraps in the left column. The column is headed "What it’s about" or "What was found" and holds a bare address
(the final destination), and the count reads "2 addresses on 3 addresses". Image paths split oddly: "/media/6965/
?width=896".

- Code: `app/src/styles/simple-results.css:316-339` (auto table, `overflow-wrap: anywhere`), `si-simple-page` at `:184`,
  `app/src/simple/whereWords.ts`.
- Fix: fixed column shares (40/60) with a minimum; let the page grow to about 1,400 px on a wide window; head the
  column "Where they end up"; "3 addresses that end at 2 pages".

### S-19 · What to fix, problem header, score dialog · **Polish** · Three different numbers for one job's points

List: "8 addresses · 6 points back". The page: "Takes 2 points off the score" and under it "…4 more points back". The
score dialog: −2 and −4 on two lines. All true, but the owner sees 6, 2 and 4 for the thing they clicked. "8 of 4,362
addresses" also puts a figure on screen (4,362) that means nothing to them.

- Code: `app/src/simple/plainWords.ts:99`, `app/src/simple/ProblemScreen.tsx:235-260`.
- Fix: on the page, "Fixing this gives 6 points back (2 for these, 4 for the links to them)". Say "8 addresses" alone.

### S-20 · Everywhere a date shows · **Polish** · Two checks on one day can't be told apart

This check is "17 Nov 2026, 06:49"; the one it's compared with reads the same. So rows say "same as 17 Nov" on a check
dated 17 Nov, the sidebar's switcher lists the site twice with identical lines (`simple-w-41-site-switcher.png`), and
Home's "1 earlier check" opens a row with the same date. The data here is a copy, so the identical time may be the
copy's doing; the wording problem is real whenever two checks share a day.

- Code: `app/src/simple/dates.ts`, `app/src/changes/changeWords.ts`.
- Fix: say "the last check" or "the check before" in rows; add the time when the day is the same.

### S-21 · Small things

- Home: the whole row opens the site (good), but "3 serious", "4 worth fixing" and "2 fixed since 17 Nov" don't go to
  those parts. The row menu's "Update the results" doesn't say what it does. `app/src/simple/home/SiteRow.tsx:129`.
- "Save as a spreadsheet" opens "Choose what to export, as what and where." In the browser build "Save as" is a path
  box; the app has its own dialog (not tested).
- "Pages that won't preview well when shared" folds a group to names ("Garden Furniture, Frequently Asked Questions,
  Privacy Policy and 4 more. Show them"): the names aren't links. `simple-ev-og-page.png`.
- jitsunami "Addresses that search engines are told not to visit": the rule is the group's heading and again under each address.
  `simple-ev-jits-robots.png`.
- "How the score is worked out" is clear; its second table ("Points for each problem") is more than an owner needs.

## 3. The garden crawl, problem by problem

"Open" means the hover-only icon (S-03). Nothing else in any card is clickable.

| Problem | "Where it is" leads with | Clickable | Missing | Verdict |
| --- | --- | --- | --- | --- |
| Pages or files that can't be found (8) | The dead address; "Linked from…" as text; "It’s a page"; 3 shown, "And 5 more. Show them" | Open the dead address | The page to edit as a control; 5 of 8 hidden | Fails the owner's test (S-02) |
| Links that lead to broken pages (4 pages) | Dead address left, page title + path + link words right | Open both | Grouping by page (one page ×5); folded under the job above | Best card in Simple; wrong way round |
| Pages or files that fail with a server error (1) | The address, "It’s a page", "Checking it took 0.5 seconds" | Open it | Where it was found | S-07 |
| Large images that slow pages down (129 images, 60 pages) | Thumbnail, file name, size; page title + path, "Shown at 1456×816 pixels" | Open image, open page | Target size in words ("make it 1456 px wide, under 200 KB"); filter by page | Good evidence; 25 at a time |
| Later pages of a list that point Google at the first page (3) | The first page left, page 2 right | Open both | Nothing | Clear |
| Pages that share the same title (2) | “Login”, then the two pages | Open both | Nothing | Clear; "None of the 2" |
| Links to other websites that don't work (1) | The outside address, "It couldn’t be reached."; page + link words | Open both | Grouping by page | Clear |
| Links with nothing to read (260 pages) | "Your home page /"; 260 pages, 3 shown | Open | "One fix" strip is right and useful | Good |
| Pages missing from your sitemap (257) | Page title + path; "2 clicks from the home page" | Open page | Why 18 became 257; the sitemap itself as a link | Misleading "one fix" (S-09) |
| Images that could be smaller in a newer format (1,420 images) | Image, size; "10 pages", 3 shown | Open | A way to see it by page or folder | Too long to use; wrong recheck numbers (S-08) |
| Images with no width and height set (3,661 images) | Image; "195 pages One fix" | Open | Same | 6,100 px page |
| Search descriptions that may be cut off (163) | Page; "Google will likely cut this description off after “…”"; description cut at 70 characters | Open page | The whole text, its length | S-13 |
| Headings that jump levels (81) | The kind of jump; pages with the heading's words | Open page | Nothing | Good rows; false "one fix" (S-09) |
| Titles that may be cut off (69) | Page title; where Google cuts it | Open page | Length | Good |
| Headings that run on (52) | Page; "The main heading is 87 characters long, more than 70: “…”" | Open page | Nothing | Good |
| Pages with no search description (12) | Page | Open page | Nothing | Clear |
| Pages that won't preview well when shared (12) | The kind; "7 pages: names… and 4 more. Show them" | Nothing until "Show them" | Names as links | Folded too far |
| Pages only linked from pages Google won't show (9) | Page; "Linked from /blog/?p=2 (…)" as text; three lines saying one thing | Open page | The linking page as a control | S-02 again, S-17 |
| Pages with very little text (9) | Page; "Only 121 words, fewer than 200" | Open page | Nothing | Clear |
| Pages that don't name their main address (8) | Page | Open page | Nothing | Clear |
| Very short titles (8) | Page; "Only 16 characters long" | Open page | A limit that matches the rows | S-13 |
| Pages that share the same search description (4) | The shared text; pages, each repeating the text cut short | Open page | Nothing | Repeats itself |
| Cookies without basic protection (3) | "The cookie “ASP.NET_SessionId” can be sent without the secure connection"; pages | Open page | Which is new since 17 Nov (was 2) | Clear for a developer |
| Pages without a preview card for X (3) | Page | Open page | Nothing | Clear |
| Your rules file doesn't say where your sitemap is | "Your rules file (robots.txt) /robots.txt"; empty finding | Open file | The line to add | S-16 |
| Pages with no main heading (1) | "No main heading"; the page | Open page | Nothing | Clear |
| Recommended security settings are missing | Home page; a sentence with no names | Open page | Header names | S-16 |
| Security settings that are on but too weak | "…for 4 months; a year is advised" | Open page | The setting's name | Nearly there |
| Pages whose address has a ? part (1) | Page; "Linked from /login/ (…)" as text | Open page | The linking page as a control | S-02 again |

Other shapes seen on the synthetic crawl and jitsunami (18 column pairs in all): "Main address they name | On these
pages", "Language version | On these pages", "Version that doesn’t link back | Pages it should link back to", "File |
On these pages", "Address | What was found", "What it’s about | At these addresses". Same faults throughout: pages named
in "Linked from", "Named as the main address by" and "Shown on" lines are text. jitsunami's image 404 says "Shown on
/collections/belts (in the main content: “Collection page banner”)", which is the right fact, as text.

## 4. A broken link: what the card should lead with

Lead with the page to edit, then the link's words, then where it goes. The owner's job is "open this page in my
editor, find the link that says X, change it". The dead address only tells five identical "Buy Now" links apart.

```
Where to fix it      8 broken links on 4 pages                 [ Copy the list ] [ Save as a spreadsheet ]

Best Rattan Patio Sets - Hand Picked by Experts        5 broken links               [ Open page ↗ ]
/blog/best-rattan-patio-sets/
   The link that says      In               It leads to                             
   “Buy Now”               main content     /3-pieces-patio-furniture-set/       missing (404)   [ ↗ ]
   “Buy Now”               main content     /henbury-6-seater-dining-set/        missing (404)   [ ↗ ]
   “Buy Now”               main content     /patio-swivel-chair-set-with-…/      missing (404)   [ ↗ ]
   “Buy Now”               main content     /wentworth-cube-dining-set/          missing (404)   [ ↗ ]
   “Buy Now”               main content     /wentworth-imperial-bistro-set/      missing (404)   [ ↗ ]

Gas Fire Pit Rattan Table & Chairs | Rattan Dining     1 broken link                [ Open page ↗ ]
/rattan-furniture/rattan-fire-pit-sets/
   “deluxe 7-piece outdoor furniture set”   main content   /outdoor-pe-rattan-dining-set-with-fire-pit-table/   missing (404)   [ ↗ ]
```

- Pages sorted by most broken links. The page's title and path are one link; "Open page" is always visible.
- A link in the header or footer of many pages collapses to one row: "In the footer of 212 pages. Fix it once in your
  site's design", with "Show the pages".
- An address nothing links to (found only in the sitemap) goes in a second list: "Missing, and still in your sitemap".
- The card's title is "Where to fix it" (the report already uses those words for its column).
- "Copy the list" gives the same blocks as text; the spreadsheet gives one row per link with the page first.

## 5. Words

| On screen | Why it jars | Better |
| --- | --- | --- |
| "8 addresses", "8 of 4,362 addresses" | "Address" is fine; 4,362 means nothing | "8 missing pages or files" |
| "6 points back" | Back to where? | "+6 to your score" |
| "Worth a look", "Things worth a look" | Fine as a level; odd as a count beside "problems" | Keep, but "22 to look at when you have time" on the Summary |
| "same as 17 Nov", "18 on 17 Nov", "Still there since 17 Nov" | Ambiguous, and on every row | "no change", "▲ 239 more than last time"; nothing when unchanged |
| "1 kind of problem on 8 addresses", "The one kind of problem is shown." | Says nothing | Drop |
| "2 addresses on 3 addresses" | Nonsense | "3 addresses that end at 2 pages" |
| "It’s a page", "It forwards to another page" | Says nothing | Drop |
| "Checking it took 0.5 seconds" | Not about the fault | Drop |
| "What it’s about" (column) | About what? It holds an address | "Where they end up", "The pages that match" |
| "At these addresses" / "On these pages" / "Address linked to" | Three names for "where" | "Page to edit", "It leads to" |
| "Your rules file", "rules file for search engines" | Nobody calls it that; robots.txt is what a developer will ask for | "your robots.txt file (the rules for search engines)" |
| "names the list's first page as its main version", "main address" | Needs decoding | "tells Google the first page is the real one" |
| "forward", "forwarding address" | Understandable; "redirect" is what every editor's settings say | "redirect (forward)" on first use |
| "loads when scrolled to" | Jargon turned into a riddle | Drop in Simple |
| "can be sent without the secure connection" | Which connection? | "can be sent without HTTPS" |
| "extras in search results" (What's fine) | Unclear | "star ratings and prices in search results" |
| "It couldn’t finish: 0 of 1 rechecked. 0 fixed." | Three ways of saying nothing happened | "The recheck didn't finish. Nothing changed." |
| "None of the 2 is fixed yet" | Grammar | "Neither is fixed yet" |
| "It rechecks the 1,000 most important of 240" | Wrong (S-08) | |
| "2 problems fixed, 2 on more." (report) | Garbled | "2 fixed, 2 got worse" |
| "Fixed along with the problem above." | Which fixes which? | "Fixing the links on these 4 pages clears both" |
| "One fix" (badge) | Reads as a count | "Fix once" |
| "Update the results" (Home menu) | Of what, how? | "Work out the results again with this version" |
| "2 Advanced settings are changed: Robots and sitemaps and Advanced." | "Advanced … and Advanced" | "2 settings from Advanced are in use" |
| "Choose what to export, as what and where." | A puzzle | "Save the list as a spreadsheet" |
| "This site check was made with an older version of Shouting Iguana, which didn't save what this check compares." | "this check" is two things | "23 checks need the site read again by this version" |
| "The score covers all 260 pages and 147 of 170 checks. 23 checks couldn’t run." | Said four times on the Summary | Once |
| "Internal URL returns 4xx · Error" (report, under each plain title) | The client's document | Move to the "words in this report" note |
| "content", "not indexable", "Inlinks", "H1 → H4", "og:title" (report rows) | Engine words in the plain report | The app's plain sentences |

## 6. Not tested, and why

- **Running a recheck or a check**: forbidden on real sites. "Recheck…" has no dialog to open and cancel, so its
  running, stopping and result states were read from past results only (garden has three).
- **The app's own window**: the native save dialog, opening a link in the system browser, printing and "Save as PDF".
  The driver is headless Chromium against the dev server, where `window.open` was trapped and "Save as" is a path box.
- **Whether the report's links work in the packaged app**: in the browser they are dead; the code has no handler, so
  the same is expected, but it wasn't seen there.
- **A check in progress, a stopped check, a first run with no sites, the Shopify prompt**: no such state in the copies.
- **Dark theme, compact density, keyboard-only use, a screen reader, 200% zoom.**
- **aptitude.co.uk** (`4950b1d1228b1373`): not opened; the three other crawls covered every card shape.
- **Settings**: read only (General). The owner's rule is that Settings is full in both modes, so its Advanced wording
  wasn't judged. Nothing was changed.
- **"Delete file…" and "Remove from Your sites"** on Home: seen in the menu, not pressed.
- The synthetic crawl's 150 problem pages were walked and their text saved, but only about 20 were read closely.
