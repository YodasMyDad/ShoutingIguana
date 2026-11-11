# Step 3: Simple mode

The design of Simple: what it shows, its words, the score, the report, the switch, and what New crawl and Settings
look like in each mode. It carries out [Simple first](v2-rebuild.md#simple-first-advanced-on-a-switch-2026-11-10) and
sits beside [the IA](v2-ui-ia.md), which describes Advanced. The bar and the states matrix are
[the UI brief's](v2-ui-brief.md), the look is [clarity's](v2-ui-design.md), and the words follow
[the glossary](v2-ui-discovery.md#glossary).

- **Mockups:** `app/design/simple/` (its [README](../app/design/simple/README.md) lists them), static HTML on the
  garden crawl's real numbers, in both themes. The score's reference code is `build/lib/score.mjs` there, and every
  plain text the mockups show is in `build/lib/words.mjs`.
- **Read with:** the comparison with Screaming Frog and Sitebulb (`docs/v2-comparison.md`, being written beside
  this), section "For Simple mode". This design takes its five points: two plain sentences and a third for what to
  do, a count and a share on every item, things to know kept apart from things to fix, five fixes first, and a
  score that says what it is built from.
- **Decided on 2026-11-11**, under the owner's standing delegation. Each decision below has its reason in a line.
  Three are the owner's to overturn: [For the owner](#for-the-owner).

The garden crawl, analysed by today's engine, is the example throughout: 265 pages and 4,521 URLs; 82 of 86 checks
ran and 4 were skipped; 19 found 933 issues (14 from 2 Error checks, 78 from 4 Warning checks, 841 from 13 Notice
checks); 63 passed.

## One app, two views

Simple is a view over the same crawl, the same checks and the same numbers. Nothing in it is computed apart from
Advanced: every figure is a count Advanced can list the URLs for, and every figure in Simple opens the pages behind
it.

| | Simple | Advanced |
| --- | --- | --- |
| Who | Anyone: a shop owner, a marketing manager | A technical SEO, an agency |
| A crawl opens on | Summary: a score, one sentence, five fixes | Overview, as built |
| The checks | What to fix: problems in plain words, with who usually fixes each | Issues, as built |
| Detail | The pages behind each problem, as a list to send on | URLs, Links, Sitemaps, robots.txt, the inspector, filters and columns |
| A new crawl | An address; speed, a limit and two switches if wanted | Every crawl setting |
| Hand over | The report, and a spreadsheet | The report, and every grid's export |

## The switch

**Mode is an app setting, not part of a route.** It is remembered across restarts, it is app-wide and not per crawl,
and a first start is Simple. Why: a route that carried the mode would make every link, back step and reopened
session decide the mode again, and the two views would drift into two apps.

- **Where.** A two-part control, **Simple | Advanced**, at the right-hand end of the top bar on every screen, in
  both modes. Why the top bar: it is the only chrome that is the same on every screen and at every width. The
  sidebar becomes icons below 1024 px, where a two-word control can't keep its form.
- **It never moves, shrinks or hides.** Everything else in the top bar gives way first, in a fixed order
  ([Top bar](#top-bar)). Mocked at 1280×720, at 900 px with the sidebar as icons, and at 200% zoom, in both modes and
  with a crawl running.
- **Also reachable** as Settings > General > Mode (the same setting, with a line on what each is), in the palette
  ("Switch to Advanced", "Switch to Simple") and on Ctrl+Shift+A.
- **Switching keeps the screen.** The same route renders its other view: a problem in Simple is that check in
  Advanced, the Summary is Overview, the report is the report. Search params the other view doesn't use (Issues'
  `group`, `severity`, `inspect`) are kept in the address and ignored, so switching back restores them.
- **From an Advanced-only screen** (URLs, Links, Sitemaps, robots.txt, the crawl's settings, an Advanced-only
  Settings section), switching to Simple goes to the nearest screen Simple has: the crawl's Summary, or Settings >
  General. It is a navigation, so Back returns.
- **Simple never leads to an Advanced-only screen by surprise.** Simple's own links to one always say so ("See it
  in Advanced", "Open in Advanced", "Show them in Advanced"). Arriving at an Advanced-only screen, by such a link,
  by Back or Forward, or by a reopened session, **turns Advanced on**: the switch shows it, and a toast says
  "Advanced is on" with **Back to Simple**. Why not a page that says "this is an Advanced screen": only someone
  who uses both modes can get there, and it would cost them a click every time.
- **The palette** lists the current mode's screens and actions. In Simple it lists no Advanced-only command except
  "Switch to Advanced". It finds a check by either of its titles in both modes, and shows the mode's own.

### Routes

| Route | Simple | Advanced |
| --- | --- | --- |
| `/` | Home: check a website, your sites | Home, as built |
| `/new?url=` | Check a website: 4 questions | New crawl: every setting |
| `/crawls/:crawl/overview` | Checking, then the Summary | Live crawl, then the answer |
| `/crawls/:crawl/overview?score=1` | How the score is worked out, over the Summary | The same breakdown, over Overview |
| `/crawls/:crawl/issues` | What to fix | Issues, the first check to fix selected |
| `/crawls/:crawl/issues/:check` | One problem | Issues, with that check selected |
| `/crawls/:crawl/report` | Report. Param: `cut` (`full`, `summary`) | Report, the same screen |
| `/crawls/:crawl/urls`, `/links`, `/sitemaps/:sitemap?`, `/robots/:origin?`, `/settings` | None: Advanced only | As built |
| `/settings/:section?` | General, Data folder, Updates, About | All eight sections |

`/crawls/:crawl` goes to the crawl's last view in the current mode: the Summary the first time in Simple.

## The shell in Simple

The same shell: sidebar, top bar, main area. No inspector in Simple: a page's detail is Advanced.

- **Sidebar.** Home; the open site, with when it was checked; its three sections, **Summary**, **What to fix** and
  **Report**; at the bottom, "Checking now" (crawls running) and Settings. What to fix carries a count: the
  problems that are Serious or Worth fixing (6 on the garden crawl). Why not all 19: a site with nothing but things
  worth a look would show a to-do count it can't clear.
- **Advanced's sidebar gains Report** as an eighth section, after a rule, below the crawl's Settings. Why last and
  not beside Issues: Ctrl+1 to Ctrl+7 are built and stay as they are.
- **Pause, Stop and Continue stay in the top bar only**, with their words, as Advanced decided. Why: one place, on
  every screen. The checking screen says where they are.

### Top bar

Back and Forward; where you are (Home, the site, the crawl's date, the section); the status pill; the crawl's
controls; search; the switch. In Simple, search is its icon: Ctrl+K and the palette work as everywhere.

The status pill says **Checking**, Paused, **Stopped early**, **Working out the results** or Done in Simple, where
Advanced says Running, Paused, Stopped, Analysing and Done.

As the bar runs out of room, these give way in this order. The measure is the bar's own width, not the window's, so
the order holds whatever the sidebar is doing.

1. The keys inside buttons (F7, F8).
2. The crawl's date.
3. The search field becomes its icon.
4. Home.
5. The count in the status pill.
6. Pause, Stop and Continue become icons, with their names as tooltips and accessible names.
7. The site.
8. Forward.

Never: Back, the section you are on, the status word, the controls, search (as its icon) and the switch. At 200%
zoom on a 1280 px window (584 px of bar) a running crawl shows exactly those. This also settles the shell pass's
open finding, the search field shrinking to one letter beside Pause and Stop.

## Simple's words

Simple has its own word wherever Advanced's is technical. Each maps to one glossary term, one to one, and is in
[the glossary](v2-ui-discovery.md#simple-and-advanced).

| Simple says | Advanced says |
| --- | --- |
| Check a site, a site check, Check again | Crawl, a crawl, Crawl again |
| Address, web address | URL |
| Page | Page |
| Your sites | Library |
| Summary | Overview |
| What to fix | Issues |
| Problem (Serious or Worth fixing); thing worth a look | A check with issues |
| **Serious**, **Worth fixing**, **Worth a look** | Error, Warning, Notice |
| Nothing wrong | Passed |
| Couldn't check | Skipped |
| Working out the results | Analysis, analysing |
| Can appear in search results | Indexable |

- **Severity keeps one meaning.** The three words map one to one, with the same shapes and colours: the red
  octagon, the amber triangle, the quiet circle. A count takes the form "2 serious problems", "4 problems worth
  fixing", "13 things worth a look". Why not High, Medium and Low: the glossary rules out "priority" and "level"
  for severity, and "Worth a look" says what a Notice is: often fine.
- **Simple never shows the issue count** (933). It counts problems (checks that found something) and, for each,
  the pages or addresses affected and what they are a share of: "5 of 265 pages". Why: 841 of the 933 are notices,
  and a total of issues says nothing a non-expert can act on.
- **"Pages" or "addresses"** follows the check's `subject`: a check that can flag files and redirects counts
  addresses, of the addresses checked.
- **The app is "Shouting Iguana" or "it", never "we".** As the built screens say: "It crawls with your default
  settings".

## Home

`home.html`, `home-first.html`.

- **Check a website.** One field ("www.example.co.uk"), **Check site** (Enter) and **Options…**. Under it, one line
  saying what a bare Enter does: "Checks the whole site, at a speed that's safe for most hosting." If the saved
  defaults differ from the engine's, the line says so instead: "Uses your saved settings: Polite speed, up to
  5,000 addresses, and 3 more set in Advanced."
- **Simple asks nothing before a crawl.** Why: a non-expert can answer one question, the address. Options… opens
  [New crawl](#new-crawl-in-each-mode) for the three more they might.
- **Your sites.** The library in plain terms, a row a site, the newest check first: the site and its pages; the
  score, as a small ring with its word; what needs fixing ("2 serious, 4 worth fixing", or "Nothing to fix"); when
  it was last checked. Older checks of a site fold under it ("2 earlier checks"). A row opens the Summary.
  - A check running now is pinned first, with its progress and "Results appear when it finishes."
  - A check stopped early shows its score as a dashed ring, "so far", and **Continue**.
  - A check never analysed shows no score and **Work out the results**. A file that has moved shows "File not
    found" and "Locate the file…", as in Advanced.
  - Row actions are Advanced's, in Simple's words: Open, Check again, Continue, Open the report, Show in folder,
    Remove from your sites (undo), Delete file….
- **First start, no crawls.** The field, then three steps (paste the address; it reads your site, and you can
  close the window; you get a score, a to-do list and a report), and two quiet lines: "Want every URL, filter and
  setting? Switch to Advanced, at the top right" and "Have a crawl file already? Open it, or drop it here."
- Ctrl+O and drag-and-drop open a `.sicrawl` in both modes.

## While it checks the site

`checking.html`. The same route as the Summary, as Overview's two states are one route.

- **Three steps**, the current one marked: Reading your site; Working out the results; Your score, what to fix and
  the report.
- **Three figures:** addresses checked, pages read, still to check. Under them one bar: checked, and still to
  check. No rate chart and no status codes: they are Advanced's.
- **How long.** "At least 12 minutes to go. It's still finding new pages, so this can grow." The figure is what is
  queued over the current pace, said as a floor. Why no percentage: the total isn't known until the end, and a bar
  that ran backwards would be a lie.
- **Can I close the window?** The header says so: "You can close this window: the check carries on, and a
  notification tells you when it's done."
- **Is anything in the way?** A card that says "Nothing so far" until the engine raises an alert, then says it in
  plain words with what to do:

  | The engine's alert | Simple says | It offers |
  | --- | --- | --- |
  | The site is slow, and the crawl backed off | "The site is answering slowly, so the check has slowed down to match. It will take longer." | Nothing to do |
  | A firewall or rate limit refused the crawler | "The site is turning the check away: its security takes it for a robot. 312 pages were read first." | Continue more gently (Polite); "What to tell whoever runs the site" (text to copy); More ways in Advanced |
  | robots.txt can't be read | "The site's rules file for search engines isn't answering, so the check can't tell where it may go. It's waiting, and will try again." | Stop |
  | The site stopped answering | "The site has stopped answering. The check is waiting, and will carry on by itself when it's back." | Stop |
  | A Crawl-delay is being obeyed | "The site asks visitors like this one to go slowly, so the check waits between pages." | Nothing to do |
  | The start URL leads to another site | "This address forwards to another site, example.org." | Check example.org instead |

- **Pause and Stop** are in the top bar. The page says what each does: "Pause holds the check. Stop ends it and
  keeps what it found, and you can continue it later."
- **Paused:** the first step reads "Paused", and a banner says "Paused. Nothing is lost." with Resume.
- **Working out the results:** the second step is current, with the analysis's real progress and phase, and
  Cancel. The three figures stay.
- **When it ends**, the screen becomes the Summary in place. See [the report at the end](#the-report-at-the-end).

## The Summary

`summary.html`, `summary-perfect.html`, `summary-stopped.html`.

Top to bottom:

1. **The site and when**, with **Check again** and **Open the report**, the primary action.
2. **The score**: a ring with the number, its word (Very good, Good, Needs work, Poor), one plain sentence, what
   the score covers, and "How the score is worked out". Beside it, five counts, each a link to its part of What to
   fix: serious problems, problems worth fixing, things worth a look, checks that found nothing wrong, checks that
   couldn't run.
3. **Fix these first**: up to five problems, Serious and Worth fixing only, in the engine's "Fix first" order
   (severity; within a severity a site-wide fault first, however small its count; then the most affected), which
   Advanced's "Fix first" uses too, so the two modes agree. Each row: the mark, the plain title, the first
   sentence of what's wrong, and its count and share. Then one sentence, "Fixing these five takes the score from
   85 to 98", and a link to the whole list. Why five: the comparison's "five things first"; a first screen that
   lists every check that fired is what Simple exists to avoid.
4. **What's fine**: "63 of 82 checks found nothing wrong", and the categories with nothing found, in plain words.
5. **Couldn't be checked**: each skipped check or pair, with its plain reason, and what would let it run.
6. **The report**: what it is, Open the report, Save as PDF.
7. **One line of facts**: pages read, addresses checked, when.

The sentence is built from counts only:

| The site | The sentence |
| --- | --- |
| Problems, score 90 or more | "Your site is in very good shape. There is 1 problem worth fixing." |
| Score 70 to 89 | "Your site is in good shape. There are 2 serious problems and 4 problems worth fixing." |
| Score 50 to 69 | "Your site needs some work. There are …" |
| Score under 50 | "Your site has problems that will hold it back in search. There are …" |
| Only things worth a look | "Nothing needs fixing. 3 things are worth a look when you have a moment." |
| Nothing found at all | "Nothing to fix. Every check that ran found nothing wrong." |
| A site-wide Serious problem | That problem's plain title leads: "Your site isn't secure (no HTTPS). That holds everything else back." |
| Stopped early | "On the pages read so far there are …" |

### Every state

| State | The Summary shows |
| --- | --- |
| First load | A skeleton in the screen's shape: the ring's outline, three lines, five rows. After 150 ms, for at least 300 ms |
| Checking, paused | [While it checks](#while-it-checks-the-site) |
| Working out the results | The same screen on its second step, with progress and Cancel |
| Done | The Summary |
| Nothing to fix | Score 100, "Nothing to fix" in place of the list, with the things worth a look under it, and when to check again |
| Never analysed | No score: a dashed ring, "No score yet". "The results haven't been worked out for this check." with **Work out the results**. The line of facts still shows |
| Out of date | A banner above the score, with **Update the results**. Its reason in plain words: "The site check was continued after these results", "Shouting Iguana's checks have been updated since these results" or "The checks' limits were changed in Advanced since these results". The score and lists stay |
| Stopped early | A banner: "This check was stopped early. It read 312 pages and still had 1,380 addresses to check. Everything below covers only what it read", with **Continue checking**. The ring is dashed and says "so far"; so does its word |
| Stopped at its limit | A banner: "This check stopped at its limit of 5,000 addresses. Everything below covers what it read", with Check again without a limit |
| Blocked part way | A banner: "The site turned the check away after 312 pages, so the rest wasn't read", with the alert's actions |
| Failed | "The site check couldn't finish", the plain cause, **Try again**, and Details (copyable). What was read is kept and shown as a check stopped early |
| The results couldn't be worked out | "The results couldn't be worked out", Try again, Details |
| Nothing could be read | No score. "No pages could be read", then how the start address answered, in plain words, and the next action: not found (check the address); asks for a password (the site is private); its rules file tells checkers to stay out (ask its owner, or use Advanced); no answer (try again later); forwards to another site (check that one) |
| Engine restarting | The app's own banner, as in Advanced |

- **A tiny site (5 pages).** The same layout. The share of pages is coarse, so one page is "many" of five. Under
  10 pages, the coverage line adds: "Only 5 pages were found. If your site has more, its links may need
  JavaScript, which this version doesn't run." Why: a script-built site reads as one clean page, and a score of
  100 for it would be a lie.
- **A huge site (100,000 pages).** The same layout and the same five rows. Numbers take separators. Lists show 25
  at a time. The report is no longer than a small site's ([sizes](#sizes)).
- **A broken site.** The same layout. A site-wide Serious problem leads the sentence and the list.

## What to fix

`fix.html`.

- **Order and grouping.** Three groups, by severity, each with its count and one line on what the group means:
  Serious ("These stop pages working or being found. Fix them first"), Worth fixing ("These hold pages back"),
  Worth a look ("Often fine as they are. They don't change the score: read each one and decide"). Within a group,
  a site-wide fault first, then the most affected: the engine's "Fix first" order. Then **Couldn't check**, always
  listed with reasons, and **Nothing wrong**, folded.
- **How many at once.** Every Serious and Worth fixing problem. Worth a look shows its first three and "Show all
  13 things worth a look" while anything above needs fixing; with nothing above, it shows them all. Nothing wrong
  is folded behind "Show the 63 checks that found nothing wrong", and lists each check's good-news line ("Every
  page has a title"). Why fold: 13 notices and 63 passes would bury 6 problems.
- **A row** has at most two labels: the severity mark, and "One fix" when one cause covers most of the pages.
  Then the plain title, the first sentence of what's wrong, and "5 of 265 pages".
- **No search and no filter.** A site has at most 86 rows here. Advanced has both.
- **Copy this list** copies the titles, counts and one-line summaries as text. **Open the report** is beside it.
- **Hidden checks** (hidden in Advanced) stay out of the list, and a last line says "1 check is hidden", with
  Show. They still count in the score, which is the engine's.
- **Keys:** J and K for the next and previous problem, Enter to open it, as Issues has.

### One problem

`problem.html`, `problem-template.html`.

- **Head.** Back to What to fix; the severity badge; the plain title; one line of three facts: "5 of 265 pages",
  "Takes 4 points off the score" (or "Doesn't change the score"), "Usually fixed by you, in your site's editor".
  At the right, **See it in Advanced**.
- **Two columns:** "What's wrong, and why it matters" and "How to fix it", as numbered steps.
- **Where it is.** The check's groups, as Advanced has them, in plain columns: the cause on the left ("This address
  doesn't work"), and where it is on the right ("Linked from"), each page by its title and its address. A check
  with no groups lists its pages flat, with what was measured ("5,476 ms").
  - 25 causes at a time, "Show 25 more", up to 200. Past that: "Save as a spreadsheet" and "See all 4,000 in
    Advanced".
  - A cause on many pages names the first three and offers "Show them".
- **A template fault is one thing to fix.** When a group is the engine's `likelyTemplate`, a callout leads: "One
  fix clears 264 of these 265 pages. The same link has no words on 264 pages: the link to your home page. That's
  almost certainly your logo, which sits in the part of the site every page shares." The group's row carries "One
  fix", and so does the problem's row in the list.
- **A list to send on.** **Copy the list** puts on the clipboard, as plain text: the plain title and the technical
  one, the site and the date, how to fix it, then each cause with the pages it's on, as full addresses. **Save as
  a spreadsheet** saves this check's sheet of the workbook.
- **Done.** The app can't mark a fix as made, and doesn't pretend to. A last card says: "Fixed it? Shouting Iguana
  can't see a fix until it reads the site again. Check again, and this drops off the list if it's gone", with
  **Check the site again**. Later, crawl comparison turns that into "fixed since last time".
- **A skipped check's page** says what the check looks for, why it couldn't run, and what would let it. **A passed
  check's page** says its good-news line and what it looks for.

## The score

One number, 0 to 100, worked out by the engine so the app, the report and the command line agree. Reference code:
`app/design/simple/build/lib/score.mjs`.

**In two sentences.** The score starts at 100, and each check that found a problem takes points off: up to 10 for a
serious problem (60 if it is a fault of the whole site) and up to 4 for one worth fixing, by how much of the site
it affects. While any serious problem stands the score can't be above 89, and things worth a look take nothing off.

| How much of the site | Share of pages or addresses | Serious | Worth fixing | Worth a look |
| --- | --- | ---: | ---: | ---: |
| A few | Under 1 in 100 | 2 | 1 | 0 |
| Some | 1 in 100 or more | 4 | 2 | 0 |
| Many | 1 in 10 or more | 7 | 3 | 0 |
| Most | Half or more | 10 | 4 | 0 |
| The whole site | A site-wide fault, whatever its count | 60 | 4 | 0 |

- **The share** is the check's count over the pages read, for a check whose `subject` is `page`, and over the
  addresses checked (the crawl's URL count, as Home shows it), for one whose `subject` is `url`.
- **A site-wide fault** is a check the catalog marks `siteWide`: one fault for the whole site, not a page's.
  Today: `robots-txt-unreachable`, `no-https` and the new `site-hidden-from-search` (Serious);
  `duplicate-site-version` and `robots-txt-unreachable-other-origin` (Worth fixing).
- **The word:** 90 and over is Very good, 70 and over Good, 50 and over Needs work, under 50 Poor.
- **The ring is the accent blue**, never red, amber or green. Why: those colours mean severity and status here, and
  nothing else.
- **Version 1.** `scoreVersion` travels with every score, and is printed in the breakdown and the report. A
  changed formula is a new version, and an analysis scored by another version is out of date.

Why these choices:

- **Notices cost nothing.** Many can't be cleared: a page canonicalised on purpose stays a notice for ever. A
  score they lowered could never reach 100 on a site with nothing wrong.
- **Bands, not a curve.** A reader can check each line against a five-row table. A formula with a square root
  can't be followed.
- **A share, not a count.** One broken page costs a 100-page site and a 100,000-page site the same band's points
  for the same share, so the score doesn't swing on size.
- **89 while a Serious problem stands.** "Very good" beside a serious problem is the reassurance the comparison
  found in Sitebulb's own screenshot: a score of 90 beside 2 Critical hints.
- **60 for the whole site.** Search engines unable to read robots.txt, or a site not on HTTPS, takes the site out
  of "Good" by itself. Ten points wouldn't: such a site would score 90.

**What it covers.** The score is built from the checks that ran, on what the crawl read, and says so beside the
number and in the breakdown: "The score covers all 265 pages and 82 of 86 checks. 4 checks couldn't run."

- A skipped check lowers nothing and raises nothing. It is counted and listed.
- A crawl that didn't finish (stopped, interrupted, or at its limit) has a partial score: the dashed ring, "so
  far", and "covers the 312 pages read before the check was stopped".
- URLs robots.txt kept the crawl out of aren't covered, and the coverage line says how many: "robots.txt keeps
  search engines out of 1,080 addresses, which weren't read."
- **No score** when there is nothing honest to say: no page was read, or the crawl was never analysed. The screen
  says why instead.

**It can't say these lies.**

| The lie | Why it can't |
| --- | --- |
| A site robots.txt blocks scores 95 | A blocked start URL reads no pages: no score, and the Summary says why. A site blocked in part says what wasn't read, beside the number |
| A site whose every page is noindex scores 100 | `noindex-page` is a Notice. The new check `site-hidden-from-search` is Serious and site-wide: 60 points. It fires when more than half of the pages that stand for themselves are noindex; a page whose canonical names another URL is a variant, and counts neither way, so a shop with more filtered listings than main pages isn't called hidden |
| robots.txt unreadable, or no HTTPS, scores 90 | Site-wide: 60 points, so 40 at best |
| A 5-page site scores 20 for one missing description | A missing description is a Notice: 100. One page of five without a title is "many": 7 points, 89 with the ceiling |
| A site with 2 serious problems reads "Very good" | The ceiling of 89 |
| An unchecked site reads perfect | No score before an analysis; skipped checks are counted beside the score |

### Worked examples

**The garden crawl: 85, Good.**

| Check | Count | How much | Points |
| --- | --- | --- | ---: |
| `internal-client-error` (Serious) | 9 of 4,521 addresses | A few | 2 |
| `broken-internal-links` (Serious) | 5 of 265 pages | Some | 4 |
| `image-too-large` (Worth fixing) | 61 of 265 pages | Many | 3 |
| `description-duplicate` (Worth fixing) | 8 of 265 pages | Some | 2 |
| `title-duplicate` (Worth fixing) | 6 of 265 pages | Some | 2 |
| `slow-response` (Worth fixing) | 3 of 265 pages | Some | 2 |
| 13 Notice checks | | | 0 |
| | | 100 − 15 | **85** |

Fixing the first five in "Fix first" order leaves `slow-response`: 98.

**A nearly clean site (invented): 98, Very good.** 48 pages. One problem worth fixing, images with no description
on 3 of 48 pages ("some", 2 points), and 6 things worth a look.

**A badly broken site (invented): 36, Poor.** 1,240 pages, 3,870 addresses.

| Check | Count | How much | Points |
| --- | --- | --- | ---: |
| `broken-internal-links` | 1,240 of 1,240 pages (a footer link) | Most | 10 |
| `mixed-content` | 620 of 1,240 pages | Most | 10 |
| `internal-server-error` | 420 of 3,870 addresses | Many | 7 |
| `title-missing` | 187 of 1,240 pages | Many | 7 |
| `internal-client-error` | 212 of 3,870 addresses | Some | 4 |
| `image-broken` | 96 of 1,240 pages | Some | 4 |
| `redirect-loop` | 14 of 3,870 addresses | A few | 2 |
| `image-missing-alt` (Worth fixing) | 1,022 of 1,240 pages | Most | 4 |
| `viewport-missing` (Worth fixing) | 1,240 of 1,240 pages | Most | 4 |
| `duplicate-site-version` (Worth fixing) | Site-wide | The whole site | 4 |
| `title-duplicate` (Worth fixing) | 540 of 1,240 pages | Many | 3 |
| `slow-response` (Worth fixing) | 310 of 1,240 pages | Many | 3 |
| `broken-external-links` (Worth fixing) | 44 of 1,240 pages | Some | 2 |
| | | 100 − 64 | **36** |

**What each mix gives.**

| The site has | Score |
| --- | ---: |
| Nothing found, or only things worth a look | 100 |
| One problem worth fixing on a few pages | 99 |
| One problem worth fixing on every page | 96 |
| Five problems worth fixing, each on some pages | 90 |
| One serious problem on a few pages (98 before the ceiling) | 89 |
| One serious problem on every page | 89 |
| The garden crawl's mix: 2 serious (a few, some), 4 worth fixing (many, some, some, some) | 85 |
| Three serious on many pages, and five worth fixing on many | 64 |
| One site-wide serious fault and nothing else | 40 |
| Two site-wide serious faults | 0 |
| A 5-page site with no title on one page | 89 |

### How the score is worked out

`score.html`. One click from the number, in both modes, and a section of the report. It is a dialog over the
Summary, and a list the reader can follow:

- the two sentences;
- a line for each check that cost points: its mark and plain title, as a link to it; its count, what it is a share
  of and the band ("5 of 265 pages: some"); its points;
- one line for the things worth a look, at 0;
- the sum: "100 − 15", then 85;
- what it covers;
- the points table above, the ceiling and the four words;
- "Score version 1".

When the ceiling is what holds the score, a line says so: "Held at 89 while a serious problem stands (98
otherwise)."

## Plain words

Plain words are new texts beside the catalog's, not a rewrite: Advanced keeps the precise ones. The engine serves
both from `GET /api/checks`, so a new check brings its own.

### The template

| Field | What it is | Limit |
| --- | --- | --- |
| `plainTitle` | The problem as its owner would say it | 60 characters, sentence case, no count, no jargon |
| `plainWhat` | What's wrong and why it matters, as 2 to 4 sentences. **Sentence 1** says what is wrong and stands alone: lists and the report's first page show only it. **Sentence 2** says what it costs visitors or search. **The last** says when it is fine, for every Notice and wherever it's true | Sentence 1: 120 characters. All: 70 words |
| `plainFix` | How to fix it, as 1 to 4 steps. Each starts with a verb and is one sentence. The first is something the reader can do now | 25 words a step |
| `fixer` | Who usually does it: `you` ("you, in your site's editor"), `developer` ("your developer or hosting company") or `either` ("you or your developer") | |
| `plainPassed` | The good news when the check finds nothing, as a fact: "Every page has a title" | 60 characters |
| `plainOneFix` | Optional. What a template fault usually is, for the "One fix" callout: "That's almost certainly your logo" | One sentence |

Why `plainPassed`: a problem's title can't serve. "Pages with no title" beside a tick reads as the fault.

**`plainPassed` says the fault is absent, in a form that stays true when the check had nothing to look at.** "No
language version leads to a broken page" is true of a site with no language versions; "Language versions lead to
working pages" is not. "Every page has a title" is fine: the check read every page. On the garden crawl the first
draft printed "The sitemap can be read" beside "No sitemap found".

The 17 categories gain a plain title each (Response codes: "Pages that load"; Canonicals: "Duplicate addresses";
Hreflang: "Languages"; the rest are in `words.mjs`), and each skip reason a plain sentence and what would let the
check run ("Your site has no sitemap, the list of its pages for search engines, to compare with. Add a sitemap,
then check the site again.").

### The rules

- **UK English**, second person ("your site", "your pages"), present tense, active voice.
- **Reading age about 12.** Sentences of 20 words on average and never over 30. One idea a sentence.
- **Plain word first, the term once in brackets**, and only where the reader will need it to search for help or to
  brief a developer: "the short text under a page's title in search results (a "meta description")". Never the
  term alone.
- **Never bare:** canonical, indexable, crawl, crawler, status code, 3xx, 4xx or 5xx, redirect, noindex, nofollow,
  robots.txt, meta, tag, attribute, header, anchor text, hreflang, schema, JSON-LD, TTFB, DOM, render, directive,
  link equity, crawl budget. A code a person will see in their browser keeps its number with its name: "not found"
  (a 404).
- **Sitemap and HTTPS stay as they are.** A site owner meets both words in their own site's settings. A text says
  what one is where it first matters: "a sitemap: a file that lists your pages for search engines".
- **"Google"** where the fact is Google's; "search engines" otherwise. No promise of ranking: say what happens.
- **Numbers carry their unit and their limit:** "over 200 KB", "more than 1 second".
- **Never "investigate", "look into" or "requires further investigation".** Say what to look at, and what each
  answer means.
- **A Notice always says when it is fine**, and that there is then nothing to do.
- **The steps name a place:** "in your site's editor, often under "SEO"". Where the fix is a developer's, the first
  step is something the reader can check themselves, and the next is what to ask for.
- **Details stay the engine's**, one text for both modes: a fact with its unit, not a sentence ("5,476 ms", "2
  images over 200 KB"). Where a detail is a bare code it gains the code's name, in both modes: "HTTP 404" becomes
  "404 Not Found". Round 4 reads every detail as a non-expert would, and changes the engine's text where a plainer
  one loses nothing: "1 broken URL" is a candidate for "1 broken link".

### Ten checks, written out

They span ten categories and the hard cases: a site-wide fault, a check on URLs that aren't pages, a template
fault, a notice that is usually fine, and a skipped check. Texts for the garden crawl's other nine checks, and the
`plainTitle` and `plainPassed` of all 86, are drafts in `app/design/simple/build/lib/words.mjs`.

**1. `robots-txt-unreachable`** (Indexability, Error; a site-wide fault). Fixer: developer.

- Title: Search engines can't read your site's rules file
- What: The file that tells search engines where they may go on your site (its "robots.txt") answers with an
  error. Google stops visiting the whole site when that happens, so new and changed pages don't reach search
  results. This is one fault for the whole site, and the first thing to fix.
- Fix: (1) Open your site's address followed by /robots.txt in a browser, to see whether it loads for you. (2)
  Send this to your developer or hosting company today: the file must answer normally, or with "not found" if you
  don't need one. (3) Ask them to make sure the firewall or security service isn't turning away requests for it.
  (4) Check the site again once it's fixed.
- Passed: The rules file for search engines isn't returning an error

**2. `internal-client-error`** (Response codes, Error; its URLs may be files, not pages). Fixer: either.

- Title: Pages or files that can't be found
- What: Some addresses on your site answer with an error instead of a page or file, most often "not found" (a
  404). Visitors who follow a link to one get an error page, and Google drops addresses like these from its
  results. That's the right answer for something you removed on purpose, as long as nothing still links to it.
- Fix: (1) Look at each address in the list and decide whether it should still exist. (2) If it should, put the
  page or file back. (3) If it has moved, forward the old address to the new one (a "301 redirect"), in your
  site's redirect settings or through your developer. (4) If it's gone for good, leave it, and remove the links
  that still point to it.
- Passed: Every page and file on the site can be found

**3. `broken-internal-links`** (Links, Error). Fixer: you.

- Title: Links that lead to broken pages
- What: Some of your pages have links that lead to an address on your site that doesn't work. Visitors who click
  one land on an error page, and search engines waste their visit on it. A link in a menu or footer repeats on
  every page, so one bad link can show up hundreds of times.
- Fix: (1) Open each page in the list in your site's editor and find the link. (2) Point it at the right page, or
  remove it. (3) If the same link is on most pages, it's in your menu, footer or page design: fix it there once.
- Passed: Every link on the site leads to a working page

**4. `empty-anchor-text`** (Links, Notice; a template fault on the garden crawl). Fixer: either.

- Title: Links with nothing to read
- What: Some links on your pages have no words: usually an image or icon, such as a logo, with no description.
  People who use a screen reader hear only "link", and search engines learn nothing about the page it leads to. A
  logo or icon in the page design is the usual cause, so one fix often clears every page.
- Fix: (1) Find the link on one of the pages: the list shows where it leads. (2) If it's an image, give the image
  a short description (its "alt text"), such as your site's name for a logo. (3) If it's an icon, ask your
  developer to add a label to the link (an "aria-label").
- One fix: That's almost certainly your logo, which sits in the part of the site every page shares.
- Passed: Every link has words to read

**5. `canonicalised-page`** (Canonicals, Notice; usually fine). Fixer: either.

- Title: Pages that name another page as the main version
- What: These pages tell search engines that another page is the main version of them (a "canonical" link). Google
  then shows that other page instead, which is right for copies, such as page 2 of a list or a print version. It's
  a problem only if the page has its own content and should appear in search itself.
- Fix: (1) Look at each page and the page it points to, and decide whether they're really the same thing. (2) If
  they are, there's nothing to do. (3) If this page should appear in search on its own, ask your developer to
  point its canonical link at its own address.
- Passed: No page names another page as the main version

**6. `duplicate-site-version`** (Content, Warning; site-wide, and skipped on the garden crawl). Fixer: developer.

- Title: Your site answers at more than one address
- What: Your site shows its pages at more than one address, such as with and without "www", or on "http" as well
  as "https". The spare addresses should forward to the main one. As it is, search engines see two copies of
  everything and pick one themselves. Visitors on an "http" copy also browse without encryption.
- Fix: (1) Decide which address is the main one: usually the "https" one your home page uses. (2) Ask your
  developer or hosting company to forward every other version to it (a "301 redirect"), page for page. (3) Check
  the site again to confirm.
- Passed: The site answers at one address only
- When skipped (`site-versions-unchecked`): The site's other addresses (with and without www, http and https)
  couldn't all be tried. Check the site again to include it.

**7. `image-too-large`** (Images, Warning). Fixer: you.

- Title: Large images that slow pages down
- What: Some pages show images whose files are over 200 KB. Big images are the most common reason a page feels
  slow, most of all on a phone, and people leave slow pages. Photos uploaded straight from a camera or phone are
  the usual cause.
- Fix: (1) Resize each image so it's no bigger than it's shown on the page. (2) Save it as a compressed JPEG or
  WebP, then upload it in place of the old one. (3) If your site has an image-optimising setting or plugin, turn
  it on so new uploads are shrunk for you.
- Passed: No image is too big to load quickly

**8. `title-duplicate`** (Titles, Warning; grouped by the pages that share a title). Fixer: you.

- Title: Pages that share the same title
- What: Two or more pages have the same title: the text on the browser tab, and the link people click in search
  results. Searchers can't tell the pages apart, and Google may make up a title of its own. It often means two
  addresses show the same page, or the site's design leaves out what's different, such as the page number.
- Fix: (1) Open each page in your site's editor and give it a title that says what's specific to it. (2) If two
  addresses show the same page, keep one and ask your developer to forward the other to it.
- Passed: Every page has its own title

**9. `slow-response`** (Performance, Warning; a measurement). Fixer: developer.

- Title: Pages that are slow to answer
- What: These pages took more than 1 second to arrive when the check asked for them. Nothing can appear on screen
  until the page arrives, so visitors wait, and Google visits slow sites less often. One slow reading can be a
  blip, so try the pages yourself before acting.
- Fix: (1) Open two or three of the pages in your browser and see whether they're slow for you too. (2) If they
  are, send the list to your developer or hosting company and ask what the server is waiting for. (3) Ask about
  page caching and a faster hosting plan: they're the usual fixes.
- Passed: No page is slow to answer

**10. `description-missing`** (Meta descriptions, Notice). Fixer: you.

- Title: Pages with no search description
- What: These pages have no search description: the short text under a page's title in search results. Without
  one (a "meta description"), Google picks some text from the page, which may not be what you'd choose. It doesn't
  change where the page ranks, so start with the pages you most want people to click.
- Fix: (1) Open the page in your site's editor and find the description box, often under "SEO" or "Search engine
  listing". (2) Write one or two sentences that say what the page offers. (3) Skip pages nobody searches for, such
  as login and basket pages.
- Passed: Every page has a search description

## The report

One document, made by the engine: a single self-contained HTML file. Inline CSS, inline SVG, no script, no web
font, light only. It prints to A4 and Letter from the system print dialog, which is also how it becomes a PDF.
`report.html` is its design on the garden crawl, and `screens/report.pdf` is that file printed by Chromium.

### Outline

1. **Head.** "Website check", the site, the date, pages read, addresses checked, checks run.
2. **Summary.** The score as a ring, its word, the sentence, what it covers; the five counts.
3. **Fix these first.** Up to five: mark, plain title, count and share. "Fixing these five takes the score from 85
   to 98."
4. **How the score is worked out.** The two sentences, the line for each check that cost points, the sum, the
   bands in one line, the version.
5. **What to fix**, from a new page. Every check that found something, in three groups. For each: the plain title,
   with the technical title and severity small at the right ("Links to broken internal URLs · Error"); its count
   and share, its points, who usually fixes it; what's wrong and why it matters; how to fix it; a "One fix" line
   for a template fault; then its first pages, with what was found on each, and "And 56 more: the full list is on
   sheet 3 of the spreadsheet". Problems are numbered in "Fix first" order, 1 to 19 on the garden crawl, so the list
   on page 1, the section here and the workbook's sheet all carry one number.
6. **What couldn't be checked**, with each reason and what would let the check run.
7. **What was checked and found nothing wrong**: every passed check's good-news line, by category.
8. **About this check.** Where it started, when, how long, what was read, how many pages can appear in search,
   what wasn't followed, how it was read (speed, limit, robots.txt), whether it finished, and the report's words
   beside the technical ones.
9. **Foot.** "Made with Shouting Iguana", the date, the score's version, the engine's version.

- **One document for both modes and both readers.** Plain words lead, and each check carries its technical name,
  so the developer it is sent to can find the check. Why not two reports: "a report for everything", and two would
  disagree.
- **Two cuts.** The Report screen's **Full report | Summary page** asks the engine for the whole document, or for
  parts 1 to 4 and 9 alone. The summary is the full report's first page (or two, for a site with many problems),
  so the two can't differ.
- **Font:** `Inter, "Segoe UI", system-ui, …`, nothing embedded. Why: a file mailed to a developer must look right
  with no network and stay small.
- **No logo and no brand colour** beyond the score's blue. Branded client reports stay "later".

### Sizes

- **Examples are capped:** the first 5 pages of each Serious and Worth fixing problem, the first 3 of each thing
  worth a look, in crawl order. The engine takes the caps as options, and these are the defaults. Why so few: the
  report explains, and the workbook lists.
- **The length follows the checks that fired, never the site's size.** About half a page for a problem to fix and
  a quarter for a thing worth a look, which sets its examples beside its text. The garden crawl's 19 checks print
  to 11 pages of A4 and an 84 KB file. A 100,000-page site whose crawl fires 76 checks prints to about 30 pages.
  With all 87 firing it is about 35 pages and under 300 KB.
- **A crawl stopped early.** The head says so under the site: "This check was stopped early: 312 pages read, 1,380
  addresses still to check. This report covers only what was read." The score is marked "so far". Part 6 lists the
  checks that need a finished crawl. Part 8's "Finished?" says no, and how to continue.
- **A crawl never analysed has no report.** The Report screen says "The results haven't been worked out for this
  check" and offers Work out the results.

### The workbook

The full lists go in an Excel workbook beside the report, which the export work (E5) builds: a summary sheet (the
score, the counts, every check with its severity, plain and technical titles, count and share), then a sheet a
check that found something, with every URL, its title, its detail and the other URL involved. A sheet is named by
the problem's number in the report and the start of its plain title ("3 Large images that slow pag…"), as Excel
allows 31 characters, and the summary sheet lists each number with its full title. Header rows are frozen and
formatted.

### The Report screen

`report-screen.html`. In both modes, the same screen.

- **A toolbar:** the title, the site, date and page count; **Full report | Summary page**; **Spreadsheet of
  everything…**; **Save…** (the HTML file); **Print or save as PDF** (Ctrl+P), the primary action, which opens the
  system print dialog on the frame.
- **The document** below it, as a sheet on the canvas: the engine's HTML in a sandboxed frame with scripts off. It
  is light in both themes, as paper is.
- Saving uses a native dialog, then a toast with "Open folder", as every export does. Default names:
  `gardenfurnituresets.co.uk 2026-10-31 report.html` and `… .xlsx`.
- **States:** building (a skeleton sheet; it should take well under a second); never analysed (above); out of date
  (the Summary's banner, and the report says the date of its results); stopped early (the report says so itself).

### The report at the end

A crawl ends in the report without a hunt:

1. The checking screen becomes the Summary, whose primary action is **Open the report**.
2. The "finished" toast, and the system notification when the window is hidden, say: "gardenfurnituresets.co.uk is
   checked. Score 85: 6 problems to fix", with **See the summary** and **Open the report**. In Advanced the toast
   keeps its words and gains Open the report.
3. Report is in the sidebar of every crawl, in both modes.
4. A site's row on Home has Open the report.

## Settings in each mode

`settings-simple.html`, `settings-advanced.html`.

| Section | Simple | Advanced |
| --- | --- | --- |
| General | **Mode** (Simple, Advanced, with a line on each); Theme | Mode; Theme; Density |
| Crawl defaults | No | Yes: the New crawl form, saved as defaults |
| Analysis | No | Yes: every threshold |
| Data folder | Yes | Yes |
| Shortcuts | No (`?` still lists them) | Yes |
| Diagnostics | No. About carries "Copy diagnostics" | Yes |
| Updates | Yes | Yes |
| About | Yes | Yes |

- Under Simple's four sections, a note: "Crawl defaults, the checks' limits, shortcuts and diagnostics are Advanced
  settings", with **Show them in Advanced**.
- **Mode** is the same setting as the switch. Its help line says so: "The same site check either way, with the
  same checks and the same numbers. This is the switch at the top right of every screen."
- Settings made in Advanced apply in Simple. There is one set of crawl defaults and one set of thresholds.

## New crawl in each mode

`new-simple.html`, `new-advanced.html`. One form and one route. Simple shows four of its questions, and what is
typed goes with the user when they switch.

| | Simple: "Check a website" | Advanced: "New crawl" |
| --- | --- | --- |
| Start | **Web address** | **Start URL** |
| Speed | **How fast**: Polite ("Up to 2 addresses a second. For small or shared hosting"), Standard ("5 addresses at a time. Right for most sites"), Fast ("16 addresses at a time. For big sites on strong hosting") | The three presets with their rates ("5 at a time, no cap, slowing down when the site does"); the rate and concurrency under Advanced |
| Limits | **How much**: The whole site, or Stop after N addresses | Limits: URLs, depth, HTML size |
| Also check | **Links to other websites**; **Images and other files** | The same two switches, in Advanced |
| Scope | No | Stay in the start folder, subdomains, nofollow, canonical, hreflang and pagination targets |
| Robots and sitemaps | No | Obey robots.txt, Crawl-delay, find sitemaps, extra sitemaps |
| Headers and network | No | User agent, Accept-Language, extra headers, proxy and its test, cookies |
| Advanced | No | Timeout, retries, external concurrency, store HTML |
| Start | **Check site** (Ctrl+Enter), Cancel | **Start crawl** (Ctrl+Enter), Cancel |
| Defaults | A tick box: "Use these choices every time" | **Save as my defaults** |

- Simple ends with: "Which parts of the site to read, sitemaps, robots.txt, sign-in headers, a proxy and timeouts
  are Advanced settings. What you've typed here goes with you", and **Open in Advanced**.
- If a setting Simple doesn't show differs from its default, Simple says so above the button and doesn't hide it:
  "3 Advanced settings are changed: Scope, Headers. Show them in Advanced."
- Each setting has its one-line "what this does" in both modes, and validation is inline.
- Advanced's sections are folded, each showing its current values in a line ("No URL limit, any depth, HTML up to
  8 MB"), so the form reads without opening them.

## Keys

The Keyboard table gains these. None clashes with a key the IA has, or with WebView2's own.

| Keys | Action | Where |
| --- | --- | --- |
| Ctrl+Shift+A | Switch between Simple and Advanced | Everywhere |
| Ctrl+1 to Ctrl+3 | Summary, What to fix, Report | Simple |
| Ctrl+1 to Ctrl+8 | The crawl's sections, as built, with Report as the eighth | Advanced |
| Ctrl+P | Print or save as PDF | The Report screen |
| Ctrl+E | Export this view: the report on the Summary and the Report screen; this problem's pages on a problem | Simple |
| J, K, Enter | The next and previous problem; open it | What to fix |
| Ctrl+Shift+C | Copy the list | One problem |
| Esc | Close the score's breakdown | Summary |

- Ctrl+P reaches the page because the shell already turns off WebView2's own print.
- `?` lists the keys that work in the current mode.
- Ctrl+F does nothing in Simple: it has no grid to filter.

## What the three readers got stuck on

Each screen was read as a shop owner who has never heard of a canonical, a marketing manager who must brief a
developer, and a technical SEO opening the app for the first time.

| Who | Stuck on | Changed |
| --- | --- | --- |
| Shop owner | "✓ Pages with no title" in the passed list read as a fault | Every check gains a good-news line, `plainPassed` |
| Shop owner | "✓ The sitemap can be read" on a site the same page said has no sitemap | `plainPassed` says the fault is absent, and stays true when the check had nothing to look at |
| Shop owner | The first sentence of a problem ran to three lines in the list and hid the count | Sentence 1 is capped at 120 characters and holds no bracketed term it doesn't need |
| Shop owner | "264 of 265 pages" looked like 264 jobs | The "One fix" callout, and its tag on the row |
| Shop owner | "5 at a time" on the speed choice | "5 addresses at a time. Right for most sites" |
| Marketing manager | The page list named pages by address only | Each page shows its title, then its address; Copy the list carries full addresses and the technical name |
| Marketing manager | Nothing said who should do the fix | `fixer`, on the problem and in the report |
| Marketing manager | The report ran to 21 pages for 19 checks | Examples capped at 5 and 3; things worth a look set beside their text: 11 pages |
| Technical SEO | No way to tell Advanced existed from a first, empty Home | The switch reads "Simple, Advanced" in words, and first-start Home names it |
| Technical SEO | The search field crushed by Pause, Stop and the switch | The top bar's order of giving way, measured on the bar |
| Technical SEO | Plain titles hid which check a problem was | "See it in Advanced" on the problem; the technical title beside each check in the report; the palette finds either |

## What round 3 builds

Settings, New crawl and export, each to this design.

1. **The mode setting.** Stored with the app's other preferences; `simple` on a first start. A `useMode()` the
   shell and every screen read. No Simple screen is needed for this round: until round 4, a route with no Simple
   screen shows its Advanced one.
2. **The switch in the top bar**, as [The switch](#the-switch) says, with the palette commands and Ctrl+Shift+A in
   the shortcut registry.
3. **The top bar's order of giving way**, by its own width. Fixes the shell pass's search-field finding.
4. **Settings.** Every section the IA lists, with General > Mode; the Simple list of four sections and its note.
5. **New crawl in both modes:** one form, one route, Simple's four questions and Advanced's sections, "Use these
   choices every time" and "Save as my defaults", the changed-settings line. Home's Options… and Settings… open it.
6. **Export (E5),** with the report in it:
   - every grid's CSV and Excel, as the IA says;
   - the workbook: a summary sheet and a sheet a check;
   - the report file from the engine, in both cuts.
7. **The report's place in export.** Ctrl+E and the Export menu on Overview and Issues offer "The report…" and
   "Spreadsheet of everything…" beside the grid's own export, each through the native save dialog and the progress
   toast. That is all of the report round 3 shows: the Report screen is round 4's.
8. **Engine work for all of it**, below: the score, the report, the workbook, `siteWide`, the new check, and the
   plain-text fields (empty until round 4 writes them).

## What round 4 builds

Simple itself.

1. **The Simple shell:** its sidebar (Summary, What to fix with its count, Report), the pill's words, search as an
   icon, and "Advanced is on" with Back to Simple.
2. **Home in Simple:** the field, the saved-settings line, Your sites with scores, first start.
3. **Checking a site:** the three steps, the figures, the floor on time left, the alerts in plain words, paused,
   and working out the results.
4. **The Summary,** with every state in [Every state](#every-state), and the breakdown dialog.
5. **What to fix** and **one problem**, with the One fix callout, Copy the list, Save as a spreadsheet, the skipped
   and passed pages.
6. **The plain words for all 86 checks** (87 with the new one): `plainTitle`, `plainWhat`, `plainFix`, `fixer` and
   `plainPassed`, to [the template](#the-template) and [the rules](#the-rules), starting from `words.mjs`. Each
   read as a non-expert would, and checked against its technical text for truth. The 17 plain category titles and
   the skip reasons' plain sentences.
7. **The Report screen**, in both modes: the toolbar, the two cuts, the sandboxed frame, Save, Print or save as PDF
   and Spreadsheet of everything; its sidebar item (third in Simple, eighth in Advanced); the finished toast's
   words and its Open the report.
8. **The score in Advanced:** Overview shows it beside the answer, and it opens the same breakdown dialog.
9. **The states, keys, palette entries and stories** the definition of done asks of each screen, in both themes,
   forced colours, 200% zoom and reduced motion.

## What the engine needs

| Need | For | Shape |
| --- | --- | --- |
| Plain texts on every check | What to fix, one problem, the report | `GET /api/checks`: `plainTitle`, `plainWhat` (sentences), `plainFix` (steps), `fixer`, `plainPassed`, `plainOneFix` (optional). In `IssueCatalog`, beside the technical texts |
| Plain category titles | Summary's "What's fine", the report | `categories[].plainTitle` |
| Plain skip reasons | Summary, What to fix, the report | `skipped[].reason.plainText` and `plainAction`, by reason code |
| `siteWide` on a check | The score | `checks[].siteWide`: true for `robots-txt-unreachable`, `no-https`, `site-hidden-from-search`, `duplicate-site-version`, `robots-txt-unreachable-other-origin` |
| A new check, `site-hidden-from-search` | The score can't say "100" for a site search engines can't list | Error, Indexability, site-wide, reported once on the home page: more than half of the pages that stand for themselves are noindex. Hidden is the noindex pages, as `noindex-page` counts them; the total is the pages the analysis read, less those canonicalised to another URL, which are variants and count neither as hidden nor in the total. A strict majority, and never with no page left to judge. Detail: "212 of 240 pages are noindex". With its planted case |
| The score | Summary, Home, Overview, the breakdown, the report | On `GET /api/crawls/{id}/analysis`: `score: { version, value, band, lost, beforeCeiling, heldBySerious, partial, covers: { pages, urls, checksRun, checksSkipped, complete, reason, blockedByRobots }, lines: [{ checkId, severity, count, of, unit, extent, points }], ifFixed: { checkIds, value } }`, or `score: null` with `noScoreReason` (`not-analysed`, `no-pages`). `ifFixed` is the score with the first five in "Fix first" order fixed. One engine class works it out, tested against `score.mjs`'s cases |
| The score in the library | Home's rows | `CrawlListItem.score: { value, band, partial }` or null, kept in the summary cache |
| The score on the command line | The same number everywhere | `si issues` prints it in its head; `si score <db>` prints the breakdown |
| The report | The Report screen, Save, Print | `GET /api/crawls/{id}/report?cut=full\|summary&examples=5&lookExamples=3` returns the HTML. `si report <db> --out <file> [--cut summary]`. `not-analyzed` when there is no analysis |
| The workbook | Spreadsheet of everything, Save as a spreadsheet | E5's export, kinds `workbook` (every check) and `check` (one sheet), with plain and technical titles |
| A code's name in details | One problem, the report | "HTTP 404" reads "404 Not Found", in both modes. A text change: grouping by detail is unaffected within one analysis |
| Out-of-date for the score | The Summary's banner | A score by another `scoreVersion` makes the analysis out of date with `engineChanged` |
| What exists and is reused | | `/issues/{check}/groups` and `/rows` with `likelyTemplate` (Where it is, One fix); `progress` events (checked, queued, pages, the rate: time left is queued over rate); alerts; `subject`; `outOfDateReasons` |

The mode, the hidden checks and every sentence that isn't a check's (states, alerts, banners) are the app's, not
the engine's.

## For the owner

Decided here, and yours to overturn. Each has the alternative and the pick.

1. **Things worth a look cost no points.** The alternative is a small cost, capped at 10 together. Pick: nothing,
   so a site with nothing wrong can reach 100.
2. **A site not on HTTPS scores 40 at best.** The alternative is to treat it as any Serious problem: 90. Pick: 60
   points for a site-wide fault.
3. **The switch is in the top bar.** The alternative is the foot of the sidebar, beside Settings. Pick: the top
   bar, as the sidebar becomes icons on a narrow window.

Later, and left room for: the failing markup shown beside each problem (it needs stored HTML); "fixed since last
time" once crawls can be compared; a branded report.
