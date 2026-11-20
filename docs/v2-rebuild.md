# v2 rebuild: plan, decisions and status

The single place to pick up the rebuild. Technical detail lives in [engine/README.md](../engine/README.md) and the
rules for working in the code in [CLAUDE.md](../CLAUDE.md). Update the status and open items here at the end of every
step. For step 3, **Where step 3 stands** below is the only status. `v2`'s copy of this doc only points here. Update
this section before you stop, then commit it. **Never push** until the owner says it is time to deploy (2026-11-15:
pushes ran CI and used up their GitHub minutes); every check runs locally, and the app and engine workflows are
manual-only. This beta's one push is the exception already authorised and not yet done: `master` and the tag
`app-v2.0.0-beta.1` (step E). After that push, the rule stands again.

## Why

The WPF app in `src/` was hard to build on, its UI was clunky, and large sites caused memory problems. An audit
traced the problems to the design, not to C#:

- every page went through Chromium;
- the HTML was stored and reloaded about nine times per page, and each page was parsed ten or more times;
- plugins held static state;
- the default 1.5 s delay per host meant 40+ hours for 100k URLs.

v2 is a fresh solution that ports the useful logic.

## Plan

| Step | What | Status |
| --- | --- | --- |
| 1 | Headless engine, storage and CLI (`si`), proven on a generated 100k-page site, with the benchmark in CI | Done: `3e556be` |
| 2 | SEO rules and issue catalog (84 checks), `si analyze` / `si issues`, exact-match proof on the synthetic site | Done: `1489adb`, then a real-site crawl and two full reviews ([Status](#status)) |
| 3 | Desktop UI: React + TypeScript in a Tauri 2 shell, with the engine as a sidecar behind a token-protected localhost API. Enterprise-grade UX: the bar is in [the UI brief](v2-ui-brief.md). Since 2026-11-10 it opens in Simple, with Advanced one switch away, and ends in a report: [Simple first](#simple-first-advanced-on-a-switch-2026-11-10) | C and D are done on `master`. E and F are left. Pick up at [Where step 3 stands](#where-step-3-stands-2026-11-15) |
| 3b | The check audit: every check proven against real sites and worded for a non-expert, then the old app's checks worth keeping and those a comparison with Screaming Frog shows are missing | Planned on 2026-11-10: [Simple first](#simple-first-advanced-on-a-switch-2026-11-10) |
| 4 | Missing basics: JavaScript rendering, a browser fallback for blocked sites, include/exclude rules, auth and cookies, list mode, custom extraction | Later |
| 5 | Integrations: Search Console, then GA4, Bing Webmaster, Google Ads and Microsoft Advertising | Later |

**Step 3 starts with a design conversation with the owner, not with code.** The owner wants a "super slick,
enterprise level" UI and UX. The old app was "clunky and a bit all over the place". **[The UI brief](v2-ui-brief.md)
sets the bar and the process**: discovery, sign-off on mockups and a clickable prototype, vertical slices, and a
definition of done for every screen. It also covers the performance budgets and the engine work the UI needs.
Discovery and the visual direction were settled on 2026-11-01 (direction B, "clarity"). Do not restart them. Simple
(below) is added to them, in the same look.

## Simple first, Advanced on a switch (2026-11-10)

On 2026-11-10 the owner changed what the app is for. This section is the plan for that change. None of it is
designed or built yet: the owner asked only for the plan.

**What the owner asked for:**

- An SEO tool that is "super easy for anyone to understand".
- The app opens in a "simplified user friendly UI" that shows the overview. A switch to an "advanced mode" lets a
  technical SEO "look at everything in detail".
- All the same checks, each "properly audited and analysed to make sure they work".
- "A report for everything at the end."
- A quick comparison with Screaming Frog: "what we're missing compared to them", and whether the UI is as good or
  better.

**What it changes:**

- **Who the app is for.** The 2026-11-01 answer was an agency with many client sites. Now anyone comes first, and
  the agency and the technical SEO are one switch away.
- **What a crawl opens on.** Simple shows the answer, what to fix first and how to fix it, in plain words, and the
  report. Advanced adds URLs, Links, Sitemaps, robots.txt, the inspector, the filters and columns, and the crawl
  settings beyond the start URL.
- **The report moves into step 3.** The UI brief and the IA had client reports as "later". The report is now what a
  Simple crawl ends in.
- **The checks' words.** They were written for an SEO. Each check gets words a non-expert can act on. Whether that
  is a new line for each check or the existing texts rewritten is settled in the Simple design.
- **A score.** The Simple summary carries one number, with how it is worked out one click away. The owner said yes
  to it on 2026-11-10. What goes into it is settled in the Simple design.

**What it doesn't change:**

- The engine, the crawl file, the sidecar API and the 86 checks.
- Everything step 3 has built or has in flight. Those screens are Advanced as they stand, and none is redone.
- Steps 4 and 5, unless the comparison reorders them.

**Rules for the Simple design:**

- Simple is a view over the same crawl, the same checks and the same numbers. It is not a second app, and it shows
  no figure that Advanced can't list the URLs for.
- One switch, in the same place on every screen, and remembered. A first start is Simple.
- Simple starts from what exists: Overview (done), with its answer and "Fix first", and each check's "what's wrong,
  why it matters and how to fix it".
- "Simple" and "Advanced" go in [the glossary](v2-ui-discovery.md#glossary) before the app uses them.

**Where it fits: inside step 3, before round 3.** It doesn't interrupt what is in flight, and it doesn't wait until
everything else is finished.

- Round 2 isn't held up. Links, Sitemaps, robots.txt and E3c are Advanced, and already built.
- Round 3 waits for the Simple design. It builds Settings, New crawl and export. The switch lives in the first,
  Simple's "paste a URL and go" is the second, and the report rides on the third. Built before the design, all
  three would be changed afterwards, and step 3's closing audit would be run twice.
- Simple itself is a fourth round, after round 3. Step 3 isn't finished without it, as it is the first thing a
  user sees.

The order, item by item, is "Left, in this order" under [Where step 3 stands](#where-step-3-stands-2026-11-15).

**The comparison.** Research only, no code. It writes `docs/v2-comparison.md`.

- Screaming Frog for features, checks and how much a screen holds. Sitebulb beside it for the Simple side: the
  owner picked it on 2026-11-01 for how it explains issues.
- Three lists: features, checks and UI. Each gap gets a verdict: step 3, the check audit, step 4, step 5, later, or
  not doing.
- The plan already holds the known gaps. Step 4: JavaScript rendering, include and exclude rules, logins, list mode
  and custom extraction. Step 5: Search Console and analytics. Later: crawl comparison, scheduled crawls and
  projects.
- It reads the vendors' own sites, documentation and screenshots. Neither tool is run: the owner has no Screaming
  Frog licence and asked for no more than that (2026-11-10).

**Step 3b, the check audit.** After step 3 and before step 4.

- **Why.** The synthetic site proves each check flags its planted case and nothing else. It doesn't prove a check
  is right on real HTML. One real site has been audited: 20 checks fired there, and 3 of them were wrong (since
  fixed or documented). Most of the 86 have never met a real site. A wrong flag costs more in Simple, where the
  reader can't tell.
- **How.**
  - Three real sites, named by the owner on 2026-11-10 and crawled with their HTML kept:
    `www.gardenfurnituresets.co.uk`, `www.aptitude.co.uk` and `jitsunami.com`.
  - For each check: its flags sampled against the stored HTML, for wrong flags; a sample of the pages it didn't
    flag, for misses; and its words read as a non-expert would read them.
  - Three sites won't make every check fire. A check that fires on none is listed as not seen on a real site. It
    stays proven on the synthetic site only, until the owner names a site that has the fault.
  - A verdict for each check in `docs/v2-check-audit.md`: right, fixed, reworded, severity changed or dropped. Each
    fix gets a planted case and a test seen failing without it, as before.
  - Then the checks in [the old app's backlog](v2-ui-discovery.md#backlog-checks-from-the-old-app), and those the
    comparison shows are missing.
- **When.** It is engine work and touches no screen, so it can run beside step 3's rounds if the owner wants it
  sooner. It costs usage, and that is theirs to call.

The owner answered this plan's four questions on 2026-11-10, and nothing here waits on them: a score, yes; the app
flags and explains, and doesn't draft or make fixes; the comparison reads the vendors' sites and screenshots; and
the three audit sites above.

## Where step 3 stands (2026-11-15)

**2026-11-20, the v2 beta is on `master`.** `master` is the v2 product. It was `ui/step-3` at
`8769f4d3`; C deleted the WPF app and set the version to `2.0.0-beta.1` (`1dc0d851`); D is the README and the shots
in `docs/readme/`. The WPF tree that was `master` is the tag `legacy-v1` (`23820739`). Do not move or delete that
tag. `master` and `app-v2.0.0-beta.1` are on GitHub. The never-push rule is back in force for ordinary work.

- **C. Done.** Deleted `src/`, `ShoutingIguana.sln`, `Installer.iss`, `migrations.ps1`, `cleanplaywright.ps1`,
  `build-installer.ps1` and `.github/workflows/release.yml`. `engine/`, `app/` and `docs/` stayed. `CLAUDE.md` is the
  v2 layout. The version is `2.0.0-beta.1`.
- **D. Done.** `README.md` is for someone deciding whether to download the app. Light and dark shots are in
  `docs/readme/`.
- **E. Done.** `master` was pushed without a rewrite (a merge kept the two v1 commits in history). The tag
  `app-v2.0.0-beta.1` is `19dbb4d8`. The release run succeeded: Windows, macOS arm64, macOS x64 and Linux. A
  GitHub-hosted Windows job has no interactive desktop, so the installer smoke there does not require a window.
- **F. Done.** The installers and checksums are in `%USERPROFILE%\Downloads\ShoutingIguana-2.0.0-beta.1`, with
  `FOR-TESTERS.txt`. Every checksum matched.

**Before that, the round was on `ui/step-3`.** History, not the resume. The round landed at `2bbeef65` (fast-forward
from `ui/ux-integrate`). The tip before the move to `master` was `8769f4d3`. Nothing was pushed.

The five UX lanes are merged. Code samples are out of the fixes (`AnalysisFreshness.VERSION` 22). A legacy image inside
a `<picture>` that offers a modern source is not `image-legacy-format`. Engine gate passed (100k bench correct, memory
down 15 MB from 205 MB). App gate passed, including stories (670, both themes, plain, forced, reduced motion) and the
full shell walk (19 Advanced, 9 Simple). The owner walk at 2560×1380 and 1280×720 matched the rules: Advanced 1 click
from the 4xx check to the page at the link, Simple 3 clicks from Home, and the anchor-text fix is words with no code
sample. The release workflow is on this branch (`93e36e25`, tag `app-v*` only). Linux `.deb` and AppImage were built
in Docker; a real sidecar start needs system `libicu` (the `.deb` gets it through WebKit, the AppImage carries it).
macOS was not built here.

At that point C, D, E and F were still left. C and D are done; the current block above is the resume. E and F are
what is left.

Gallery version 8 is in `.claude/worktrees/_gallery/site/` (outside the repo, as before): 69 views, light and dark,
shot at 2560×1380 CSS. The Windows installer `Shouting Iguana_2.0.0-alpha.1_x64-setup.exe` was built unsigned and
the owner's install was upgraded with `/S` while the app was closed (exe in `%LOCALAPPDATA%\Shouting Iguana`,
written 2026-11-20 15:52). That installed copy is still `2.0.0-alpha.1`. The source version is `2.0.0-beta.1`.

**PAUSED 2026-11-19, about 09:15, for usage, mid-way through a UX round (session `da833503`). History, not the resume.**
Nothing is running: every agent stopped, every sidecar, Vite server and test site is down, every worktree is clean.
Nothing was pushed. Nothing below is merged into `ui/step-3` except the two severity changes and, since about 09:45
(session `90e68be9`), the fix as code, which has its own block below: **"The fix as code: merged, and what is
outstanding"**. The whole record is in
[`docs/handoff/2026-11-19/`](handoff/2026-11-19/): read [`checkpoint.md`](handoff/2026-11-19/checkpoint.md) first.

**RESUMED 2026-11-19 (session `848ba86b`), then PAUSED AGAIN the same day for usage. This block is the current
status; it replaces the lane table and steps 1 and 2 of the resume list below.** Nothing is running: every agent
stopped, every server down, every worktree clean. Nothing was pushed. **Nothing is merged into `ui/step-3`**: steps 3
to 6 of the resume list are not started. Each lane's state, finding by finding, is in
`docs/handoff/2026-11-19/state-after-<lane>.md` (they replace the `state-<lane>.md` files, which are the morning's).

| Lane | Branch | Head | State |
| --- | --- | --- | --- |
| engine | `ui/ux-engine` | `a2f9db6f` | **Finished and gated.** `verify-engine.sh` whole at head: build clean, 2,419 + 331 + 45 tests, 100k bench, readbench (23 gates). `AnalysisFreshness.VERSION` 21. A sitemap cut short on a recheck now keeps what the crawl read |
| shell | `ui/ux-shell` | `505c7f94` | **Finished.** 19 of 24 findings done, H-08, H-16, H-22 partly, H-12 (simple's files) and H-13 (polish) not. Whole Vitest passed at head (5,687). e2e: 13 specs ran one commit before head (1 failure, since fixed), 3 rerun at head; stories not run. The crawl's sidebar item is now "This crawl's settings" |
| data | `ui/ux-data` | `e254de30` | **Stopped early, committed clean.** D-02 (the `Url` button), D-06, D-09, D-12 done, D-10 done differently; D-04, D-08, D-11, D-14, D-16, D-17, D-18, D-20 partly. Whole Vitest passed three commits before head (5,751); data e2e at head 108 passed. Other e2e and stories not run |
| issues | `ui/ux-issues` | `2b915000` (WIP: e2e specs only) | **Stopped early.** `ui/step-3` is merged in, and **items 1, 2 and 3 of the fix as code are done there** (the fix in the check's head, "On this site", "1 link, to / in the header, repeated on 260 pages"). A-07, A-09, A-10, A-12, A-15, A-18 done bar small leftovers. The owner's 404 task: 1 click to the linking page at the link. Whole Vitest had 2 failures, fixed, **not rerun whole**; the 11 changed e2e specs **not rerun at head** |
| simple | `ui/ux-simple` | `24907078` (WIP) | **Stopped early.** `ui/ux-simple-report` merged in (fast-forward; its e2e passed). S-01 to S-04, S-07 ("Where to fix it"), S-12, S-13, S-16 to S-19 done; S-05, S-06, S-10, S-14, S-15, S-20, S-21 partly. Owner's task: 3 clicks from Home to the page at the link. **Whole Vitest never run**; last e2e 53 passed, 4 failed, the fixes committed and **unrun**. Engine catalog wording not done |

*To resume from here:*

1. Check the heads above (`git worktree list`), read each `state-after-<lane>.md`.
2. **Close the three lanes stopped early**, one agent each, gates before new work: **simple** (rerun its 12 e2e specs,
   whole Vitest, lint, format, stories; replace the WIP commit), **issues** (the 11 changed e2e specs, whole Vitest,
   stories `--forced` and `--reduced-motion`), **data** (the unrun e2e and stories; D-14 on Links; delete the old
   width code). Their partly done findings are in the state files: finish them or leave them listed.
3. Then steps 3 to 6 of the list below as written (integration branch; merge data, issues, shell, simple, engine;
   whole-suite gates; the walk as the owner; merge, gallery, installer). Changes to that list: the
   `CheckDetail.tsx` fix-as-code conflict is already resolved on `ui/ux-issues`, but shell's `b484f6e5` also touches
   `CheckDetail.tsx`, `AnalysisState.tsx` and `IssuesScreen.tsx`; tests that find the crawl's "Settings" item need
   "This crawl's settings"; data made a click on a URL grid's first cell open the inspector and renamed the facet
   button "Filter by status class", so other lanes' specs need the same edits; simple's `c1b7c50d` touches
   `Sidebar.tsx`, `TopBar.tsx`, `goToSection.ts`; capture `engine-checks.json` once from the merged sidecar.

**The v2 beta: everything left, in order (the owner's decisions of 2026-11-20; nothing here is open).** The beta is
this UX round, the move to `master`, a new README and a release workflow. Nothing else goes in.

- **A. Finish the round**: steps 1 to 3 above, ending with the round merged into `ui/step-3`, gated and walked as
  the owner, the gallery, the Windows installer and the owner's install upgraded while the app is closed.
- **B. Take the code out of fixes** (in the round, on the integration branch after the lanes are merged, before the
  gates and the walk). The owner: "just remove code completely and make the explanation clear in the text of how to
  fix". This reverses the fix-as-code direction of 2026-11-19, in both modes.
  - Remove `CodeExample` from the check pane, with its stories and tests, and `fixExamples` from what the app
    shows. Whether the catalog and the API keep the field is the builder's call; if it goes,
    `AnalysisFreshness.VERSION` and `engine-checks.json` follow.
  - Keep the words the round got right: the fix at the top of the check, "On this site" naming the case the rows
    show, "1 link, to /, in the header, repeated on 260 pages: one fix clears them".
  - Read every check that had an example and make its fix text say, without code, what to change and where, e.g.
    "Give the logo link in your header a label (an `aria-label`, or alt text on the image inside it)". Then review
    the fix text of every check that fires on the three real crawl copies, in both modes.
  - Simple stays text only. The site's own markup is not stored or shown.
  - `image-legacy-format`: the rule passes an image whose `<picture>` offers a modern source (`ImageFact.InPicture`);
    planted case and expected count on the synthetic site change with it.
- **C. v2 becomes the product on `master`** (the owner: "This also needs to become the main branch product, as we
  are retiring the old code"). All local until step E.
  - Tag today's `master` as `legacy-v1`: the only copy of the WPF app that is kept.
  - Bring `ui/step-3` onto `master` and delete the old code there: `src/`, `ShoutingIguana.sln`, the Inno Setup
    scripts, `migrations.ps1`, `cleanplaywright.ps1`, `build-installer.ps1` and the legacy `release.yml`. That
    workflow runs **on push**: it must be gone before anything is pushed.
  - Rewrite `CLAUDE.md` for the v2 layout (most of it describes the WPF app) and retire its pointers to `v2` and
    `ui/step-3`. Do not rename anything in the engine or the app.
  - The app's version becomes `2.0.0-beta.1` everywhere it is stated.
- **D. A new `README.md`** (the owner: "revamp the readme with screenshots and make it a modern looking readme").
  Written for someone deciding whether to download it, not for contributors.
  - Screenshots from the merged build at the owner's window size, light and dark: Simple's Home and "Where to fix
    it", an issue with its fix text, the URL inspector, the report. Images live in the repo (`docs/readme/`).
  - What it is; downloads per OS; how to open an unsigned build past SmartScreen (Windows) and Gatekeeper (macOS);
    a short first crawl; what it checks; building from source; licence.
  - It says the `ShoutingIguana.PluginSdk` NuGet package is no longer used and v2 has no plugin system. The package
    stays published.
- **E. The release workflow, then the one push.**
  - Extend `app-release.yml` (today Windows only, run by hand) into a matrix of Windows, macOS (arm64 and x64) and
    Linux (`.deb`, `.AppImage`): each builds the sidecar and the shell and attaches its installer and a checksum to
    one GitHub release. It runs **on a tag `app-v*` only**, never on a branch push. `app.yml` and `engine.yml` stay
    manual. Unsigned for the beta: the signing steps skip, saying so.
  - Before the push: check whether the self-contained sidecar needs the system's `libicu` on Linux (no
    `InvariantGlobalization` was found in the engine's project files) and build and run the Linux bundle once
    locally in Docker. The macOS build cannot be tried on this machine: say so to the owner when it ships.
  - Then push `master` and the tag `app-v2.0.0-beta.1`. The owner's go-ahead (2026-11-20): "when next batch is
    done, you update everything and commit to github and that creates the releases for each". It covers **this one
    push, once A to D are done**; the never-push rule stands until then and again afterwards for ordinary work.
  - Watch the run; fix and re-tag if a platform fails (each run costs minutes: the repository is private and macOS
    runners bill at ten times the Linux rate, so get it right locally first).
- **F. Hand over.** The repository stays private and the owner sends testers OneDrive links: download every
  installer and its checksum from the release into one folder and tell the owner where it is, with the
  unsigned-build instructions for testers.

*After the beta, not before:* step 4 (JavaScript rendering, list mode, custom extraction); URL include/exclude
patterns; a per-URL new/gone comparison between crawls; a "worst pages" list in Simple (none designed yet); and a
talk with the owner about the cheapest way to sign, in a way that also covers other apps they may build.

*What the owner asked (2026-11-19, on the installed app and their own garden crawl):* "The UI looks nice in most cases
but the UX really falls short." In Issues > "Internal URL returns 4xx" the page that holds each broken link was plain
text; reaching it took an expand, a "Show the link in Links" and another screen. They asked for a high-level UX audit
of every screen, done by using the app as an SEO, and for the faults to be sorted out, in Simple too ("people are going
to be probably more interested as to where the broken link is"). Later the same morning: a correct "Internal links
without anchor text" finding read as a false positive because the pane never showed which link or what to type: "if
there's an obvious fix, it needs to be very easy to read."

*Done and on `ui/step-3` (local only):*

- **Two severities changed at the owner's word** (`e02b185a`, `4a7fab91`): `canonical-to-first-page` is a Notice
  ("good SEO practice", reworded as a common, often deliberate setup) and `broken-external-links` is an Error ("it's a
  broken link"). Catalog: 17 Errors, 52 Warnings, 101 Notices. The garden fixture is 2 / 3 / 23 and scores 87.
  Engine tests 2,403 + 331 + 45 and the 100k bench passed; Vitest passed whole only with `--maxWorkers=8` (5,513); the
  e2e specs and stories were updated and **not run**; readbench was **not run**.
- **The audit** (`2a2cc140`): four auditors used the app at **2560 × 1380, the owner's window** (every earlier audit
  and gallery was 1280 × 720), then at 1280 × 720, on copies of the three real crawls and the synthetic one. About 80
  findings in `audit-issues.md`, `audit-data.md`, `audit-shell.md` and `audit-simple.md`.
- **The rules every screen follows from now on** (`fix-common.md`): a URL's text is a link; open-in-browser sits
  directly after it; the page that holds a fault is in the row with the element that identifies it; opening that page
  lands on the fault (`urlAtText`, `app/src/lib/textFragment.ts`, on the base); counts are links to exactly their
  list; one scroller per pane; columns share spare width by need; Back keeps your place.

*Built on lane branches, **not merged, and none has had a whole-suite run at its head**.* Each lane's exact state,
finding by finding, with what ran and what didn't, is in its `state-<lane>.md`:

| Lane | Branch (worktree `.claude/worktrees/<name>`) | Base | Head | State |
| --- | --- | --- | --- | --- |
| issues | `ui/ux-issues` (`ux-issues`) | `2a2cc140` | `8c918b47` | 12 findings done, 5 partly (A-07, A-09, A-10, A-15, A-18). The owner's 404 task held at 2560 × 1380 when last seen, before the last two commits. At 1280 × 720 the card still scrolled on five busy checks |
| data | `ui/ux-data` (`ux-data`) | `2a2cc140` | `5922ad44` | 6 done, 8 partly, 5 not started. Links, Sitemaps, robots.txt and the inspector changed; **URLs and the grid core are as on the base**. Known fault: `Url`'s open button sits about 270 px after an on-site URL's text. No "after" screen was looked at |
| shell | `ui/ux-shell` (`ux-shell`) | `2a2cc140` | `1f2419f9` | 9 done (H-01, H-02, H-03, H-05, H-06, H-11, H-18, H-19, H-23), 2 partly (H-04, H-07), 13 not started |
| simple | `ui/ux-simple` (`ux-simple`) | `4a7fab91` | `4a7fab91` | **Nothing built**: its agent ended on a server error after deciding how; the decisions are in `state-simple.md` |
| simple's report | `ui/ux-simple-report` (`ux-simple-report`) | `4a7fab91` | `84b2641a` | The report in two wordings, with working links and "where to fix"; the spreadsheet's linking-page columns. e2e rewritten, not run (`state-simple-report.md`) |
| engine | `ui/ux-engine` (`ux-engine`) | `4a7fab91` | `c71fe5bd` | The lost sitemap list traced and three more row losses on a recheck fixed; a damaged file says so; the recheck's numbers and words fixed. `AnalysisFreshness.VERSION` is **21** there. The whole engine suite, bench and readbench were **not run at head** |
| fix as code | `ui/ux-fix` (`ux-fix`) | `f2ae0922` | `179f838a` | **Merged into `ui/step-3`** (`14f87577`) and mounted in the Issues pane as it is on the base (`3387487b`). 67 checks carry a fix as before and after code (`fixExamples`), with `CodeExample` and `HowToFix`. What is left is in the block below; the branch is no longer needed |

*The lost sitemap list (the data audit's D-01) was not a fault of any installed build.* An in-progress engine build of
18 Nov (`8cbd8c24`) deleted a sitemap's rows on a recheck, then failed inserting them into a file without a `lastmod`
column; `2afdf35d` had already fixed it, and the damaged file was a lane's test copy. The owner's library was not
opened to confirm.

*To resume, in this order:*

1. Read `docs/handoff/2026-11-19/checkpoint.md`, then each `state-<lane>.md`. Check every lane worktree is still clean
   and at the head in the table (`git worktree list`).
2. **Finish the lanes**, one fresh agent each, briefed with `fix-common.md`, `lane-briefs.md`, the lane's audit and its
   state file, working from that file's "next five steps": **simple first** (nothing is built: merge
   `ui/ux-simple-report` into it, then S-01 and the "Where to fix it" card), then **data** (the `Url` open-button
   fault, column sizing, the URLs screen), **shell** (13 findings), **issues** (the five partly done, the 1280 × 720
   scroll), **engine** (its gates at head).
3. **Merge** into an integration branch off `ui/step-3`, in this order: data, issues, shell, simple (with its report),
   engine (the fix as code is already on `ui/step-3`: don't merge `ui/ux-fix` again). Expect conflicts in `app/src/ui/Url.tsx` (data's `a57d5990` and issues' `4b38b12b` both add
   `openUrl` / `openHint`, to the same agreed shape: keep data's), `app/src/styles/components.css` and `tokens.css`
   (appended blocks: keep all), `IssueCatalog.cs`, and the `Shared:` commits each state file lists. Capture
   `app/src/test/simple/engine-checks.json` once from the merged sidecar instead of merging it by hand. `ui/ux-issues`
   will conflict in `app/src/screens/issues/CheckDetail.tsx` where `Explain` now draws `<HowToFix>`: do items 1 to 3 of
   "The fix as code: merged, and what is outstanding" below while resolving it.
4. **The whole-suite gates on the merge**, since no lane ran them: `verify-engine.sh` (bench and readbench),
   `verify-app.sh`, `e2e:filled`, `tauri build --no-bundle`, `smoke:shell`; Linux in Docker if `src-tauri` changed.
5. **Look at it as the owner**: drive the merged app at 2560 × 1380 and 1280 × 720 on the crawl copies
   (`.claude/worktrees/_handoff/2026-11-19/crawls/`, driver in `docs/handoff/2026-11-19/tools/`), redo the owner's 404
   task and each audit's tasks with click counts, in both modes. Fix what still fails `fix-common.md`'s rules.
6. Merge into `ui/step-3`, update this section, a new gallery shot **at the owner's window size**, the installer, and
   upgrade the owner's install only while the app is closed. Never push.

**The fix as code: merged, and what is outstanding (2026-11-19, session `90e68be9`).** The owner read a correct
"Internal links without anchor text" finding on the garden crawl as a false positive: 260 pages, all one link, the
header logo `<a href="/"><svg>…</svg></a>` with no `aria-label` and no `<title>` in the SVG. The pane never said it
was one link or what to type. Their words: "if there's an obvious fix, it needs to be very easy to read. In a brilliant
UI and UX, so the user understands how to fix things", then "I need it all merged into the main app like everything
else" and "anything not done add it back into step 3 … so that when I start another agent session, it knows what needs
doing".

*Done, on `ui/step-3`:*

- `14f87577` merges `ui/ux-fix` (no conflicts): what it adds is in
  [`docs/handoff/2026-11-19/fix-lane.md`](handoff/2026-11-19/fix-lane.md), "What the lane adds". In short: 67 of the 170
  checks carry `FixExamples` (78 examples; `empty-anchor-text` has three cases, `icon-link`, `image-link`,
  `empty-link`, and its `Fix` now covers an inline SVG); `GET /api/checks` sends `fixExamples`; `ui/CodeExample.tsx`
  draws "Now" over "Fixed" with the change marked and a Copy button; `screens/issues/HowToFix.tsx` draws the heading,
  the words and the example, one case at a time as tabs.
- `3387487b` mounts it: `Explain` in `app/src/screens/issues/CheckDetail.tsx` takes the check and draws "Why it
  matters" beside `<HowToFix check={check} />` (the fix in the wider column, `--how-to-fix-columns`, at the `wide:`
  breakpoint; stacked below it). `app/src/test/issuesSidecar.ts` gives `empty-anchor-text` two examples, and
  `IssuesScreen.test.tsx` has "shows a check's fix as code under How to fix it, a case at a time, and words alone where
  it has none".
- Ran at `3387487b`: typecheck, lint, format check, the whole Vitest suite with `--maxWorkers=8` (5,615 passed, 189
  skipped, 520 files). With the sidecar built from the merged engine,
  `e2e/issues.spec.ts` and `e2e/what-matters.spec.ts` passed (17 of 17); the rest of the e2e suite was not run.
- The engine code at `3387487b` is byte for byte the lane's at `179f838a` (`git diff 179f838a 3387487b -- engine` is
  empty), where `verify-engine.sh` passed whole (build, tests, 100k bench, readbench). The stories passed axe (light,
  dark, forced colours, reduced motion) at the lane's head, before the mount.

*Outstanding, in the order to do it. Nothing here is started unless it says so.*

1. **The fix is still at the bottom of the pane.** On the base the "Why it matters / How to fix it" strip sits under
   the affected pages, a "Why it matters and how to fix it" button away. The owner's bar is that the fix is easy to
   read, so it belongs near the top. That move is the issues lane's A-09 and A-10 (partly done on `ui/ux-issues`:
   "why" and "how" beside "What's wrong" at wide sizes, folded at narrow ones). When `ui/ux-issues` is merged it
   conflicts in `CheckDetail.tsx`: keep the lane's layout, and where the lane draws the paragraph of `check.fix` put
   `<HowToFix check={check} preferredCase={…} />` instead (it draws its own `<h3>` "How to fix it" with the pane's
   classes, and returns null for a check with no fix and no examples). Give it the wider column
   (`wide:grid-cols-(--how-to-fix-columns)`: the code wants up to 100 characters, the words about 46); in the narrow
   fold it goes in whole. Then fix the new test in `IssuesScreen.test.tsx`, which finds the strip by
   `closest('[tabindex="-1"]')`. If the issues lane is not going to be finished soon, do the move on the base instead:
   put "How to fix it" directly under "What's wrong" in the head and leave "Why it matters" in the strip; the test
   "puts what the check flags ahead of why it matters and how to fix it, a button away" (`IssuesScreen.test.tsx`),
   `app/e2e/issues.spec.ts:47-51` and `app/scripts/smoke-shell.mjs:423-445` name the button and the headings.
2. **"On this site" is not wired.** `HowToFix` gets no `preferredCase`, so the first case shows (for
   `empty-anchor-text` the icon case, which happens to be the garden site's) with no "On this site" marker. Pass
   `preferredFixCase(check.id, targets.map((target) => target.flags))` (`screens/issues/fixCase.ts`) from the
   `IssueTarget`s the pane has already read (`/issues/{checkId}/targets`: `useWhereFound` on the base,
   `useRowReferences` on `ui/ux-issues`), never from a request of its own; `undefined` until some are loaded. Add a
   test that the `image-link` tab is selected and marked when the targets carry `imageLink`.
3. **"260 pages" still reads as 260 links.** The template callout (`TemplateCallout`, `IssueGroups.tsx`, about line
   1287 on the base; the issues lane has changed that file) says "The same URL, /, on 260 of 260 pages, so it's likely
   in the site's template: one fix clears the group. It's a link in the header." It should lead with the count of
   things: "1 link, to /, in the header, repeated on 260 pages: one fix clears them", and the group's rows should not
   look like 260 separate faults ("in the header" 260 times). Pinned by `IssuesScreen.test.tsx` (the template callout
   tests, about lines 541, 1229 and 1242 before this round) and `IssuesScreen.evidence.test.tsx:87-97`. Simple already
   says it well (`simple/plainEvidence.ts:207-240`).
4. **The gates not run on the merged branch**: `build-storybook` and `npm run test:stories` (plain, `--forced`,
   `--reduced-motion`), the whole e2e suite, `e2e:filled`, `tauri build --no-bundle`, `smoke:shell`. They are step 4 of
   the resume list above; run them there, on the integration branch.
5. **Not in the owner's installed app.** No installer was built from `3387487b`. It goes in with step 6 of the resume
   list (installer, then upgrade the owner's install only while the app is closed). The owner has not yet seen the
   block anywhere but in a Storybook shot.
6. **Look at it as the owner** on the garden crawl copy at 2560 × 1380 and 1280 × 720, light and dark: open
   "Internal links without anchor text" and each check in the owner's list that has an example (images without width
   and height, missing meta description, missing canonical, Open Graph tags missing, X card missing and incomplete,
   cookies without Secure or HttpOnly, security headers missing, missing H1), and ask of each: in five seconds, do I
   know what to type and where? Fix what fails.
7. **The other 103 checks have words only.** The lane judged their fixes editorial or structural. Nobody has reviewed
   that list. Go through the checks that fire on the three real crawl copies and have no example, and decide each:
   an example, better words, or right as it is.
8. **DECIDED 2026-11-20: not to be built, and the code block itself comes out (see "The v2 beta", B).** The code shown is a sample, not the site's own markup ("Example Furniture", not the owner's logo link). The
   crawl stores no HTML of the element a check names (`links` holds `from_id, to_id, kind, flags, position, text,
   fragment`; `response_html` is kept only with `StoreHtml`, off by default). To show the real element as "Now": the
   parser (`Parsing/PageWalker.cs`, where a link without a name is found) keeps the element's opening tag and a
   shortened inside, capped at a few hundred characters, only for links and images a check will name; a new column or
   side table that only `Storage/CrawlWriter` writes; `IssueTarget` and `/issues/{checkId}/targets` send it;
   `CodeExample` shows it in place of the sample `before`, and builds "Fixed" from it where the fix is one attribute.
   The 100k bench must stay flat, the crawl file's schema version and `AnalysisFreshness.VERSION` go up, and existing
   crawls show the sample until crawled again. **Not agreed with the owner: ask before building.** `v2-ui-design.md`
   lists it as "Later; needs stored HTML".
9. **DECIDED 2026-11-20: Simple stays text only.** Simple doesn't show the code. `simple/ProblemScreen.tsx` shows `plainFix` steps only. `CodeExample` is in `ui/`
   so it can sit under the steps, perhaps only where the fixer is a developer. Not agreed with the owner: ask.
10. **DECIDED 2026-11-20: the rule passes those images.** `image-legacy-format` says one thing and checks another (`fix-lane.md`, "Found on the way"): the words offer a
    `<picture>` fallback, the rule still flags the `<img>` inside one. Either the rule passes an image whose
    `<picture>` offers a modern source (`ImageFact.InPicture`) or the words stop offering it. The owner's to decide.
11. `app/src/test/simple/engine-checks.json` on `ui/step-3` is now the capture with `fixExamples`. Capture it again
    from the merged sidecar after any lane that changes the catalog (`fix-lane.md` says how; there is no script).

*Decided by the owner on 2026-11-19, after the pause:* **JavaScript rendering is imperative** ("or we won't be able to
properly crawl or check things") and **custom extraction is needed**. So step 4 follows this UX round, JavaScript
rendering first, and neither is "later" any more. List mode and include/exclude rules were explained to the owner the
same day, and the owner's answer was "yes all those are needed". **Step 4, in the order agreed: JavaScript rendering
with include/exclude rules alongside it (small, and it keeps a crawl out of filter and parameter traps), then custom
extraction, then list mode.** It starts when this UX round is merged and gated, not before.

*Left for the owner to decide (features, not UX fixes, so not in this round):* URL include and exclude patterns,
JavaScript rendering, a URL-list crawl mode (all three are step 4 of the plan and were asked about on 2026-11-19: the
old app's ListMode and CustomExtraction plugins and its browser rendering are the only parts not yet carried over); a
per-URL new/gone comparison between crawls; a "worst pages" list in Simple. Also open: whether `image-legacy-format`
should pass an image whose `<picture>` offers a modern source (`fix-lane.md`, "Found on the way").

Still waiting from before this round: the owner's walkthrough below (the Jitsunami signature), and the release.

**Step 3b, under way since 2026-11-17 (session `a539aa23`).** The owner asked for step 3b and, with it, a UX audit
done as the site owner ("For every single view / UI it must be actually useful … easy to scan, understand and action"),
plus a check that every setting of the old app is here. Every brief, finding and the orchestrator's checkpoint are in
[`docs/handoff/2026-11-17/`](handoff/2026-11-17/) (`checkpoint.md` first). The owner's decisions that day:

- **The fix loop: compare and recheck.** Every check, the score and every list compares with the site's previous crawl
  (New, Fixed, Fewer, the score's change), and a problem offers "Recheck these N pages", which fetches only those pages
  again ("4 of 5 fixed"). No manual "mark as fixed".
- **New checks: all of them.** Every check in the old app's backlog, and every Screaming Frog check that needs only data
  already fetched.
- **Title and description lengths in pixels**, as Screaming Frog measures them: yes.
- **Never trip a site's rate limit** as the first Jitsunami crawl did ("make sure this is smart enough we don't hit
  things like this").

The three real sites were crawled for the audit (`-c 2 --rate 2 --respect-crawl-delay --store-html`, at most 5,000
URLs): aptitude.co.uk (33 pages), jitsunami.com (10 pages, the rest refused with 429s) and gardenfurnituresets.co.uk
(4,360 URLs, 260 pages). The crawl files are in the session scratchpad's `audit3b/crawls*/`. Crawling any other real
site still needs the owner's word, and so does a third Jitsunami crawl.

**Merged into `ui/step-3` on 2026-11-17 (local only):**

- **The check audit** (`ca10014` merges `ui/3b-accuracy`). All 87 checks were checked against the stored HTML of the
  three sites; the verdict per check is in [v2-check-audit.md](v2-check-audit.md). The only wrong flags came from
  Shopify's login redirect and from the 429s. The fixes:
  - a canonical or hreflang target that refused the crawler is unverified, not non-indexable;
  - links to redirects count only 301 and 308;
  - `redirect-temporary` skips a redirect to another site (hosted login, checkout);
  - `orphan-page` is skipped (reason `pages-unread`) when 1 in 20 or more pages couldn't be read;
  - a page's empty link to itself (a logo) is flagged;
  - schema.org subtypes inherit their parent's required properties;
  - a `<meta charset>` counts only within the first 1,024 bytes.

  Severities changed: `sitemap-error` and `structured-data-invalid` are now Warnings, and `description-duplicate` a
  Notice. Words were rewritten throughout. Scores: Jitsunami 76 → 79, the garden site 83 → 85.
- **The evidence** (`73a2674` merges `ui/3b-evidence-ui`, which holds `ui/3b-evidence`). Every check names its evidence
  columns and target facts ([v2-evidence.md](v2-evidence.md)). New URL columns: redirect chain, robots.txt rule, used
  on, pixel width and height, fetch error. Details name what they count ("1 image over 200 KB: 293 KB"; the title and
  description lengths have read in pixels since the pixel-widths merge below, "742 px, over 580 (78 characters)"). The image checks are grouped by image, and rows are ordered by importance. The app shows it all in Advanced
  and in Simple's words, with thumbnails. Image matching was 100% on the three real crawls. `c6a9b40` captures Simple's
  fixtures again (the fixture garden crawl now scores 87).
- **The polite crawl** (`9c1e8d2` merges `ui/3b-polite`):
  - each host gets a slow start and a time-based ramp, its pace is halved when it asks to slow down (AIMD), and a
    `Retry-After` pauses the whole host, with no burst afterwards;
  - a 429 is put down to its folder, and only that section is paced (429s from three sections in ten minutes count as
    the whole site);
  - a 429'd URL is asked again at the end, with the gap doubling up to 20 minutes, else it is "Not checked: the site
    asked the crawler to slow down";
  - new notices `rate-limited` and `rate-limited-unchecked`, plus a section-limited wait;
  - `internal-blocked` is now "Internal URL refused the crawler", a Notice, for a firewall's refusal only;
  - a public site's crawl never fetches a private address (`PrivateNetworkGuard`).

  The integration made a page the site kept rate-limiting count as unread for the orphan skip. `3721374` captures the
  catalog fixture again, and `93bd1eb` and `35d4888` update five e2e expectations the slow start changed (the first
  figures can say "1 URL so far"; a paused count settles a moment later).
- **Pixel widths** (`41b2c23` merges `ui/3b-pixels`; design as built in [v2-pixel-widths.md](v2-pixel-widths.md)):
  - `title-too-long`, `title-too-short`, `description-too-long` and `description-too-short` (ids unchanged) measure
    Liberation Sans 2.1.5 advance widths (OFL, metric-compatible with Arial; listed in About's licences): 20 px for
    titles, 14 px for descriptions, CJK and emoji at 1 em;
  - limits: titles over 580 px or under 200 px, descriptions over 985 px or under 400 px;
  - Advanced says "612 px, over 580 (64 characters)". Simple and the report say where Google will likely cut it off,
    at the last whole word, and never open the quote on a separator ("…Phone, Email or Contract", not "…| Phone");
  - the setting fields are renamed (`titleMaxPixels` and so on). Old names in the sidecar's settings and in crawl
    files convert: a default becomes the new default, anything else 9.4 px (titles) or 6.2 px (descriptions) a
    character. Settings > Analysis shows px with a characters hint;
  - new URL columns `titleWidth` and `descriptionWidth`, and where results cut them; the inspector shows both widths.

  Real crawls (copies), characters → pixels: garden furniture's title too long 84 → 69, title too short 10 → 8,
  description too long 203 → 163; aptitude.co.uk 14 → 11, 3 → 2, and description too short 1 → 0; Jitsunami's title too
  short 6 → 3, the rest unchanged.
- `AnalysisFreshness.VERSION` is **12**: every older analysis reads as out of date.

**Checks on the merged `35d4888`: all green.** The engine verify and the scripted app verify ran on `3721374`; the two
later commits touch only `app/e2e`, and everything from the final e2e on ran on `35d4888`.

| Check | Result |
| --- | --- |
| `verify-engine.sh` | Build passed; tests 1,648 + 286 + 28 passed. |
| 100k bench | Every planted page and issue, nothing else; memory flat (−11 MB), peak 199 MB. |
| Readbench gate | Passed (on a quiet machine). |
| `verify-app.sh` | Every step exit 0 but unit, where one start-up test timed out under load; it passes alone and in the full rerun (3,965 passed). Stories 544 × 2 themes clean plain, forced and reduced. |
| e2e | 259 passed, 3 skipped, after the five updated expectations above (each reproduced alone first). |
| `e2e:filled` | 31 passed. |
| Release app | `tauri build --no-bundle` passed; `smoke:shell` (19 + 9) and `check:orphans` passed. |
| Linux in Docker | clippy clean; cargo test 136 + 3 passed. |

**Checks on the merged `41b2c23`: green.** Its tree is identical to `ui/3b-pixels`' `510e6fe`, where everything ran:

- engine: build, tests 1,692 + 287 + 28; the 100k bench (every planted page and issue, nothing else, peak 202 MB,
  flat); readbench 0 of 22 gated reads over their gate;
- app: `verify-app.sh` every step exit 0 (unit 3,988, scripts 54/54, e2e 260 passed, 3 skipped); `e2e:filled` 31;
- stories: one story, `screens-overview--mock-crawl`, fails in a fresh worktree because its sample data
  (`app/design/data/fixtures/`) is git-ignored and never generated. It fails the same way on `50b2338`.

`app/src-tauri/` is unchanged, so neither the Linux check nor `smoke:shell` was run again.

**Designed, not built yet:**

- **The fix loop:** [v2-fix-loop.md](v2-fix-loop.md) on `ui/3b-fixloop` (`5cf1c67`), API marked "(fix loop)" in its
  copy of v2-sidecar-api.md. The orchestrator accepted all eight open decisions:
  - a recheck updates the same crawl file (marked "rechecked N pages on" and the date) and analyses it again in full;
  - the earlier crawl is analysed again with today's rules (Advanced asks first);
  - URLs no longer linked get a new skip reason, and their rows are kept;
  - new links found by a recheck are fetched one step deep;
  - only the latest crawl can be rechecked, and not while the same site is crawling;
  - rows match by exact URL, else only counts compare;
  - "previous" is the newest earlier crawl of the same site with results, preferring the same start URL (a stopped crawl
    compares as partial);
  - a recheck takes at most 1,000 URLs, most important first; `orphan-page` and `no-https` need a full crawl.

  Its stages, each sized for one agent: E1 test-site revisions, E2 the compare reader and `si compare`, E3 the recheck
  engine, E4 robots.txt, site versions and sitemaps, S1 and S2 the sidecar, U1 Simple, U2 Advanced, V the verify.
- **The new checks:** [v2-new-checks.md](v2-new-checks.md) on `ui/3b-newchecks` (`a91ae87`): 84 new checks (1 Error,
  18 Warnings, 65 Notices), 8 changes to existing ones and 27 not built, in nine batches built one after another. The
  orchestrator's rulings:
  - `title-same-as-h1` is dropped (Google recommends they match);
  - `sitemap-over-limit` is a Warning, not an Error;
  - `readability-low` applies to English pages only, as a Notice;
  - `deep-page` uses the crawl's recorded depth, and the bench's expected count is computed.

  Batch 9 makes `og:image` a link kind, which moves the link-count tests. Its change C8 (pixel widths) is built and
  merged (above). Its copy on `ui/3b-newchecks` still says 561 px; 580 is the ruling.
- **Pixel widths:** built and merged (above). The design and its research are in
  [`handoff/2026-11-17/pixels-design.md`](handoff/2026-11-17/pixels-design.md). Title too long is 580 px rather than
  Screaming Frog's 561: 580 is the measured cut, and 561 flags 97 garden titles to 580's 69.

**Settings against the old app:** every setting is here or was dropped on purpose (v2-ui-discovery.md, "Crawl
settings"), proxy included: one address field for `http`, `https` or `socks5://user:pass@host:port`, with a Test
button. Still to fix:

- there is no consent step before a proxy or header secret is saved to Windows Credential Manager
  (`client.ts:179-182`, `:238`; `secrets.ts:152-155`);
- the words say credentials "aren't kept" (`words.ts:62`, the toast in `NewCrawlScreen.tsx:231`), but they are kept;
- SOCKS5 with a login is untested.

The defaults stay Standard with no rate cap, because the slow start and AIMD now protect the site.

**Left in step 3b, in order:**

Items 1 and 2 are done: the installer from `35d4888` passed `smoke:tauri` and upgraded the owner's install, and
`41b2c23` merges pixel widths.

**Since 2026-11-17 evening, at the owner's word ("Can we do this any faster?"), items 3 to 5 run side by side.** Three
lanes, each in its own worktree off `ui/3b-pixels`:

- `ui/3b-views`: item 3, all of it;
- `ui/3b-fixloop-engine`: item 4's E1 to E4;
- `ui/3b-checks`: item 5's batches 1 to 3.

Their briefs (`lanes-common.md` and `lane-*-brief.md`) are in the session scratchpad's `audit3b/`, with a copy kept in
the handoff folder at each merge. Each lane runs its own tests. The long gates (readbench, the full e2e, `e2e:filled`,
the installer) run once per merged round on a quiet machine. At merge, the orchestrator:

- renumbers `AnalysisFreshness`;
- adds the fix loop's `IssueDefinition.Recheck` to the new checks, from the checks lane's table.

Merged from the lanes so far (local; each lane's own verify green, the round's long gates still to run on a quiet
machine):

- **`8b6d891`: new checks, batches 1 to 3** (`ui/3b-checks` at `ffc7204`).
  - 35 checks: indexing and canonicals, links and crawlability, the head and served files. The catalog is 122, and
    `AnalysisFreshness` is 13.
  - Older crawls skip the checks whose facts they don't store ("earlier version").
  - The real-site audit re-parsed the three crawls' stored HTML.
    - Garden: `canonical-to-first-page` 3, `non-indexable-inlinks-only` 9, `pagination-to-non-indexable` 2, all
      correct. Every other new check is 0.
    - Two wrong flags were fixed first: `charset-invalid` for a late meta charset when the header already says utf-8,
      and `uncrawlable-links` for links that lead nowhere.
- **`7e36b8a`: the fix loop's engine, E1 to E4** (`ui/3b-fixloop-engine` at `e208ece`; as built in
  [v2-fix-loop.md](v2-fix-loop.md) §12).
  - Test-site revisions, `si compare`, rechecks (`si recheck`) of URLs, robots.txt, sitemaps and site versions.
  - `IssueDefinition.Recheck` is required on every check.
  - The invariant test: on revision 1, a recheck matches a full crawl for all 122 checks. A recheck never says
    "fixed" where a full crawl says "still". A duplicate is fixed only when its own text changed, and its whole stored
    cluster is fetched again.
  - Verify on `e208ece` (this merge's tree): engine 1,971, CLI 34, sidecar 287; bench and readbench exit 0.

- **`1602893`: the fix loop in the sidecar, S1 and S2** (`ui/3b-fixloop-engine` at `4886cac`).
  - Library items carry `siteKey`, `previous` and `changes` from cached summaries, without opening a file.
  - Endpoints: `/changes`, `compareWith` on groups and rows, `/fixed`, and `recheck` on `/api/checks`.
  - The report's "Since your last check"; the workbook's "Fixed since" sheet.
  - Rechecks: plan, start, reads, the `rechecking` status, events, `needs-secrets`, Stop, and the refusals
    (`site-busy`, `recheck-not-latest`).
  - A duplicate is fixed only when its own text changed. Unchanged text that is unflagged only because other pages
    changed is "not checked".
  - Verify on the same tree: engine 1,976, CLI 34, sidecar 315, bench exit 0.
  - Readbench missed one timing gate under the lanes' load: a links search for a common word took 2,529 ms against
    2,500, where it took 1,281 ms at E4. It is to be rerun on a quiet machine.
- **`1b714f1`: new checks, batches 4 to 6** (`ui/3b-checks` at `a5e4d53`): sitemaps and robots.txt (changes C2, C4),
  structured data (C5), images.
  - The real-site audit, all correct:
    - garden: `robots-txt-no-sitemap` 1, `image-legacy-format` 240;
    - aptitude: `image-legacy-format` 28, `image-alt-unhelpful` 7, `image-missing-src` 1, `image-hotlinked` 1.
  - Two wrong flags were fixed first: `structured-data-schema-errors` on a `www.schema.org` context, and
    `image-alt-unhelpful` on a logo whose alt is its plain name.
  - The merged tree (S1 and S2 with batches 4 to 6) was built and tested: engine 2,081, CLI 34, sidecar 315; app
    typecheck, unit 3,994.

- **`40f5e6b`: the owner's UX audit, screen and settings fixes** (`ui/3b-views` at `9bf5bd3`).
  - Issues sorted by what matters (Fix first, Most URLs, Most points, Severity, Category; remembered per mode), with
    the evidence above Why and How.
  - Related checks as tested catalog data (`IssueDefinition.Clears`) with computed counts: "Fixing this also clears …"
    in both modes, folded in Simple's What to fix.
  - Headlines as things to change, and how many are in the template.
  - Links grouped by target, with a grouped export, an Images preset, file-name labels and thumbnails (the broken
    glyph only for images that failed), and the inspector's Images tab.
  - robots.txt blocked URLs with Linked from, In a sitemap and conflicts, fitting at 960 px.
  - Secrets go to Windows Credential Manager only when the owner ticks Remember; the words are truthful; SOCKS5 with
    a login is tested.
  - Checks on this merge's tree: engine 2,107 + 323 + 34; bench and readbench passed; unit 4,214; stories 571; e2e
    275 passed.
- **`8ca8a53`: new checks, batches 7 to 9; the catalog is 170** (`ui/3b-checks` at `68dcdd52`).
  - Security and URLs, headings, content and hreflang, Open Graph and X cards. `title-same-as-h1` is not built, as
    ruled.
  - `heading-levels-skipped` judges from the first H1 on (the orchestrator's ruling: hidden theme headings before
    the H1 aren't a skipped level).
  - `hreflang-unlinked` flags pages only.
  - Share-text duplicates never flag the page whose text is its own.
  - The real-site audit was all correct after those fixes: HSTS under a year, cookies without Secure, heading jumps,
    lorem ipsum, X cards, `og:url` on page 2s.
  - `AnalysisFreshness` is 14.
  - Checks on this merge's tree: engine 2,190, sidecar 323, CLI 34; bench ok; unit 4,214; stories 571 × 2; e2e 13
    specs.
  - Readbench missed two timing gates under load (the links search for a common word, 2,694 ms against 2,500; pages
    by response time, 199 ms against 185). Batch 9 adds a share-image link from every page.
- **`eeaeb07`: the headline leads with what to fix** (`ui/3b-views` at `b05c7b5`): errors and warnings as things to
  fix, notices apart as worth a look; long file names in the inspector.
- **`31b9112`: never a count over an empty list, and one unit per mode** (`ui/3b-views` at `b7a221b`).
  - An empty 4xx list on a screenshot came from Simple's fake sidecar. The real sidecar listed all 9 pages, with the
    pages linking to each, on copies of the garden crawls.
    - A grouped check whose groups come back empty now lists its URLs.
    - A sidecar invariant over every check fails any count with no rows under it.
  - Advanced counts things ("About 77 things to fix · about 775 more worth a look"). Simple counts problems ("6
    problems to fix and 22 more worth a look").
  - Checks at `b7a221b`: 573 stories × 2 themes clean.
  - The views lane is finished.
- **`5e327f3`: the fix loop in the app, U1 Simple and U2 Advanced** (`ui/3b-fixloop-ui` at `c65fe6d`; tree identical
  to the verified `fce0cde6`).
  - Simple:
    - each line then and now on Home, Summary and the score breakdown;
    - the recheck card in every state;
    - What to fix's change lines and Fixed group;
    - causes marked "Still there" or "New since".
  - Advanced:
    - the Library's score column and Compare with…, with a picker kept per crawl;
    - Overview's comparison and Rechecks cards;
    - Issues' change column, filters, sort, and Now and Fixed tabs;
    - Recheck in Issues, URLs and the inspector.
  - Both modes:
    - "Check the site again" for every check a recheck can't settle (a test covers the catalog);
    - "not checked" is never shown as fixed;
    - counts are in each mode's headline unit.
  - Decisions and what isn't built are in [`v2-fix-loop.md`](v2-fix-loop.md) §12.
  - Checks at `fce0cde6`: unit 4,674; 635 stories clean in both themes, in forced colours and in reduced motion; e2e
    284 passed, 3 skipped; `e2e:filled` 33 passed.

- **`77d20e7`: every class the app writes compiles to CSS** (`ui/3b-classes` at `4a6bfed`; found while reviewing U2's
  shots).
  - The theme is closed, so an off-scale utility compiled to nothing, silently. The guard test,
    `app/src/styles/classes.test.ts`, failed on 30 uses: `max-w-72`, `basis-80`, `pl-11`, `text-text-link` and others.
    It now fails on any class with no CSS, naming the file and line. It can't see interpolated or passed-on classes:
    138 of 5,678 uses.
  - Each one is fixed with a named token:
    - Advanced's check head puts Recheck, Copy URLs and Hide on one row, with the plan line beneath.
    - Simple's flex bases work again (the Summary's name was squeezed at 200% zoom, and the recheck card at 960 px).
    - Fold bodies start under their title.
    - Dead `si-*` names are removed.
  - Visible changes the owner hasn't seen yet:
    - Crawl options' folds indent their switches.
    - Earlier crawls are indented under their site.
    - Two captions are now regular weight.
  - Checks: Vitest 4,694; 232 stories, axe clean in both themes, in forced colours and reduced motion; e2e 10 specs, 53
    passed.
- **Real sites, at the owner's word (2026-11-18):** a fourth Jitsunami crawl, and rechecks on a copy of the garden
  crawl. Both are reported in the [`handoff/2026-11-18/realsites-ready.md`](handoff/2026-11-18/realsites-ready.md).
  - **Jitsunami, crawl 4:** 62 URLs, 9 pages, none blocked. The same 7 products and `/collections/belts` were still not
    checked: Shopify kept refusing for the whole 20-minute cap.
    - One recheck of the products, 15 minutes later, read 1. The site refused again.
    - Score 97. The 3 points are the new `placeholder-text` check, and its flag is correct: the theme's lorem ipsum
      shows in the blog sidebar.
    - All 7 checks that fired on the product pages it read are correct. They come from the theme or the platform.
  - **Garden rechecks:**
    - The broken pages: 0 fixed, 8 still, in 7 s.
    - Duplicate titles: 2 still.
    - Both are right: the site wasn't changed.
    - The robots.txt recheck failed. The fix lane below takes that up.
- **Found by the real-site run, fixed on `ui/3b-recheck-fixes`:**
  - B1: rechecking sitemaps fails on any crawl made before `sitemap_urls.lastmod`, which includes every 17 Nov crawl.
  - B2: a failed recheck leaves the file half-written (all 240 sitemap entries deleted), and the analysis still runs.
    `si compare` then reports two checks as fixed that weren't.
  - B3: the recheck summaries mislead ("Rechecked 47 URLs" for 7 asked; "refused again" never said).
  - B4 and B5: the 429 words and the cap's last try.
  - B6: Microdata `content` on any element.
  - B7: repeated notes.
  - Session cookies counted as new on a recheck.
  - The owner's installed app predates the fix loop, so none of the owner's files can have been touched.
- **Merged e7a5a578 (`ui/3b-recheck-fixes` at 0c866ef):** all of the above, each with a test seen failing first.
  - B1: one shared schema upgrade (`Storage/CrawlSchemaUpgrade`) for a new crawl, a continue and a recheck, plus a
    guard test that fails when a column is added without it.
  - B2: a recheck is one transaction. A failure leaves the file as it was, and nothing is analysed.
  - B3: the CLI says asked, read, refused again (and why) and never asked. The app says "the site turned them away
    again" in Simple and "refused again: 429" in Advanced.
  - Cookies are judged by name across the site. `AnalysisFreshness` is 15.
  - A rerun of the garden recheck against a local copy found one more bug: a robots.txt or sitemap read that got no
    answer wiped the sitemap entries and said "fixed". It is now "not checked", and the crawl's data is kept.
  - Checks: engine all passed (one new pace test was timing-sensitive under load and is being made deterministic);
    Sidecar 325, Cli 36; bench flat (210 → 183 MB); Vitest 4,701; e2e fix loop and inspector 26.
  - **Round 2, merged 9ae271a8 (64db26d):**
    - A check whose column an upgrade added but the crawl never filled reports "not run", not 0. The table
      `uncollected_facts` lists each sitemap whose `lastmod` was never read, and a recheck that reads it clears it.
    - A robots.txt or sitemap recheck reads only the site's own origins that the check's issues name (it had asked
      instant.page).
    - The B5 test can't fail under load.
    - Checks: engine 2,227, Sidecar 325, Cli 36, all passed; bench flat; readbench met every gate.
  - **For phase B:** paused time still counts toward the 20-minute cap.
- **Jitsunami, crawl 5, at the owner's word (2026-11-18, `-c 1 --rate 1 --no-external`):** 46 URLs, 9 pages, the same 8
  not checked, 20:47. Shopify refused the products from about 35 s in, after roughly 25 requests, and for the whole
  cap. Going slower doesn't get an unsigned crawler through.
- **Shopify, researched (2026-11-18).** The brief, with sources, is
  [`handoff/2026-11-18/lane-shopify-brief.md`](handoff/2026-11-18/lane-shopify-brief.md).
  - Shopify limits crawlers "most strictly when unsigned", storefront pages included since 30 May 2026. The sanctioned
    way through is a crawler signature (Web Bot Auth): the store owner makes one under Online Store › Preferences ›
    Crawler access, one domain each, for up to 3 months. Screaming Frog, Sitebulb and Semrush all take one.
  - No reputable tool rotates user agents, headers or proxies to get past it, and Shopify treats that as evasion. We
    don't build it.
  - The owner (2026-11-18): "The app should be smart enough to tell the user if it's a Shopify site and it's being
    rejected and how to fix it."
  - **Merged 324220b3 (`ui/3b-shopify` phase A at 237e15c):**
    - The signature goes only over https, only to the exact host and port it was made for, and only until it expires.
      It never crosses a redirect to another host, and never goes to the www/bare twin without its own. Crawl files,
      exports and logs never keep it.
    - Shopify detection: every Jitsunami response had `powered-by: Shopify`, and every page had `Shopify.theme`
      (`x-shopid` never appeared). It's stored in `site_platform`.
    - Refused pages are not checked, with Shopify's reason, never broken and never in the score. A signed refusal
      names the likely cause: made under an hour ago, expired, or for the www/bare twin.
    - `--signature-file` on crawl and recheck.
    - The app has Shopify crawler access in the crawl options in both modes, and one live alert with Shopify's own
      button names. A signature is added mid-crawl by stopping and then continuing. The Summary, problem page and
      Overview prompt leads to "Recheck these N pages". The next crawl of the site opens the part with "Last time
      Shopify refused N pages".
    - Checks: engine 2,325, CLI 45, Sidecar 329; bench flat; readbench passed; Vitest 4,756; e2e 31; stories
      axe-clean in all four modes.
    - There's no app e2e for the signed flow, because the sidecar can't trust the test store's certificate. The
      engine tests cover it.
  - **Phase B, under way on the same branch:**
    - stop early on an unsigned store (one retry after `Retry-After`, then finish);
    - paused time doesn't count toward the cap;
    - Shopify words in the end notice;
    - cookies and security headers that Shopify sets are marked "Set by Shopify", out of the score;
    - Simple's alert opens the form in Simple;
    - a remembered signature fills in the next crawl of the host.
  - The owner will add the Jitsunami signature in the app at the end. It's never pasted in chat.
- **The "can I fix it from this?" pass (the owner, 2026-11-18).** "Not just display random data that doesn't help you
  find where the issues are."
  - Phase 1 (done): three auditors opened every check that fires on copies of the garden, Aptitude and Jitsunami
    crawls and on the synthetic site, in both modes. Findings are in the scratchpad, `fixit-{links,page,index}/`.
  - Phase 1 found 3 false or wrong pointers:
    - `broken-fragment-links` names the working link;
    - `image-no-srcset` flags `data-srcset`;
    - `heading-too-long` shows the H1 on H2 rows.
  - Other blockers:
    - rows that "forward to" themselves;
    - Simple telling a robots.txt block to "remove the links";
    - `hreflang-inconsistent-code` hiding the code;
    - Advanced cramped at 1440 px.
  - Page counts agree across every view.
  - **Phase 2, all merged:**
    - index: cee3122e;
    - Shopify phase B: dcf3c824;
    - page: 6f76807a, plus the Shopify card's placement in 35af0751;
    - links: 82eed758, plus a polish round in 7083415c.
  - `AnalysisFreshness` is 20.
  - What changed:
    - every Simple check in plain words, held by a whole-catalog test;
    - the fragment link and `data-srcset` false flags fixed;
    - headings quoted;
    - Copy the list hands over every image;
    - site-wide checks name their file;
    - sitemap counts agree with the checks;
    - `blocked-by-robots` grouped by rule;
    - below 1,600 px the check keeps the room, and the list folds behind "All checks";
    - a check's only group opens;
    - group rows are real columns;
    - the Links presets agree with the checks;
    - Fix first treats a 4xx and its broken links as one job;
    - Shopify's own cookies and security headers read "Set by Shopify", out of the score;
    - a repeated header (`nosniff, nosniff`) counts once;
    - one canonical loop is one group.
  - Left over, all small:
    - the flat Links grid doesn't remeasure its columns after fonts load;
    - the Sitemaps "Found via" column is still cut;
    - no `si testsite` options for robots/sitemap-missing or the limits;
    - `duplicate-site-version` is planted only on the HTTPS synthetic site.
- **Jitsunami, crawl 6, signed (2026-11-18, 17:13, at the owner's word, with the signature the owner made):**
  - Settings: `-c 1 --rate 1 --no-external`, built from 35af0751.
  - Result: 253 URLs, 22 pages, 0 refused, 4:23. Unsigned at the same speed it was 46 URLs, 9 pages, 8 refused, 20:47.
  - All 7 products and `/collections/belts` read, with status 200.
  - The signature was in neither the log nor the crawl file.
  - New real findings: `/collections/mens` returns 404, and a banner image is missing.
- **The round's gates, at d1c98c8d (7083415c plus a smoke-script fix), on a quiet machine: all pass.**
  - Engine 2,398, Sidecar 331, Cli 45.
  - The 100k bench is exact, and memory stayed flat (209 → 198 MB).
  - Readbench: all 23 gates met; the links search took 1,239 ms against its 2,500 ms gate.
  - Typecheck, lint, format and the scripts pass.
  - Vitest: 5,482. `main.test.tsx` timed out once at 5 s and passed alone, so it's flaky under load, as before.
  - Stories: 665 × 2 themes, clean plain, in forced colours and in reduced motion.
  - e2e: 291 passed (3 skipped); `e2e:filled`: 33 passed.
  - `tauri build --no-bundle` passed, and so did `smoke:shell`, after two stale expectations in its script were fixed.
  - The installer (unsigned) passed `smoke:tauri`.
  - Linux Docker: clippy clean, cargo test 136 + 3.

- **Gallery 7 found seven defects, all fixed (2026-11-19):**
  - beb2c78d: inspecting with "All checks" open folds the list away; Sitemaps' "Found via" shows whole.
  - f4852d4c:
    - a page refused on recheck kept nothing and so looked fixed ("Nothing wrong"). Now it keeps its record, links and
      issues, and never counts as fixed;
    - Simple counts addresses held back in a slowed section, and never says "Nearly done" while they wait;
    - the section limit is said once;
    - a recheck from the Shopify form says what it read.
  - 3aa35e0a: the report numbers only the jobs, and broken links fold under the 4xx.
- **Re-gated at 3aa35e0a, all passing first time:**
  - Vitest 5,501; e2e 292 (3 skipped); `e2e:filled` 33;
  - stories 665 × 2 themes, clean in all four modes;
  - `smoke:shell` 19 + 9; the installer (unsigned, 47,624,107 bytes) passed `smoke:tauri`.
  - The engine (2,402, 331 and 45 tests, plus bench and readbench) was green on the same engine code in the fix lane.
- **Gallery 7.1 published** (version 9) at <https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR>: 69 views × 2
  themes, with 24 new for 3b, all shot on 3aa35e0a.
- **The owner's install was upgraded** to 3aa35e0a with setup `/S` while it was closed (2026-11-19, 04:53).

**Step 3b is finished.**

Next:

- **The owner's walkthrough:**
  1. Open the app, and the owner adds the Jitsunami signature in the Shopify form.
  2. Run one signed crawl in the app, together.
  3. Delete `%USERPROFILE%\.shouting-iguana\shopify\jitsunami.com.txt`; the owner deletes the signature in Shopify.
- The release waits for the owner's word. Any further real crawl or recheck needs the owner's word again.

1. ~~The installer from `35d4888`, `smoke:tauri`, and the owner's install upgraded with setup `/S` while the app is
   closed.~~ Done.
2. ~~Pixel widths (`ui/3b-pixels`, off `ui/step-3`), before the new checks.~~ Done (`41b2c23`).
3. The view fixes from [`ux-views.md`](handoff/2026-11-17/ux-views.md):
   - the Issues check list sorted by what matters;
   - related checks ("fixing this also clears …");
   - headline counts as things to fix;
   - Links grouped by target, with an Images preset;
   - the inspector's images with their sizes;
   - the robots.txt-blocked list with "linked from" and "in a sitemap".

   Also the settings fixes above.

   Done (`40f5e6b`, `eeaeb07`, `31b9112`).
4. The fix loop, stages E1 to V (designed on `ui/3b-fixloop`). Done to U2 (`7e36b8a`, `1602893`, `5e327f3`). Stage V
   is the round's gates and gallery 7.
5. The new checks, batches 1 to 9 in order (specified on `ui/3b-newchecks`). Done (`8b6d891`, `1b714f1`, `8ca8a53`).
6. ~~Audit Jitsunami's product pages again once a crawl reads them.~~ Done 2026-11-18 on the two product pages that
   could be read: every flag was correct. Shopify still refuses the rest for longer than the 20-minute cap.
7. Gallery 7, then tell the owner step 3b is finished. The release step follows at the owner's word.

**Merged 2026-11-15: the framed workspace and where each URL was found** (`66f5c46` merges `ui/framed`, which holds
`ui/framed-settings`; `ba72f75` merges `ui/found-on`; local only). All of it came from the owner's first use of the
installed app:

- **The framed workspace.** The owner found the shell "wishy washy, … no clear segmentation", and said the accent rule
  on the current item "looks AI made". Of three mocked directions (canvas
  <https://claude.ai/artifact/4srqyFoFVHsEmzeTrqTRBx>) they chose B. What changed:
  - the sidebar sits on a grey frame, and the top bar, screen and inspector form one bordered inset panel;
  - the crawl at the top of the sidebar is now a working switcher (recent crawls, then Home);
  - `.si-current` is a neutral fill and bold text everywhere, with no accent rule or accent text;
  - the grid's selection keeps its tint but loses the rule;
  - cards have stronger borders and ruled headers, and the Overview's figures are separate cards.

  Design and reasons: [v2-ui-design.md](v2-ui-design.md#the-framed-workspace-2026-11-15). Build notes:
  v2-ui-architecture.md, "The framed workspace".
- **Settings are the full Settings in both modes.** The owner: the modes are "just for the UI … not for settings".
- **Wide windows.** Overview, Home and Simple's capped screens grow with the window (`--content-max` 1,136 → 1,800 px).
  The "offset Overview" the owner saw was not a bug: it was a cropped shot of a maximised 4K window at 150%.
- **Where it was found.** The owner: "when we show 404 links or 302 … it needs to show the page that they were found
  on". Each check declares `IssueDefinition.References`: 13 checks list `sources`, 17 list `targets`, and the rest
  `none` (the full list is in [the IA](v2-ui-ia.md#where-its-found-uifound-on)).
  - The engine adds `FoundOnReader` and two reads, `POST /urls/found-on` and `POST /issues/{checkId}/targets` (up to
    100 URLs a call).
  - The data appears in Advanced Issues ("Found on 3 pages · /x/ +2", which expands to the sources), in the
    source-side checks ("Links to /old/ (404) +2"), in Simple ("Linked from …"), in exports ("Linked from" columns)
    and in the report ("Where to fix it").
- **Open in the browser.** Every listed URL has an open button, Shift+Enter opens the cursor row's URL, and the row
  menus have "Open in browser".

**Checks on the merged `ba72f75`: all green.** Neither merge touched `app/src-tauri/`, so the Linux check was not
needed.

| Check | Result |
| --- | --- |
| `verify-engine.sh` | Build passed; tests 1,498 + 286 + 28 passed. |
| 100k bench | 101,114 of 101,114 pages and only the planted issues; memory grew 1.9 MB, peak 191 MB. |
| Readbench gate | Passed. |
| `verify-app.sh` | Every step exit 0. Unit 3,868 passed. Stories 531 × 2 themes clean plain, forced and reduced (the first story runs since framed's last CSS fix). |
| e2e | 252 passed; 2 skipped (the measurement specs, which run only with `SI_E2E_MEASURE`). |
| `e2e:filled` | 31 passed. |
| Release build | Sidecar and NSIS installer built (`Shouting Iguana_2.0.0-alpha.1_x64-setup.exe`, 46.0 MB, unsigned). |
| Shell smoke | `smoke:tauri`, `check:orphans` and `smoke:shell` passed (19 + 9). |

The branches' own checks, and the review shots both agents made (in the session scratchpad's `framed/` and
`found-on/`), are in their commits (`42b6938`, `88f4c4b`).

**Merged 2026-11-16: gallery 6's fixes** (`b6b6e65` merges `ui/gallery6-fixes`; local only). Gallery 6 (published
2026-11-15) showed four faults. Each fix has a test seen failing first:

- **The switcher** (view 23) names each site as Home does, with its own port, and says the state of a crawl that didn't
  simply finish, in the mode's words ("Interrupted", "Stopped"), so two crawls of a site in the same minute differ
  (`24d47c2`).
- **Sitemap names** (view 32) show whole. Each count's column is as wide as its name on two lines and its widest count,
  not a count in the millions (`eeb6f6c`).
- **Simple Home** (view 02): Your sites shares its columns as a subgrid, so the site's name takes the room its row has
  (`cb010b8`).
- **A check's affected URLs** (view 25), up to 100 rows, are drawn whole and scroll with the pane: one scroll bar. A
  grid's focus ring is drawn by its frame, over the rows and the scroll bar, so it shows whole (`0b7eba5`).
- **Found by the re-shoot:** a sitemap's Status ("Couldn't be read: 404 Not Found") was cut at every width, version 6
  included. It now grows into the room the address leaves past 300 px. It is whole from about 1,440 px, and at 1,280 px
  the names come first (`4ed2f5a`).
- Three older e2e tests asserted the old behaviour and were updated (`64280cf`).

**Checks on the merged `b6b6e65`: all green.** Only `app/src/` and `app/e2e/` changed: no engine or `app/src-tauri/`
change, so neither `verify-engine.sh` nor the Linux check was needed.

| Check | Result |
| --- | --- |
| `verify-app.sh` | Every step exit 0 but two one-offs, both clean when rerun on a quiet machine (below). Stories 531 x 2 themes clean plain, forced and reduced. |
| Unit | 3,880 passed. The verify run had one timeout (Simple Home not drawn within 1 s while the gallery's images were being optimised); the file passed 5 runs of 5 and the whole suite on the rerun. |
| e2e | 255 passed and 2 skipped. One test met the preview server refusing a connection; its spec then passed 15 of 15 (`--repeat-each 3`). |
| `e2e:filled` | 31 passed. |
| Release build | Sidecar and NSIS installer built (`Shouting Iguana_2.0.0-alpha.1_x64-setup.exe`, 46.0 MB, unsigned). |
| Shell smoke | `smoke:tauri`, `check:orphans` and `smoke:shell` passed (19 + 9). |

Also done on 2026-11-16:

- **The owner's installed copy is upgraded** to `b6b6e65`: setup `/S`, in place in `%LOCALAPPDATA%\Shouting Iguana`,
  while the app was closed. The installed sidecar's hash matches the build's.
- **Gallery version 6.1 is published** at the same link, <https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR>:
  - views 02, 23, 25 and 32 are re-shot on the fix branch's release build, from the gallery's own run folder;
  - their captions and "Seen in these shots" are updated;
  - the site is in `.claude/worktrees/_gallery/site/`, and the v6 backup is in the session scratchpad's
    `gallery6-fixes/site-backup-v6/`.
- **A gotcha:** run the app's unit tests from a `C:/` path. From the lowercase `c:\` working directory the editor gives
  a shell, every file fails with "Vitest failed to find the runner", because Vitest loads twice. `verify-app.sh` is
  safe, as it changes to the folder it is given.

**Left, in order (as of 2026-11-16; step 3b's own list is above):**

1. Step 3b: the check audit on the three real sites above, at the owner's word. It crawls real sites, which the agents'
   rules otherwise forbid.
2. The release step (signing, licences, updater secrets, CI on all three OSes), at the owner's word. Merging to
   `master` or releasing still needs an explicit yes.

Still for the owner's eye: at 1280x720 with an export running, the sidebar scrolls and the export's row sits half below
its fold.

**Step 3 finished 2026-11-15, before the owner's feedback above.** Polish 2 was merged (`9be9c71`) and verified:
`verify-app.sh` all green, e2e 233, `e2e:filled` 30, `smoke:shell` 19 + 9. Gallery version 5 was published (42 views).

That day the owner's machine got an install from `06677e9`, per-user in `%LOCALAPPDATA%\Shouting Iguana`, with crawls
in `Documents\Shouting Iguana`. The install found a bug in `smoke-installer.ps1`, fixed in `c4a69dd`. Tauri's
uninstaller leaves the remembered folder (`HKCU\Software\Aptitude\Shouting Iguana`), so the next real install went into
the test's deleted temp folder. The test now restores that key as it found it, and refuses to run beside a real install.

**Where it stood before the pause (2026-11-15): round 5 is merged into `ui/step-3` (`d6ea51a`) and the whole of it is verified.** On its code
(`a8f8acc`, which differs from the merge only in docs): `verify-engine.sh` (build, tests 1,489 + 285 + 28, the 100k
bench, readbench gate) and `verify-app.sh` (every step: unit 3,734, stories 528 x 2 themes plain, forced and reduced
clean, sidecar, e2e 226) exit 0; `e2e:filled` 29; cargo fmt, clippy, test 139 + 3; round 5's six specs x5; unit
under load; the release build with `smoke:shell` Advanced 17 + Simple 8; Linux in Docker (clippy, cargo test 134 +
3). The verify found and fixed one engine bug from before round 5 (a running crawl's URL grid narrowed to a links
view's ends counted against an older URL total). Every closing-audit gap and both reviews' findings are fixed.
**Gallery re-shot and published** (2026-11-15, version 3, 42 views x 2 themes, release shell at `9bbd588`): six of
the 2026-11-14 defects confirmed fixed in the real shell; it found ten more, two of them round 5's G6 (the Report
frame keeps Windows' classic scroll bar, and the subtitle still wraps) that passed in Chromium and fail in WebView2.
**The polish round is merged** (`2b1229f`, pushed; from [`brief-polish.md`](handoff/2026-11-15/brief-polish.md)):
all ten fixed and proved in the release shell with before/after shots (scratch `a539aa23-…/r5-polish/shots/`), each
with a test seen failing first, and new `smoke:shell` steps that failed on the old build (Advanced 19 + Simple 9
pass). Causes worth knowing: the top bar's flicker was reduced motion (0.01 ms transitions made every resize a tiny
animation, so the give-way measure read stale widths), not WebView2; the Report frame's scroll-bar style was refused
by the engine's own report CSP (the app's style now goes first in the frame's copy; the saved file is unchanged).
Checks: `verify-app.sh` all exit 0 on `5cb952c` (unit, stories 528 x 3 clean, e2e rerun 228 after updating two toast
specs), `e2e:filled` 30, cargo fmt, clippy, test 141 + 3 on the final commit, Linux in Docker on `2b1229f` (clippy,
cargo test 136 + 3); the engine untouched. Not verified in the shell: the "stopping" top bar (under a second), the
updater's untrusted, unreadable and unavailable cases, Advanced's toast changes (unit tests). The gallery's twelve
changed views were re-shot and published (version 4); they showed four small things still wrong (the inspector's
Inlinks tab cut beside "7 more"; the sidebar 7 px taller than 720 px with a crawl paused and an export running; the
Library badge squeezing the address to "http:…"; Simple Home's stopped row wrapping at the dot) and the drop
overlay's shot still showing the old wording. **Left: polish 2** (`ui/r5-polish2` from
[`brief-polish-2.md`](handoff/2026-11-15/brief-polish-2.md); the same agent re-shoots the five views into
`_gallery/site/`; scratch `a539aa23-…/r5-polish2/`), then merge, publish the gallery, and tell the owner step 3 is
finished; step 3b follows. Known and left: the tray shows "Shouting Iguana" for a few ms between a check's end and its analysis;
a real screen reader not tried; installing a real update needs a release with `latest.json` (the release workflow's
secrets are the owner's to add: see the architecture doc). The account of how it got here follows (2026-11-13 on).

**Stopped for the day on 2026-11-13 (evening), at the owner's word** ("merge and push so everything is on github,
then get all docs up to date so I can continue tomorrow"). **Every screen of both modes is built, reviewed, fixed
and merged into `ui/step-3` (`8fd6c66`, pushed).** Round 4 went in that day: integrated, reviewed with fresh eyes
(20 findings, no defect at the seams), 19 fixed with a test seen failing per fix and one judged moot, merged. Every
local `ui/*` branch is on GitHub. What's left is checking the merged whole, the release build's smoke test, the
closing audit and the gallery. No agent is running.

**A new session needs** this section and the folder [`docs/handoff/2026-11-13/`](handoff/2026-11-13/): every brief
(`brief-*.md`; round 4's review and fixer are `brief-round4-review.md`, `brief-round4-fixes.md`), every review's
findings with the orchestrator's decisions (`reviews/`; round 4's is `round4-review-findings.md`), each agent's
checkpoint (`checkpoint-<name>.md`), the orchestrator's logs (`checkpoint.md`, and `checkpoint-session-2.md` for the
evening), and `tools/verify-app.sh`, `tools/verify-engine.sh`, `tools/r4-integrate-checks.sh`. The 2026-11-11 folder
keeps `ui-slice-rules.md`, which every UI brief cites. Subagents can't be resumed across sessions: relaunch each
from its brief.

### To resume, in this order (from 2026-11-14)

**2026-11-14: step 1 is done.** `151d6dd` verified as one whole, every step green: engine build, tests (1,428 +
267 + 28), the 100k bench, `readbench --gate`; app `npm ci`, typecheck, lint, format, script tests, unit 3,437 (75
skipped), build-storybook, stories 488 x 2 themes plain, forced colours and reduced motion (no violations), the
sidecar build, e2e 188 (2 skipped); `cargo fmt`, `clippy`, `cargo test` (120 + 3). Two things seen on the way, neither
in the app's code: in the scripted run, `src/main.test.tsx`'s "shows a plain failure…" timed out once at 5 s on its
cold `import('./main')` under the suite's load (its "index.html has no #root" is that import landing after the
teardown); it passed 3 of 3 alone and in the full rerun. A full `npm test` run without the script's private TEMP then
failed to load all 328 files: the stale Vite cache below; moved aside, the suite ran green.

**2026-11-14: step 2 is done.** The release build of `151d6dd` (`build:sidecar`, `tauri build --no-bundle`), then
`smoke:shell` PASS, 16 steps, export's Open folder and Open file and the Report's print among them, and `smoke:tauri`
PASS. The first run failed at its second step: since round 4 a new profile opens in Simple, and both walks drive
Advanced's Library. They now start the app in Advanced (`advancedSettings` in `scripts/lib/shell.mjs`, written to the
run's own profile before the app starts). Not covered: Simple's screens in the real shell (Home, the checking screen,
the OS notice in Simple's words); the e2e covers them in Chromium only.

**2026-11-14: step 3 launched** on `b8684fa`: the closing audit (worktree `closing-audit`, ports 50874-50877) and the
gallery re-shoot (worktree `gallery-shoot`, ports 49874-49877, building into `_gallery/site/`), from the two briefs
unchanged, with the head and their folders (this session's scratchpad, `a539aa23-…`) given at launch. If this
section still says "launched" in a new session, neither reported: check their worktrees and relaunch from the briefs.

**2026-11-14: the gallery is done and published** (same address, version 2): 32 views x 2 themes in the release
shell at `b8684fa`, 12.4 MB, every image viewed before publishing. Defects seen while shooting, for the audit's list:
(1) the Simple | Advanced switch is cut off in the top bar while a crawl runs in Advanced at 1280 px (3 of 3 live
crawls; the bar overflows 1094 > 1056, search and Home don't give way first); (2) a Simple problem page and New
crawl let the whole window scroll (absolutely positioned `sr-only` spans with no positioned ancestor in `main`), so
the wheel over the sidebar scrolls the app off screen and the top bar loses 10 px; (3) a problem opened from a
scrolled What to fix list opens part way down, its back link out of sight; (4) the URLs row menu opens where the
previous right-click was (the first at the window's top-left); (5) Simple's finished toast is squeezed into a narrow
column; (6) the Report screen's subtitle wraps to 3-4 lines at 1280 px and its frame keeps Windows' classic scroll
bar; minor: URLs' search placeholder cut ("Search URLs an"), Advanced's sidebar says "1 crawling" for a paused crawl,
Home's "last checked" 14:51 against 14:13 in the breadcrumb (end against start?).

**2026-11-14: the closing audit is done: step 3 is close, not finished.** Its whole report, with the gallery's
defects, is [`docs/handoff/2026-11-14/closing-audit-findings.md`](handoff/2026-11-14/closing-audit-findings.md):
32 gaps, the definition of done item by item, the keyboard tables, the parity list. No screen is missing; Simple in
the real shell works end to end; axe clean in 32 runs; the 100k budgets met. The serious gaps: Simple tells a user
robots.txt keeps checkers out when it was only unreachable (gap 1); a stopped check gets no "so far" score (2); URL
and page counts differ between screens (3); Advanced Home is still the foundation version (4); Advanced words in
Simple, mostly from the shell (5-7). The brief's audit ports 50874-50877 are in a Windows reserved range: use others.
**Next (step 4 of the resume list): the owner chooses what blocks "finished"**, then a fix round from that list.

**2026-11-14: the owner's word: "Fix everything"** (every gap, the "defer" ones included). **Round 5 launched** on
`001d024`, four builders at once from [`brief-round5.md`](handoff/2026-11-14/brief-round5.md): A Simple's truth
and words and the stopped check (`ui/r5-simple`), B Advanced Home, one count everywhere, Overview and Issues
(`ui/r5-home`), C the grid, URLs, inspector and exports (`ui/r5-grid`), D the shell, frame and platform: updater,
proxy test, credential store, data folder, licences, Simple in `smoke:shell` (`ui/r5-shell`). Worktrees
`.claude/worktrees/r5-*`, scratch folders in this session's scratchpad (`a539aa23-…/r5-*`, each with a
`checkpoint.md`). Then: integrate the four (B's drop with D's shell event), a fresh-eyes review, fixes, merge, the
whole-suite verify and both smoke walks, and a gallery update. Not in this round, and not in the audit's gaps: the
native macOS menu and installers for other OSes (can't be built or checked on this machine). If this section still
says "launched" in a new session, the builders didn't report: check each branch's log and checkpoint, and relaunch
from the brief with "resume from your checkpoint".

**2026-11-14: all four parts built and reported** (condensed in
[`round5-reports.md`](handoff/2026-11-14/round5-reports.md); branches pushed: `ui/r5-simple` `7f910d0`,
`ui/r5-home` `efed126`, `ui/r5-grid` `8cb83aa`, `ui/r5-shell` `4e513b6`). **Integration launched** on `8ec31e6`
from [`brief-round5-integrate.md`](handoff/2026-11-14/brief-round5-integrate.md) (worktree `r5-integrate`,
branch `ui/r5-integrate`, scratch `a539aa23-…/r5-integrate/`). Owner to know: pages now leave out Cloudflare's
`/cdn-cgi/` endpoints (B, for one count everywhere: reverses E7's call); a crawl the user stops is analysed at once
(A); the data folder is a switch, not a move (D); the updater's signing key pair is at
`%USERPROFILE%\.shouting-iguana\updater.key` (+ `.password`), and a release needs the secrets
`TAURI_SIGNING_PRIVATE_KEY` and `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` set on GitHub (the architecture doc has the
workflow). After integration: a fresh-eyes review, its fixes, the whole-suite verify, both smoke walks, the gallery,
then merge into `ui/step-3`.

**2026-11-14: integrated** on `ui/r5-integrate` at `008b4b2` (pushed): every check green on the final commit (unit
3,684 x 4, stories 528 x 3 modes clean, e2e:filled 29, full e2e 224, new specs x5 170/170, cargo 134 + 3, engine
1,446 + 279 + 28, 100k bench, readbench gate), both `smoke:shell` walks pass, and a hand walk with real mouse input
in the release build proved G1, G4, Filter by type, column resize/move/pin and drop to open. Left for the fix round:
Advanced Home names a site by host only (two ports, one name); `GoToUrl` tests got a 20 s limit, not a cause. Linux:
`ui/r5-shell` passed the CI's Linux job in Docker (`tools/linux-ci.sh`). **Review launched**: two fresh-eyes
reviewers at once from [`brief-round5-review.md`](handoff/2026-11-14/brief-round5-review.md) (R1 the app, R2 the
engine, API and shell with security), then their fixes.

**2026-11-14: reviewed.** R1 (the app) 12 findings, one high (an open Delete confirmation re-targets to another
crawl and deletes its file); R2 (engine, API, shell) 11, none high, security sound (no key in history; updater
signature and downgrade checks hold). Both lists in [`reviews/`](handoff/2026-11-14/reviews/). The integrated branch
also passed the Linux CI job in Docker. **Fixes launched** on `008b4b2` from
[`brief-round5-fixes.md`](handoff/2026-11-14/brief-round5-fixes.md): F1 the app (`ui/r5-fix-app`, incl. Advanced
Home's host+port names and the `GoToUrl` cause), F2 engine and shell (`ui/r5-fix-engine`). Then: merge both into
`ui/r5-integrate`, the whole-suite verify, both smoke walks, Linux in Docker, the gallery, merge into `ui/step-3`.

**2026-11-14: both fixers done** (every finding fixed with a test seen failing first; reports condensed in
[`round5-fixes-reports.md`](handoff/2026-11-14/round5-fixes-reports.md); branches pushed). **Last merge and whole
verify launched** on `ui/r5-integrate` from [`brief-round5-final.md`](handoff/2026-11-14/brief-round5-final.md)
(scratch `a539aa23-…/r5-final/`): merge F1 then F2 (an expected conflict in the export path's engine files), F2's
one-line ask in `offers.ts`, the cause of `CrawlSettingsScreen`'s load timeout, then both verify scripts, cargo,
`e2e:filled`, round 5's specs x5, the release build and both smoke walks. After it: Linux in Docker
(`tools/linux-ci.sh`), the gallery re-shoot (the app changed a lot: Advanced Home, Overview's totals, the grid),
merge `ui/r5-integrate` into `ui/step-3`, push, and tell the owner step 3 is finished.

1. **Verify `ui/step-3` at `8fd6c66` (or later) as one whole.** Run both scripts from a fresh log folder, one at a
   time (the app's takes ~2.5 h; run it in the background):
   `bash docs/handoff/2026-11-13/tools/verify-engine.sh <repo> <logs>` then
   `bash docs/handoff/2026-11-13/tools/verify-app.sh <repo>/app <logs> 51874 5956` (copy the git-ignored fixtures
   into `app/design/data/` first: `cp -r` from another worktree's `app/design/data/fixtures`, or regenerate them).
   What's known green: on round 4's integration with its fixes (`96576cd`, the same app and shell code as
   `8fd6c66`): typecheck, lint, format, script tests 43, unit 3,437 (75 skipped), `cargo fmt`/`clippy`/`test`
   (120 + 3), build-storybook. On `37a58c3`, before the fixes: stories 482 x 2 themes and forced colours clean.
   Also green on `96576cd`: `e2e:filled` 29, round 4's specs x5 105, the full e2e 188 (2 skipped). The three
   story runs found ONE violation, the same in each: the fixer's new `SavedSettingRefused` story's "Options…" link
   in running text was told apart by colour alone in the dark theme (axe `link-in-text-block`). Fixed on
   `ui/step-3` after the merge (`si-link--inline`, as every other link in running text); the stories then ran clean
   in the dark theme (488, no violations). If a test file fails to load with "Vitest failed to find the runner",
   the worktree's Vite cache is stale: move `app/node_modules/.vite`, `.vite-temp` and `.vitest-cache` aside and
   run it again. The whole-suite verify
   still has to be seen green on the merged head. The engine: last green on `3ebd565` (the 100k bench and
   `readbench --gate`); round 4 didn't touch the engine, so its half is a formality.
2. **The release build and the shell's smoke test** (needs the desktop session): in `app/`,
   `npm run tauri build -- --no-bundle` (after `npm run build:sidecar`), then `npm run smoke:shell`: the export step
   (Open folder, Open file) and the Report's print step. Never yet run on round 4's code.
3. **The closing audit** (`brief-closing-audit.md`, read-only, both modes) and **the gallery re-shoot**
   (`brief-gallery.md`, builds into `.claude/worktrees/_gallery/site/` so the private page
   https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR keeps its address; the orchestrator views every image before
   publishing). They can run at once, once 1 is green. Update both briefs' base commit to `ui/step-3`'s head.
4. Fix what the audit finds that blocks "finished"; then tell the owner step 3 is finished. Step 3b, the check
   audit on real sites, follows.

**Round 4, as merged** (`ui/r4-integrate` into `ui/step-3`): three parts, each checked by its builder, integrated
on the hand-over UI and E7. A: Simple's Home, the checking screen, `ui/ScoreRing`, the shell's notification in
Simple's words. B: the Summary, What to fix, the score's breakdown. C: the Report in both modes (printing), the
score on Advanced's Overview. The integration settled the `overview` row (A's dispatcher showing B's Summary),
replaced every stand-in, kept ONE `reveal_export_file` in the shell, and put favicons and `pagesAnalyzed` on
Home. The review's fixes (`ui/r4-fixes`): an unanalysed stopped or failed check says so on the Summary (Continue
checking, Try again with the cause); a cancelled first analysis isn't announced as ready (toast and OS notice);
Simple's words in its toasts and screen-reader announcements; Ctrl+E on the Summary; a passed check says what it
looks for; things worth a look listed when there's nothing to fix; the breakdown never loads for ever and closes
back through history; Home words a refused address in Simple's words; hidden checks counted honestly; one date and
one page count per sentence; the time-left estimate and the blocked callout's count fixed; dead code removed. The
moot one: passing `crawling` to a problem's export, whose page is never drawn while crawling.

Small things owed: the design fixtures' regeneration cross-check reads a library field (`urlCount`) that no longer
exists (5 false mismatches); the `link-pass-split` and read gates have never run on CI; `readbench`'s links "not
followed" half-way read missed its gate once under load (554 ms against 200; 43 ms on the rerun); the score
breakdown dialog's new "not analysed" and "couldn't be read" fallbacks have no story (no route can open them now).

The earlier history of 2026-11-13 (the pause in the morning) follows; it is done and kept for the record.

### The morning of 2026-11-13 (history)

Paused that morning by the owner to save usage, about nine-tenths done, round 4 unmerged at `37a58c3`; resumed the
same day ("continue."). The resume list then was: round 4's integration checks, a fresh-eyes review, the verify of
`e350053` (its engine half then finished green on its own; its app half stopped after build-storybook, green to
there), the closing audit and the gallery. The first two are done (above); the rest moved into today's list.

**Merged on 2026-11-13**, each reviewed (but E7), fixed with a test seen failing per fix, merged, and verified again
by the orchestrator:

- **Round 4, Simple's screens** (`ui/r4-integrate` with `ui/r4-fixes`, `8fd6c66`), with its review's 19 fixes:
  see "Round 4, as merged" above. Its whole-suite verify on the merge is the first step of the resume list.
- **Round 3's UI** (`ui/round3`), with its review's 20 fixes: the mode (`settings.mode`, `useMode()`), the switch
  (Ctrl+Shift+A), the mode table `app/src/app/simpleScreens.ts` that round 4 fills one row a screen (a `filled`
  vitest project and `npm run e2e:filled` run everything with every row stubbed), Simple's sidebar, the top bar's
  order of giving way (13 steps; the switch never gives way), Settings (one file a section), New crawl in both
  modes, the crawl's settings and Continue, the alerts' actions.
- **The UI's take-up of E3c** (`ui/e3c-takeup`), with its review's 8 fixes: probes left out of Overview's figures,
  one URL count everywhere, time spent crawling across runs, outcomes on named URLs, `fixFirst`, Home at 200% zoom,
  quiet scroll bars in WebView2, the tray's memory budget stated as the private working set.
- **The URL grid** (`ui/urls-grid`), with its review's 16 fixes: presets, the column chooser, the filter bar and
  facets, three-key header sort, Ctrl+F, saved views (byte-budgeted; the shell writes compact preferences and never
  loses a failed write silently), whole-view copies, a row menu on every grid that holds its rows, the cursor
  following its row in every grid.
- **The hand-over UI** (`ui/handover`), with its review's 11 fixes: export on every grid (Ctrl+E, the native save
  dialog, a progress toast, exports that outlive their screen), the report and the workbook from Overview and
  Issues, the shell's `reveal_export_file` and `open_export_file` (they take an export id only; judged safe by the
  review; Open folder and Open file proven in the release build), the palette's search, favicons, Settings >
  Analysis, Links' "Show in URLs".
- **E7** (`ui/e7`, not reviewed: small, on reviewed patterns): export of the sitemaps list, robots.txt origins and a
  URL view narrowed by links; the library's `pagesAnalyzed` (the crawl counts Cloudflare's `/cdn-cgi/` page, the
  analysis doesn't audit it: both counts are right); the inspector's skeleton test on fake timers.
- Verified by the orchestrator on the merges: `e8176c6` (round 3 + E3c take-up) 2,938 unit, 339 stories x3, e2e
  143; `0a79de0` (URL grid) 3,092 unit, 369 stories x3, e2e 152 + one load-timed test hardened; `db6b69e` (hand-over)
  3,205 unit, 409 stories x3, e2e 165, the one failure the flaky test E7 then fixed.

**Merged on 2026-11-12**, each reviewed, fixed with a test seen failing per fix, and verified again by the
orchestrator on the merge (engine build and tests, the 100k bench, `readbench --gate`; the app's typecheck, lint,
format, unit, script tests, Storybook in both themes, forced colours and reduced motion, the sidecar build, e2e):

- **Sitemaps and robots.txt** (`ui/sitemaps-robots`, f61714d): `ui/step-3` merged in, both screens on the
  `GridScreen` frame, the counts checked against the real sidecar. Verified: 1,300 unit, 286 stories x3, e2e 111.
- **Export, the workbook and the report (E5)** (`ui/e5`), with its review's 16 fixes. A CSV field is cut at 32,758
  characters, Excel's real limit for one cell (measured), so a long URL can't split a row and start a formula. The
  sidecar keeps a journal of export part files in its own data folder and deletes them at its next start (a hard
  kill left them in the user's folder); it never sweeps a folder by pattern.
- **The catalog's fixes** (`ui/catalog-fixes`), with its review's 8 fixes. `site-hidden-from-search` needs a
  noindex home page as well as a noindex majority, and is skipped (new reason `start-not-root`) on a crawl that
  didn't start at the site's root. The `robots-txt-*` checks and `blocked-by-robots` are skipped
  (`robots-txt-ignored`) where a crawl that ignored robots.txt never read it. A byte-order mark declares a page's
  encoding; an empty `charset=` doesn't. `canonical-to-redirect` names where its redirects end, or says the end
  wasn't checked. Eight technical texts corrected (four titles). **17 skip reasons, 87 checks,
  `AnalysisFreshness.VERSION` 8.** The garden crawl is unchanged: 85. Verified with E5: engine 1,301 + 228 + 25,
  bench, gate; app 1,311 unit, 286 stories x3, e2e 111.
- **E6** (`ui/e6`), with its review's 6 fixes: `POST /search` (a keystroke 1.3-4.5 ms p95; the first search of a
  crawl 55-89 ms p95, so the palette warms it up on open); link passes split over up to eight read connections
  (common text filters and search 110-210 ms where they took 0.8-1.6 s), gated against their one-connection twins;
  the favicon (raster only, its whole structure checked, never failing a crawl); links to URLs (`linkEnds`,
  `/links/ends`); robots.txt origins searched; the inspector's exact links; `urls` on groups;
  `GET`/`PUT /api/analysis-settings`; `listedSitemaps`; `respected`; statuses in sitemaps and robots.txt named as
  in issue details ("404 Not Found") for new crawls. Its verify on the merge: see the status log.

**Built and under fixes on 2026-11-12 (merged on 2026-11-13, above):** the URL grid to the IA (`ui/urls-grid`), round 3's mode,
switch, top bar, Settings and New crawl (`ui/round3`), the UI's take-up of E3c (`ui/e3c-takeup`). **Briefed, not
started:** round 4, the Simple screens, three agents on round 3's frame; the hand-over UI (export on every grid,
the report and the workbook from Overview and Issues, the palette's search, favicons, Settings > Analysis). The
briefs, reviews' findings and the orchestrator's checkpoint are in the session scratchpad
`C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\`
(`checkpoint.md` first); they are copied into `docs/handoff/` at the end of step 3 or at a pause.

**Decided on 2026-11-12 and 2026-11-13 under the owner's delegation** (each the owner's to overturn): saved views keep a "links" narrowing; a crawl that didn't start
at the site's root skips `site-hidden-from-search`; export part files are cleaned through the sidecar's journal;
search ranks a match at the start of the host or of a path segment first, never in the scheme or query; the top
bar's switch never gives way, and at the smallest sizes Pause, Stop and search fold into one "More" menu and then
the status pill shrinks to its dot; a settings file from before the mode existed opens in Advanced (only a first
start is Simple); Copy diagnostics writes `%USERPROFILE%` for the user's profile path; grids keep the design's 32 px
rows by default (Compact, 28 px, gives about 18 rows at 1280x720 against Screaming Frog's 20); a named URL whose
fate isn't known says "Not fetched". **Settled 2026-11-13 (the owner: "make the best choice for an SEO tool"):** links
on pages the analysis doesn't audit (Cloudflare's `/cdn-cgi/` pages) stay out of inlink counts and orphan checks, as
built. Those pages are made by Cloudflare, not the site: counting them as a source would let a real orphan look
linked. Links *to* them (every obfuscated email links to `/cdn-cgi/l/email-protection`) are still stored and shown.

### The pause of 2026-11-11 (history)

**Paused on 2026-11-11 by the owner, to save usage.** Step 3 was about two-thirds done. Nothing was running.

**Everything is on GitHub**: this branch, and every working branch under its own name (`ui/links`,
`ui/sitemaps-robots`, `ui/e5`, `ui/e6`, and the merged `ui/e3c`, `ui/score` and `ui/simple-design`). Work can go on
from any computer.

A new session needs this section and one folder in the repo: [`docs/handoff/2026-11-11/`](handoff/2026-11-11/). It
holds `checkpoint.md` (the session's log, with a PAUSE section at its end), every brief (`brief-*.md`),
`ui-slice-rules.md` (the rules every UI slice follows), `tools/verify-app.sh` and `tools/verify-engine.sh`, the
reviews' findings (`reviews/`), each agent's own checkpoint (`checkpoint-<name>.md`, and `e5/` and `e6/` with their
`HANDOFF` sections), and the plain words' notes. The files name paths on the first machine (a scratchpad under
`%LOCALAPPDATA%\Temp\claude`, and `C:\Projects\ShoutingIguana\.claude\worktrees\...`): on another computer, read
those as "this folder" and "a worktree of that branch". Subagents can't be resumed across sessions: relaunch each
from its brief and its checkpoint.

### On another computer

- Clone, and check out `ui/step-3`. For a branch an agent works on, `git worktree add .claude/worktrees/<name>
  <branch>` keeps each agent in its own folder, as the briefs assume.
- It needs the .NET 10 SDK, Node with `npm ci` in `app/`, and Rust for the Tauri shell.
- Two things aren't in the repo. **The test crawls** live in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls` on the
  first machine: a real crawl of the garden site and a 100k synthetic one, which `npm run dev` and several briefs
  use. The synthetic one can be made again with `si bench --pages 100000 --db <file>`; the garden one has to be
  copied across, or the site crawled again. **The design fixtures** (`app/design/data/fixtures`, git-ignored) are
  extracted from those crawls: `app/design/data/README.md` says how. The e2e suite needs neither: it crawls the
  engine's own test site.
- The owner's memory file is per machine. `CLAUDE.md` points a new session here.

### Merged on this branch on 2026-11-11

- **[The comparison](v2-comparison.md)** with Screaming Frog and Sitebulb (item 3).
- **The Simple design** (item 4): [`v2-ui-simple.md`](v2-ui-simple.md), small edits to the IA, the design doc and the
  glossary, and 18 mockups on the garden crawl in `app/design/simple/`. It ends in three lists the build rounds are
  briefed from: what round 3 builds, what round 4 builds, what the engine needs. The owner's gallery of the
  mockups is published, private: https://claude.ai/artifact/R53PTE7wZb7rPZHX6Z3GfN (built by
  `gallery-simple\build.cjs` in the handoff folder, published from
  `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\simple\`: republish from that path to keep the URL).
- **Engine E3c, with its review's 7 fixes** (`ui/e3c`), each with a test seen failing without it:
  - A running crawl's log is folded past 64 MB while a links grid reads it: 75 to 87 MB at its peak on a
    40,000-page crawl with one reader, against 106 to 160 MB before, and the crawl is no slower. A read may wait
    for a fold; none fails. `si bench --read-links --max-log-mb` gates it, and CI runs it.
  - The time spent crawling survives a first run that stored nothing, a clock set back and a machine that slept.
  - `GET /sitemaps` gives each sitemap's `urls.robotsDisallowed` and `urls.redirectingOrBroken`.
  - A kept `/stats` takes no read slot. `stats-pass` is gated at 450 ms, and its miss of the 300 ms budget is a
    known limitation in the API doc.
- **The engine's score work** (`ui/score`; contract: "The score" and "Engine changes the score made" in the API
  doc):
  - The score, version 1, worked out by the analysis from its own counts and stored with it:
    `analysis.score`, `fixFirst`, `crawledUrlCount`; the library item's `score`; `si score`. The garden crawl
    scores 85, as the design works it out.
  - The catalog has **87 checks**: the new one is `site-hidden-from-search`. Checks carry `siteWide` and
    `reportedOnce`. `AnalysisFreshness.VERSION` is 7. A status in a detail reads "404 Not Found".
  - **The plain words for all 87 checks**, 17 categories and 15 skip reasons, written to the design's template,
    reviewed against each rule by a second agent and corrected. They are generated from one data file,
    `engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json`. Ten texts name a limit as a
    token the engine fills from the settings the analysis used. `GET /api/checks` serves them.
- **The Links screen** (`ui/links`, with its review's 14 fixes and four smaller ones, each with a test seen
  failing without it): every link in one grid, with presets, facets read only while a menu is open, a filter
  editor the engine judges, a column chooser, a whole-view copy and "All N in Links" from the inspector. Shared
  with URLs: the `GridScreen` frame and `minRows` (a grid keeps five rows at 200% zoom), no reads while the window
  is hidden. On Issues, a check's count is worded by its subject: a page check's share is "of the N pages
  analysed" and a URL check's "of the N URLs crawled", both from the analysis's own counts. `npm run dev` keeps a
  crawl's analysed copy while its original hasn't changed.
- **Verified on the score merge by the orchestrator:** the engine build and 1,353 tests (1,156 engine, 176 sidecar, 21 CLI), the 100k bench (every planted issue, flat memory), and the app's typecheck, lint, format, 877 unit tests, script tests, 167 stories in both themes, forced colours and reduced motion, and e2e 62 of 62. On the Links merge, the full app checks were being run again when this was written: their result is the last line of the PAUSE section of `handoff/2026-11-11/checkpoint.md`, or missing if the session ended first.
  - The first run of these checks found two faults that every agent's own run had missed: the merged tree didn't
    compile its sidecar tests (the score's test used a helper the E3c fixes had removed), and one file came
    unformatted from the E3c fixes. Both are fixed. `verify-engine.sh` now stops when the build fails, where it had
    gone on to test the previous build.
  - `readbench --gate` failed on this machine while four other agents were running, each time in a different
    class and by a different margin (1,139 ms and 2,078 ms against gates of 200 and 550 in two links classes, then
    245.7 ms against 245 in a facets class), and each class passed when run again alone. Owed: one whole run of
    the gate with nothing else running.

### Built on their own branches, not merged

Each is pushed under its own name. None is merged here: Sitemaps needs a merge with real conflicts, and the two
engine branches are part-built and haven't been reviewed.

| Work | Branch and worktree | State at the pause |
| --- | --- | --- |
| Sitemaps and robots.txt | `ui/sitemaps-robots`, `sitemaps-robots` | At `fe51eb0`. All 17 of its review's findings are fixed, each with a test seen failing without it (unit 1,105, stories 245, e2e 86 on its own branch). `ui/step-3` is not merged into it yet |
| Engine: export, the workbook and the report (E5) | `ui/e5`, `e5` | At `03877b6`, with `ui/step-3` merged in. Built: CSV and Excel export of every grid, the workbook, and the report on the real score, order and plain words, with `si export` and `si report`; the branch builds and its tests pass (1,259 engine, 219 sidecar, 23 CLI; 880 app unit tests). Not done: the 100k bench and the read gate, the report measured again on the 100k crawl, the printed pages looked at past page 1, the API doc for the last changes, and any review. Its `HANDOFF` is at the top of `e5/checkpoint.md` |
| Engine: fast search, the favicon, the limits' API, and what round 2 found the engine lacks (E6) | `ui/e6`, `e6` | At `87e3ff5`. Nine of its twelve items are built and tested (1,042 engine, 177 sidecar, 17 CLI): `POST /search` for the palette, link passes split over four connections, robots.txt origins searched and filtered, the inspector's exact links, `urls` beside `pages` on a check's groups, `linkTotals.hyperlinkInlinks`, `GET` and `PUT /api/analysis-settings`, a sitemap index's `listedSitemaps`, `respected` on `/robots/file`. Not started: the favicon. Part-done: "links to URLs" and a view's distinct ends (written, never applied or compiled: `e6/LinkEndsTests.cs.pending` and `e6/specs/link-ends-*.py`), and the docs and TypeScript types. Not run: the 100k bench, the read gate, anything in `app/`. No review. Its `HANDOFF`, item by item, is at the top of `e6/checkpoint.md` |

### To resume, in this order

1. **Sitemaps and robots.txt.** An agent merges `ui/step-3` into `ui/sitemaps-robots` (expect conflicts in
   `GridCore.tsx`, `api/queries.ts`, `keyboard/registry.ts`, `e2e/overview.spec.ts`, `UrlsScreen.tsx` and both UI
   docs; `checkpoint-sitemaps-fixes.md` has its fixer's notes for this merge). 87 checks and "404 Not Found" may
   break tests that count 86 or match "HTTP 404". Its two screens take the Links slice's `GridScreen` frame and
   `minRows`. The list's Crawled and Blocked columns have only been seen against a mock: check them against the
   real sidecar, which now sends the two counts. Then merge, `verify-app.sh`, push.
2. **Export (E5).** Finish from its `HANDOFF`, then a fresh-eyes review of the whole branch (it has had none),
   fixes, the merge, both verify scripts, push.
3. **E6.** Finish its list from its `HANDOFF` (the favicon, "links to URLs", the types and docs, the measurements
   on a quiet machine), then review, fixes, merge `ui/step-3` in, the merge, both verify scripts, push. Two traps
   its agent left: the pending `bySource` edit to `Scan` must be redone by hand, and a sidecar test demands that
   every analysis setting has a described limit in `Sidecar/Options/AnalysisLimits.cs`.
4. **The catalog's fixes**, an engine job that isn't briefed yet:
   - `site-hidden-from-search` must also require the home page (or the start URL's page) to be noindex. As built
     it counts noindex pages only, and a shop with many noindexed filter pages could still be told its site is
     hidden, which costs 60 points.
   - Twelve faults in the technical texts, confirmed by the plain words' review (the list is in
     `reviews\plain-words-review.json` and `plain-words\notes.md` in the handoff folder).
   - Three behaviours: `charset-missing` ignores a byte-order mark; `blocked-by-robots` reads as passed, where it
     should be skipped, when the crawl was told to ignore robots.txt; `canonical-to-redirect` gives no final URL.
5. **Three UI slices, briefed and not started**, to run at once after item 1 (they touch the shell and the grid
   that Sitemaps changes): `brief-urls-grid.md` (the URL grid to the IA: filter bar, columns and presets, header sort,
   Ctrl+F, a row menu on every grid, saved views), `brief-round3-ui.md` (the mode and its switch, the top bar's
   order of giving way, Settings, New crawl in both modes, the crawl's settings, and the frame round 4 plugs
   Simple's screens into), `brief-e3c-takeup.md` (the UI takes up E3c's fields and the engine's `fixFirst`, and
   three of the real shell's findings).
6. **Not briefed yet:**
   - After E5 and E6 merge: export on every grid (Ctrl+E, the save dialog, the progress toast), "The report…" and
     "Spreadsheet of everything…", the palette's search, favicons, Settings > Analysis, the inspector's exact links
     to a sitemap and a robots.txt line, "Show these pages in URLs" from Links. Two notes from E5: the `export`
     event isn't in the app's `SidecarEvent` union yet, and a `srcdoc` frame for the report inherits the app's
     CSP, so that policy must allow the report's inline style sheet.
   - Round 4, on the frame `brief-round3-ui.md` builds, three agents at once: the Simple shell, Home and "checking
     a site"; the Summary, the score's breakdown, What to fix and one problem; the Report screen in both modes,
     the finished toast, and the score on Advanced's Overview. The design's "What round 4 builds" is the list.
7. Re-shoot the screenshot gallery, then the closing audit in both modes (items 7 and 8 of the list below).

The machine sets the pace: with four heavy agents and a verify run it was at 50 of 57 GB of committed memory and
90% of its processors, and timings went wrong. Run at most four heavy agents, and one verify run at a time.

### How each piece goes

1. A build agent.
2. A fresh-eyes review. A subagent can't write a file named `findings.md`: save its report yourself as
   `<name>-review\findings.md` for the fixer.
3. A fixer, which gives every fix a test seen failing without it, and then merges `ui/step-3` into its own branch,
   so the agent that knows the code resolves the conflicts.
4. The merge into `ui/step-3`.
5. The full checks, run again on the merge and not taken from the agent: `tools\verify-app.sh` and
   `tools\verify-engine.sh`. Read `summary.txt` line by line: a step that failed can hide behind later ones.
6. The push.

### Decided on 2026-11-11 under the owner's delegation

Each is the owner's to overturn.

- **The score:** notices cost nothing; a fault of the whole site costs 60 points; it can't pass 89 while a serious
  problem stands. It carries a version.
- **The switch** between Simple and Advanced is at the right of the top bar.
- **Simple's severity words:** Serious, Worth fixing and Worth a look, one to one with Error, Warning and Notice.
- **"Fix first":** a fault of the whole site leads its severity, in Advanced as well. The app still uses its own
  order until `brief-e3c-takeup.md` is built.
- **A check's share:** a page check is over the pages the analysis ran on; a URL check is over the URLs crawled
  when the analysis ran.
- **"Crawled" on Sitemaps** means the crawl requested the URL. URLs robots.txt refused have their own column.
- **Plain words** are new texts beside the technical ones. A limit in one is filled in from the analysis's
  settings. A check reported once for the site says so, where it would have said "1 of 265 pages".
- **The report** is one HTML file the engine makes, with an Excel workbook beside it.
- **An export path on a network share is refused**, as a crawl file's is.

### Known and open

- On a crawl of several hundred thousand pages a link pass takes over 2 s, the fold of the log gives up, and the
  log grows as it did before E3c's fix. Nothing at that size was measured.
- The inspector's link grids show one row at 200% zoom. Sitemaps' count columns start off-screen there.
- Nothing has run in the real Tauri window since the shell pass: the grids' least height (`contain: size`) and the
  hidden-window refresh are untested there.
- A real screen reader, anything at 1M URLs, and CI on this branch: not done.

Done on this branch, on top of `v2`:

- The grid readers, the inspector, live crawl data, and the app shell and front-end review fixes are merged.
- The client uses `POST /urls/inspect`, `/urls/inlinks`, `/urls/outlinks` and `/urls/source`.
- Near-duplicates use four SimHash lanes. `AnalysisFreshness.VERSION` is 6.
- The catalog has 86 checks. FAQPage no longer requires `mainEntity`. `es-419` is an invalid hreflang. JSON-LD is
  strict. A remote JobPosting needs `jobLocation` or `jobLocationType`. New check `html-past-crawl-limit`.
  `blocked-by-robots`, `description-missing` and `h1-missing` are Notices. `duplicate-site-version` is a Warning.
  Each check has `why` and `learnMore`, and `GET /api/checks` returns them.
- Inter and JetBrains Mono are bundled (roman 400, 500 and 600) in `app/src/styles/fonts/`.
- The forced-colours Tab test is green (merged `ui/forced-tab`, 2026-11-04). The app had no bug: the cached e2e crawl
  had an analysis by an older engine, so the URLs screen showed its "Analyse again" offer, a Tab stop between the
  search and the grid. The e2e setup (`startOnCurrentCrawl` in `app/scripts/lib/engine.mjs`) now remakes a stale
  cached crawl, or re-analyses the copy of a stale `SI_E2E_CRAWL`, and the test checks its precondition. 309 unit
  tests, 14 script tests, and the e2e suite (10 tests) passed twice in a row.
- The design system (merged `ui/design-system`, 2026-11-04): Lucide icons, Radix primitives, and the components in
  `app/src/ui/` in the clarity look, listed in the architecture doc's "Components". Each is shown in every state under
  "Design system/" in Storybook, and `npm run test:stories` runs axe over every story (`-- --forced` for forced
  colours). The shell, Home, URLs, Settings, feedback and the palette use them. On the merge: 354 unit tests, 59
  stories with no violations in either theme, e2e 13 of 13.
- The design system's fresh-eyes review found 25 defects, all fixed (merged `ui/ds-fixes`), each with a test seen to
  fail with its fix reverted. The worst: under forced colours, focus was invisible on anything selected. Also one Esc
  for a tooltip and the app, fields' clear reaching `onChange`, chip focus, ConfirmDialog errors, 200% zoom, reduced
  motion, cached registry lookups, and app messages in their own tone (not the severity marks). On the merge: 389
  unit tests, 14 script tests, stories clean in both themes and forced colours, e2e 27 of 27.
- The Issues screen (merged `ui/issues`, 2026-11-04):
  - **Checks list:** the checks by category, skipped checks always listed with their reason, passed behind a switch,
    hidden checks remembered with Undo.
  - **Selected check:** what's wrong, why it matters and how to fix it, with Learn more.
  - **Affected pages:** a paged, virtualised treegrid in `GridCore` (`tree`, `spanRow`, `sortable`), with the
    likely-template callout and "Show all N pages in URLs". A `none` check lists its URLs flat.
  - **Copy URLs**, the palette and keys (J/K, Ctrl+F, Ctrl+Shift+C/H), and every state in Storybook.
  - **Sidebar:** counts by severity, and it starts as icons below 1024 px.
  - **Inspector history:** one model for both screens, `routing/inspector.ts`.
  - **Review:** a fresh-eyes review found 23 defects, and 22 are fixed, each with a test seen to fail with its fix
    reverted. The one left is "pages affected" shown for checks that can flag non-pages: it needs an engine field,
    in E3b.
  - **Verification:** on the merge, 488 unit tests, 84 stories clean in both themes, forced colours and reduced motion,
    14 script tests, and e2e 36 of 36. Timings are inside the budgets on the garden and 100k crawls: switching checks
    takes 77–96 ms, opening a group 37–47 ms.
  - **Export** waits for E5.
- The Overview screen (merged `ui/overview`, 2026-11-10):
  - **Live:** counts, rate, time left when a URL limit is set, the found-and-crawled chart, the magnified status
    bar, alerts and waits. One clock: the time this run has spent crawling.
  - **Done:** the answer sentence, severity, Fix first, categories, skipped checks, status codes, indexability,
    depth and response times. Every number is a link. 220 of them were checked against where they land, on three
    crawls.
  - **Controls:** Pause, Resume and Stop stay in the top bar only, and the banners carry each state's next action.
    The owner was told and can overturn it.
  - **Review:** 18 defects, all fixed, each with a test seen failing without its fix.
- The URL inspector (merged `ui/inspector`, 2026-11-10):
  - **Sections:** Summary (the four indexability tests, values drawn on the engine's limits), Issues, Inlinks and
    Outlinks (each URL walks the inspector to it), Redirects, Headers, Structured data, Hreflang and Source
    (CodeMirror 6 with the app's own search).
  - **Palette:** "Go to URL…".
  - **Hidden checks:** a check the user has hidden stays listed for the URL, marked Hidden, out of the counts.
  - **Review:** 30 defects, all fixed the same way.
- Engine E3b (merged `ui/e3b`, 2026-11-10):
  - `GET /api/link-columns`, `POST /links` and `/links/facets`, `GET /sitemaps`, `POST /sitemaps/urls`,
    `POST /robots`, `/robots/file` and `/robots/blocked`.
  - `subject` (`page` or `url`) on every check, and `group` on each of the inspector's issues.
  - The analysis builds a link summary (4.5 MB on the 100k crawl). `AnalysisFreshness.VERSION` is still 6.
  - robots.txt has a new state, `unanswered`: no answer, and the crawl went ahead.
  - **Review:** 8 defects, 7 fixed in the engine and 1 in the merge. The worst: `/links/facets` answered 500 while a
    crawl ran. A running crawl's links reads now answer for one moment of the crawl, and a test reads every kind of
    query about 11,500 times while a crawl grows.
- After the merges (merged `ui/palette-handoff`, 2026-11-10):
  - A command chosen in the palette runs once the palette has given focus back, so it keeps the focus it takes.
  - The session's first Ctrl+K takes about 20 ms, not 320.
  - A grid's result count is no live region. It is said once, after the user changes the view.
- The real shell (merged `ui/shell-pass`, 2026-11-10):
  - **How:** the release build (`tauri build --no-bundle`, WebView2 157, 150% scale) with its own data folder,
    driven over WebView2's debugging port, with a Win32 helper for the window, the tray, native dialogs and keys.
    `npm run smoke:shell` repeats the walk: 14 steps in about a minute. It needs a Windows desktop session, so it
    isn't in CI. The architecture doc says how to run it.
  - **Works:** start, and the engine killed or slow to start; a crawl from Home with F7 and F8; URLs, the inspector
    and Issues; the palette, "Go to URL…" and the shortcut sheet; window size and place; the tray, with a crawl
    finishing while the window is hidden; "Stop crawling and quit?"; a second start; the OS theme changing;
    copying; "Open in the browser".
  - **Keys:** WebView2's own (F5, Ctrl+R, F3, Ctrl+G, Ctrl+J, Ctrl+P, F12, Shift+Esc) do nothing and still reach
    the page.
  - **Timings** on the 100k crawl are beside Chromium's in the architecture doc, all inside their budgets: the
    answer painted in 57 to 61 ms, a status class to its first rows in 51 to 66 ms, the inspector's summary in 18
    to 20 ms, the first Ctrl+K painted in 46 to 61 ms, and no frame over 33 ms through a live crawl.
  - **Fixed:** back from the tray after a crawl ended there, Overview failed with too many re-renders
    (`useAnalysisAnswer`).
  - **Found, still open:**
    - Home at 200% zoom loses the library's Site column and scrolls sideways.
    - The top bar's search field shrinks to one letter beside Pause and Stop at 1280 px.
    - Scroll bars are Windows' classic ones with arrows, which headless Chromium never shows.
    - Overview's breadcrumb crushes the date at 200% zoom.
    - Hidden in the tray, the shell and WebView2 hold 66 MB of working set where the doc says 23 MB. The two
      figures may not measure the same thing.
    - Not built: Ctrl+F on URLs (the IA's "Filter this grid"), a header sort and a row menu on the URL grid.
  - **Not checked:** real hardware keys and the physical screen (it ran in a disconnected remote session),
    notifications as shown, the tray icon's look, Pause and Resume from the tray menu, scales other than 150%,
    the `.sicrawl` file association and drag and drop.
- **Verification on the merges**, run again by the orchestrator:
  - App, on the shell merge `c709158`: 876 unit tests, 25 script tests, 167 stories clean in both themes, forced
    colours and reduced motion, and e2e 62 of 62.
  - Engine: 1,156 tests, the 100k bench (every planted issue, flat at 193 MB) and `readbench --gate`.
  - The gate failed once, under load, on four URL-grid sorts: 201 to 264 ms against 200. On a quieter machine the
    same reads took 26 to 35 ms with E3b and 25 to 33 ms without it, and the whole gate then passed.
- **Not verified by anyone yet:** a real screen reader, anything at 1M URLs, and the shell under a person's hands
  (the list above). CI has not run this branch.
- The clickable prototype is the app itself: `npm run dev` runs it against copies of the real crawls. A static mock
  build isn't published, as it can't be reviewed file by file.

Left, in this order. Where each stands is above.

1. Round 2: E3c and Links are merged. Sitemaps and robots.txt is fixed and not merged.
2. The UI takes up E3c's fields, and the shell pass's open findings are fixed: briefed, not started.
3. Done: the comparison.
4. Done: the Simple design, with its mockups published for the owner.
5. Round 3: the engine's score is merged; its export and E6 are part-built on their branches; the UI (the mode
   and its switch, Settings, New crawl, the URL grid) is briefed and not started; export and search in the UI are
   not briefed.
6. Round 4, Simple: the plain words are done and in the engine. No screen is built.
7. The screenshot gallery of the built app (36 views from the shell pass, private:
   https://claude.ai/artifact/NWJJ4cCWtaDor2FfkzdHsR) is re-shot and republished when the remaining screens land.
   Its builder is `gallery\build.cjs` in the 2026-11-10 scratchpad, and it is published from
   `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\`.
8. Before step 3 is called finished: a read-only audit of what is built against the IA, the Simple design and the
   UI brief (screens, the Keyboard table, the definition of done), in both modes.

After step 3 comes step 3b, the check audit, and then step 4.

Known and open: a links filter on the target with a link's position takes 565 ms half way down a big crawl, and
nothing is measured at 1M URLs.

Never edit the files in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`.

The catalog branch was verified before its merge: 1,104 tests, and the 100k bench found every planted issue. The
merge itself was clean. CI on `v2` does not run this branch.

## Status

As of 2026-10-31:

- **Real site.** `www.gardenfurnituresets.co.uk` (behind Cloudflare) was crawled at 2 requests/s on 2 connections:
  4,521 URLs in 37 minutes, never blocked. An audit checked every issue it reported against the stored HTML of all 265
  pages. 17 of the 20 issues it reported were right. The rest are fixed or documented below:
  - duplicate H1s within a paginated series;
  - images sized by CSS;
  - one-off slow responses.
- **Reviews.** Three rounds of parallel reviews covered storage, rules, fetching, and the CLI and tests. They found
  about 60 defects, then about 35, then about 20. Every one is fixed, except those listed under
  [Known limitations](#known-limitations). Each fix has a test that was seen to fail with the fix reverted.
- **Legacy app.** The old app's link-queue fix shipped as 1.0.4 from `master`.

## Decisions (and why)

- **C# / .NET 10 for the engine.** Official .NET SDKs exist for every integration on the roadmap (Search
  Console, GA4, Google Ads, Microsoft Advertising). The old app's problems were design, not language.
- **Cross-platform.** The engine targets `net10.0` and CI tests on Windows, macOS and Linux. The UI shell is Tauri
  for the same reason.
- **The NuGet plugin marketplace is gone.** It never worked (plugins never loaded) and the owner agreed. Checks are
  built-in rules in `engine/src/ShoutingIguana.Engine/Analysis/Rules`.
- **HTTP-first fetching.** Each page is parsed once into `PageFacts`. A browser is used only where needed, in step 4.
- **Blocked sites are reported, not hidden.** The crawler uses:
  - realistic headers;
  - a polite rate with automatic backoff;
  - explicit `Blocked` detection (Cloudflare, Akamai, DataDome...);
  - custom headers, so a site's firewall can allowlist it.

  There will be no proxy rotation or fingerprint spoofing.
- **`www` and the bare domain are one site.** Both are internal, and they share one rate limit and connection limit.
  Sites serve one and redirect the other, and separate limits would double the load on the server. The owner confirmed
  this on 2026-11-01.
  - Crawling them as one site stays. A version that answers instead of redirecting to the main one is
    `duplicate-site-version`, a Warning: Google treats the versions as duplicate URLs and picks one, preferring
    HTTPS. Probes aren't parsed, so a canonical on the other version isn't seen.
  - Nothing links to the other versions as a rule, so the crawler probes each one at the start of a crawl: a status
    check of the start URL's path there, never parsed or followed, so the site is never crawled twice.
  - A dead version isn't flagged, and neither are its unreachable URLs or robots.txt: a dead `www` twin is normal.
  - A probe never holds up a crawl: one try, none at the end, and none at all where its origin's robots.txt timed
    out. A version behind a firewall that drops packets then costs one robots.txt cycle, not three minutes.
- **A site that stops answering stops the crawl, which can be resumed.**
  - The crawler pauses a minute between tries, and after 10 minutes without an answer it stops: status Stopped,
    and `si crawl` exits 3.
  - `--resume` continues from there.
  - Whether something answers is judged per origin, with its robots.txt as the probe. So one part of the site
    going quiet (a dead `www` twin, a closed port 80, a section that never answers) doesn't stop a healthy crawl.
  - An origin that has answered before is waited out, never written off. Only one that has never answered can be
    written off, and only while the site's last answering origin still answers. A start URL that only redirects
    (`http://` to `https://`) therefore can't make the real site look dead.
  - Time spent paused doesn't count toward the 10 minutes.
  - A URL the crawler gave up on without asking is "Not checked", never broken.
- **A check that can't be accurate is skipped, not reported clean.**
  - The orphan and sitemap coverage checks need a complete crawl and readable sitemaps.
  - The external-link, image and sitemap checks need what the crawl options may switch off.
  - When a crawl doesn't support a check, it is left out of the results with nothing stored for it, and a note says
    why.
  - Not checked is not the same as 0, and the UI must show the difference.
- **A robots.txt that gets no answer at all blocks only the start URL's origin.**
  - RFC 9309 says to assume a full disallow when robots.txt can't be fetched, and the crawler does that for a 5xx or
    a 429 anywhere.
  - On any other origin with no answer at all (another site, a dead `www` twin, a closed port 80), the URL is fetched
    anyway. An origin that can't answer robots.txt almost never answers anything else, and fetching shows the real
    network error. Without it, a dead link would be reported as "blocked by robots.txt".
- **Crawl files hold no secrets**, since people share them. Header values and proxy credentials aren't stored, and
  resuming a crawl asks for them again.
- **One SQLite file per crawl (`.sicrawl`).**
  - Only `Storage/` writes to it: `CrawlWriter` during a crawl, `IssueWriter` for analysis.
  - Memory stays flat because the frontier, results and links live on disk.
- **Analysis runs after the crawl, from stored data.** Any crawl can be re-analysed with new rules or thresholds
  without recrawling. Issue ids are stored in crawl files, so they never change.
- **Analysis judges what search engines will see.**
  - An instant meta refresh is a redirect.
  - A canonical whose target leads back to the page, or nowhere, leaves the page indexable. A broken canonical is
    ignored, and a redirect is the stronger signal.
  - A sitemap may list URLs on its own host, the start URL's host and the home page's host. A `www` sitemap listing
    `www` pages is fine on a crawl that starts at the bare domain.
  - A link is named the way a screen reader names it: an image's alt text, a labelled icon, `aria-labelledby`,
    `aria-label` or `title`.
  - The pages of a paginated series (`rel=next/prev`) may share an H1, but each still needs its own title and
    description.
  - The crawl's own record stays as fetched. The analysis stores where it differs in `effective_indexability`,
    which the stats and the UI read.
- **The proof is exact.**
  - The synthetic site (`engine/src/ShoutingIguana.TestSite`) is clean by construction: problems appear only where
    they are planted.
  - The e2e test and the 100k benchmark require the exact URLs for every check, so extra flags fail as well as
    misses.
  - Every new check needs a planted case.
  - The one allowance is for response times, which depend on the machine. Up to 5% of pages (at least 25) may be
    flagged slow besides the planted one. The e2e test also checks the stored times directly, so the crawler's own
    timing can't drift unnoticed.
- **Thresholds follow common SEO tool defaults.** For example: titles 30–60 characters, descriptions 70–155,
  200 words, images over 200 KB. All are configurable in `AnalysisSettings`. Googlebot's 2 MB HTML limit is not:
  it is Google's, named in the check, and not a setting.
- **Check severities (November 2026).** Severity is part of the checks fingerprint, so analyses from before these
  changes are out of date. That is intended.
  - `blocked-by-robots` is a Notice, not a Warning. Blocking is usually deliberate (baskets, internal search,
    filters). Search Console lists it as excluded, not an error, the same as `noindex-page`.
  - `description-missing` is a Notice. Google writes most snippets from the page, and a missing description has no
    documented ranking effect.
  - `h1-missing` is a Notice. Google says it rarely relies on headings' HTML semantics; the gap is mainly
    accessibility.
  - `duplicate-site-version` is a Warning, not an Error. Nothing is broken: Google treats the versions as duplicate
    URLs and picks one, preferring HTTPS. Probes aren't parsed, so the check can't see a canonical on the other
    version and can't tell a harmless copy from a harmful one.
  - `meta-refresh` stays a Warning. One severity per id. Google treats an instant refresh as a permanent redirect,
    but a server redirect is what Google recommends, and every client follows it.
- **Simple first, Advanced on a switch (2026-11-10).** The owner wants an SEO tool "super easy for anyone to
  understand", with the detail one switch away for a technical SEO. That replaces the 2026-11-01 aim, an agency's
  tool, as the first face of the app. The screens built for that aim stay, as Advanced. The plan is
  [Simple first](#simple-first-advanced-on-a-switch-2026-11-10).

## Handoff to step 3 (the UI)

The engine is a library, and the CLI is a thin layer over it. The UI needs the same thin layer as a localhost API.
What the UI must be like is in [the UI brief](v2-ui-brief.md). This section covers what the engine gives it.

Work on a branch off `v2` (for example `ui/step-3`) and merge `v2` in often. Engine changes keep landing on `v2`
meanwhile, starting with the check for duplicate site versions (bare domain or `www`, http or https).

### What exists

Everything below is public in `ShoutingIguana.Engine`.

| Need | API |
| --- | --- |
| Start, pause, resume or stop a crawl | `new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, path, Resume), observer)` returns a `CrawlJob`: `Pause()`, `Resume()`, `StopAsync()`, `Progress`, and `Completion` (a `CrawlSummary`) |
| Live progress and warnings | `ICrawlObserver.OnProgress(CrawlProgress)` every 500 ms. `OnNotice(CrawlNotice)` carries a stable `Code` to key on, such as `blocked`, `site-down` or `robots-unreachable`; the message text may change |
| Crawl settings | `CrawlOptions` and its `Validate()`. `CrawlDatabase.ReadOptions(path)` returns a stored crawl's settings, secrets removed (`HasRedactedProxyCredentials`; header names are kept, values aren't) |
| Analyse | `new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, settings, cancellationToken)` returns an `AnalysisSummary`: counts per check, `IsPartial` and `Notes` |
| Issues | `IssueReader`: `ReadAnalysis`, `ReadCounts`, `ReadNotes`, `ReadIssues(path, id, limit, offset)` (paged) and `ReadAll` (streamed, for export). A check missing from `ReadCounts` wasn't run |
| What each check means | `IssueCatalog.All` and `Find(id)`: id, category, severity, title, description (what), why, fix and an optional learn-more link |
| Headline numbers | `CrawlStatsReader.Read(path)` |
| Anything else | `CrawlDatabase.OpenReader(path)` is a read-only connection. `BlobCodec` decodes `response_details` (headers and page facts) and `response_html`. The tables are defined in `Storage/CrawlSchema.cs` |

### Rules the UI must keep

- **One writer at a time.**
  - A crawl or an analysis holds `<crawl file>-lock`. On Linux and macOS the file stays after the lock is released.
  - An analysis refuses a crawl that is running or was interrupted.
  - Readers are safe at any time, including during a crawl.
- **Show indexability as the analysis judged it**:
  `COALESCE(effective_indexability.indexability_reason, responses.indexability_reason)`.
- **Show skipped checks and the analysis notes.** A skipped check is not a 0.
- **Ask again for secrets on resume.** Header values and proxy credentials are not in the file.
- **Never write to a crawl file outside `Storage/`.**

### What's missing

1. **The sidecar.** An ASP.NET Core minimal API on localhost, started by the Tauri shell, with a random port and a
   per-launch token. It needs:
   - crawl control;
   - analysis;
   - the readers;
   - progress and notices, pushed over server-sent events.
2. **Readers for the grids.** These must be paged, filtered and sorted, and live in `Storage/`:
   - URLs: status, indexability, depth, type, size and time;
   - links in and out of a URL: anchor, position and flags;
   - one URL in detail: headers, facts, redirect chain, inlinks, issues and HTML;
   - sitemaps and robots.txt.

   Only issues are paged today. Anything else means SQL against `OpenReader`.
3. **Export.** Today only the CLI's issues CSV exists. Still to do: URL lists and Excel.
4. **Grouping.** A sitewide template fault is one row per page. On the real site, one logo link without text was 265
   rows of `empty-anchor-text`, which buried the 4 that differed. The grid should group a check's rows by detail and
   related URL.
5. **A list of crawls.** Today a crawl is just a file path.

## Open items and owner decisions

- **Proposed, not yet confirmed:**
  - crawl modes HTTP / Rendered / Auto (Auto samples raw against rendered HTML and switches per site);
  - raw-vs-rendered diffs as a feature;
  - a per-host browser fallback when a site blocks plain HTTP.
- **Decided this round under delegation, easy to revisit:**
  - stopping after 10 minutes without an answer;
  - the checks skipped on crawls that can't support them.
- **Releases: signing and licences (raised by the owner 2026-11-15, for the release step).** Build and publish on
  GitHub for Windows, macOS and Linux, with signed installers. The owner wants ONE signing set-up, as cheap as
  possible, that signs any of their software, free or paid, whenever they release. Not just this app. Options as
  known on 2026-11-15 (check prices and eligibility when the step comes):
  - Windows: **Azure Trusted Signing** (~$10/month; signs anything, from GitHub Actions; eligibility has been
    limited: companies with ~3 years' history in certain countries incl. the UK, individuals in fewer countries).
    Fallback: a cloud OV code-signing certificate (Certum, SSL.com; ~£100–250/year). Ruled out for paid software:
    SignPath Foundation (free, open source only, signs as SignPath) and Certum's open-source certificate. EV no
    longer skips SmartScreen; a new signature still builds reputation over the first downloads.
  - macOS: Apple Developer Program, $99/year (individual), covers signing and notarising every app; no cheaper route.
  - Linux: no signing needed for AppImage, .deb, .rpm; a free GPG key if a package repository is published.
  - The updater's minisign key (`%USERPROFILE%\.shouting-iguana\`) is separate and free; its two GitHub secrets
    still need adding.
  - Total about $220/year (Azure + Apple), or ~$250–350 with a certificate instead of Azure.
  - Licences for paid software (other products, maybe later this one): to decide. Options: Keygen (free tier,
    offline checks), a merchant of record that issues keys and handles VAT (Lemon Squeezy, Paddle, Gumroad; a share
    per sale, nothing up front), or the app's own licence files signed with a private key (free).

## Known limitations

- Near-duplicate detection estimates similarity from a 256-bit fingerprint. Pages 70% to 96% alike (a heavy shared
  template) can be flagged on a large enough site, and a bucket of pages sharing much of their text is searched within
  a bound, so a near copy in one could be missed (`engine/README.md`, Near duplicates). Crawls made by engines before
  the 256-bit fingerprint skip the check until crawled again.
- Links are capped at 10,000 per page, so pages linked only from a huge HTML sitemap can look orphaned.
- Some crawls skip the orphan check: stopped, depth-limited, URL-limited, folder-scoped, with sitemaps off or none
  read, or with 1 in 20 or more of the pages it tried to read refusing the crawler or not answering. Below that share,
  a page linked only from a page that refused the crawler is still reported as an orphan. A declared sitemap that failed (other than with 404/410) skips the not-in-sitemap check. The analysis notes
  say why, and `si analyze` and `si issues` print them.
- A redirect chain is reported once, where it's entered. But if it ends at a broken URL, every hop is flagged
  `redirect-broken`.
- An hreflang alternate that redirects to a 404 is reported as non-indexable (it redirects), not as broken.
- Block pages served with status 200 (Imperva, the Sucuri JavaScript challenge) aren't recognised; their HTML is
  analysed as the page.
- robots.txt: a `Crawl-delay` line ends a run of `User-agent` lines, as a rule does. So `User-agent` lines after it
  start a new group.
- An external host that stops answering is written off for the rest of the crawl. Its remaining URLs are "Not
  checked", and links to them aren't reported as broken.
- On a depth-limited crawl, a sitemap URL that redirects has its target fetched, but not if that URL is also linked
  from beyond the limit.
- Two robots.txt checks, `robots-txt-unreachable` and `robots-txt-missing`, are proven by loopback crawl tests, not
  by plants on the synthetic site, whose own robots.txt has to work. `blocked-by-robots` and
  `robots-txt-unreachable-other-origin` are planted.
- `slow-response` reports a single measurement, so one stall during the crawl flags a page. The detail shows how much
  of the time was spent waiting for the first byte.
- `image-missing-dimensions` reads the HTML attributes. An image whose box CSS sizes is still flagged, as the
  crawler doesn't read the CSS.
- A sitemap is stored under the URL it was found at, not the URL it redirects to.
- Word counts for Chinese, Japanese and Thai count characters, which is close enough for thin-content checks.
- Title and description widths count full-width characters and emoji as two, an estimate of the room they take,
  not a pixel measurement.
- A version of the site that answers 200 is flagged `duplicate-site-version` even if its page has a canonical or an
  instant meta refresh pointing to the main site, because probes aren't parsed.
- Hreflang script subtags aren't checked against ISO 15924: any four letters pass. Rare enough that it wasn't added.
- A sitemap URL on a different protocol from the sitemap (an http URL in an https sitemap) isn't reported.
  sitemaps.org drops it; the check only compares hosts. Left on the backlog.
- The evidence a check shows (step 3b) leaves out what would need new collection or has no clear definition: which
  image is inside an empty link, a breakdown of a large page's HTML, a near-duplicate cluster's similarity, every
  failing sitemap with where each is referenced, the JSON-LD block that failed with the code around it, an alternate's
  own hreflang list, and whether `canonical-invalid` is in the HTML or a Link header ([v2-evidence.md](v2-evidence.md),
  Left out, and why). `pixelWidth` and `pixelHeight` are shown but don't sort or filter.

## How to verify

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- bench --pages 100000 --concurrency 16 \
  --max-peak-mb 400 --max-growth-mb 100 --min-pages-per-sec 250
```

CI (`.github/workflows/engine.yml`) runs both on every push to `v2`.

Latest results:

| Run | Commit | Tests | Crawl | Peak memory | Analysis |
| --- | --- | --- | --- | --- | --- |
| Local, 32 cores, quiet machine | `68d55a2` | 417 engine + 16 CLI | 35 s (2,916 pages/s) | 168 MB | 7 s |
| GitHub runner, 4 cores | `530733b` | 464 + 16 on Windows, macOS and Linux | 91 s (1,114 pages/s) | 204 MB | 12 s |

Both runs found every planted issue and flagged nothing else, with no check skipped or failing. Runner speed varies:
on `68d55a2` the runner crawled in 76 s and analysed in 7 s, and the analysis does almost the same work on both
commits.

## Working with the owner

- Keep reports short and to the point: the answer first, no essays.
- The owner delegates freely ("get it done", "do whatever is needed next"). Pushing to `master` or releasing still
  needs an explicit yes.
- `src/` is legacy: critical fixes only. New work goes in `engine/` (and `app/` for the UI).
- The owner's bar for the UI is high: show them mockups and a prototype, and get a sign-off before building each
  screen ([the UI brief](v2-ui-brief.md)).
