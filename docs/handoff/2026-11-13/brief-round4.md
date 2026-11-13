# Brief: Simple mode's screens (UI, round 4), three agents at once

Read `docs/handoff/2026-11-11/ui-slice-rules.md` in your worktree first: it holds the rules every UI slice follows
and the defects earlier reviews kept finding. Where it names a model in the commit line, use
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. This brief adds your part. **Three agents build round 4
at once, each one part (A, B or C, named in the message that launched you).** Read the whole brief so you know
where the others' edges are; build only your part.

The app is gaining a Simple mode for people who aren't SEOs, with Advanced (everything built so far) one switch
away. The design is done and signed off under the owner's delegation: `docs/v2-ui-simple.md`, with mockups on the
real garden crawl in `app/design/simple/` (open them from disk; `README.md` there lists them). Round 3 built the
frame: the mode (`useMode()`), the switch (Ctrl+Shift+A), the top bar's order of giving way, Simple's sidebar
(Summary, What to fix, Report), Settings, New crawl in both modes, and **the mode table: one file mapping a route
to its Simple screen**, where each of you adds rows and nothing else (the architecture doc has the one-line recipe).
The engine already serves everything Simple needs: the score (`analysis.score`, `fixFirst`, the library's
`score`), the plain words for all 87 checks, 17 categories and 17 skip reasons (`GET /api/checks`), the report and
the workbook (E5).

## Where you work

Each part has its own worktree and branch, cut from `ui/step-3` with round 3 merged (the head is in the message
that launched you):

| Part | Worktree | Branch | Ports (dev, sidecar, preview, e2e sidecar; Storybook) | Scratch folder (under `S3`) |
| --- | --- | --- | --- | --- |
| A | `.claude/worktrees/r4-home` | `ui/r4-home` | 65874-65877; 7306 | `r4-home\` |
| B | `.claude/worktrees/r4-summary` | `ui/r4-summary` | 66874-66877; 7406 | `r4-summary\` |
| C | `.claude/worktrees/r4-report` | `ui/r4-report` | 67874-67877; 7506 | `r4-report\` |

`S3` = `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad`.
Each scratch folder has a `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there at each phase boundary.

Shared files (`keyboard/registry.ts`, `api/queries.ts`, the mode table, the router, the glossary): add, don't
reorder or reformat, so the three branches merge cleanly. A component two parts need (the score ring, a severity
word, the plain check row) is built ONCE, by the part named for it below, in `app/src/ui/` with its story; the
other parts use it from `ui/step-3` once merged, or, if they need it before then, build against the mockup with a
`TODO(round4-merge)` comment naming the owner part and nothing else (the orchestrator removes these at the merge).
Everything a Simple screen says that isn't a check's words (states, banners, alerts) is the app's: write it in the
glossary's Simple words.

## Read as well, all three

- `docs/v2-ui-simple.md`: all of it. Your sections are named below.
- `docs/v2-ui-ia.md`: the shell, Routes, "The five flows" ("In Simple the five are three"), Keyboard.
- `docs/v2-ui-brief.md`: "Every view has every state", "Showing data", "Accessibility", "Performance budgets",
  "Definition of done".
- `docs/v2-ui-architecture.md`: the mode table and how to add a row, "Rules every slice follows", "Feedback", the
  live stream, "Overview".
- `docs/v2-sidecar-api.md`: "The score", "Analysis", "Checks" (the plain fields), the library, "Progress", alerts,
  "Report (E5)", "Export (E5)".
- `docs/v2-ui-discovery.md`, section "Glossary", and `v2-ui-simple.md` "Simple's words": Serious, Worth fixing,
  Worth a look (one to one with Error, Warning, Notice).

## Part A: Home in Simple, and checking a site

`v2-ui-simple.md`: "Home", "While it checks the site", "The shell in Simple" (what round 3 left for round 4: the
pill's words, "Advanced is on" with Back to Simple, if not built), "Keys". Mockups: `home.html`, `home-first.html`,
`checking.html`.

1. **Home in Simple**: the field ("Web address", Enter to check), the saved-settings line, Your sites with each
   site's score, word and state (running, stopped early with its score "so far", never analysed, analysed by an
   older engine), its favicon or the letter fallback (`hasFavicon`; if the favicon UI isn't merged yet, the letter
   only), each row's Open the report (it routes to the Report screen part C builds: a row in the table), first start.
2. **Checking a site**: the three steps, the three figures, the floor on time left, the alerts in plain words with
   their actions (each lands where Advanced's alert does), paused, stopped, working out the results, and the hand
   over to the Summary when it ends. Every figure lands on itself (rule 1 of the slice rules): a figure that is a
   link opens exactly that count.
3. **The finished toast and system notification**, in both modes, as "The report at the end" 2 says ("… is checked.
   Score 85: 6 problems to fix", See the summary, Open the report; Advanced keeps its words and gains Open the
   report).
4. Owns: the **score ring** component (`app/src/ui/`: value, band word, partial "so far" dashed, no score, in both
   themes and forced colours), used by B and C.

## Part B: the Summary, the score's breakdown, What to fix and one problem

`v2-ui-simple.md`: "The Summary" with "Every state", "What to fix" with "One problem", "The score" (what the
breakdown shows), "Plain words" (how the fields are shown), "Keys". Mockups: `summary.html`, `summary-perfect.html`,
`summary-stopped.html`, `score.html`, `fix.html`, `problem.html`, `problem-template.html`.

1. **The Summary**, every state the design lists (finished, perfect, nothing but notices, stopped early, failed,
   URL or depth limit, checks skipped, no score for each reason, never analysed, out of date, running), its five
   fixes first in the engine's `fixFirst` order with `ifFixed`, What's fine, What couldn't be checked, and Open the
   report as the primary action.
2. **The score's breakdown** dialog (Esc closes it), from `score.lines`, `covers`, the bands, the version, opened
   from the Summary (and from Advanced's Overview by part C).
3. **What to fix**: Serious, Worth fixing, Worth a look, Couldn't check, Nothing wrong; J, K, Enter; hidden checks
   left out as Advanced leaves them out.
4. **One problem**: what's wrong, why, how to fix it, who usually fixes it (`fixer`), the One fix callout for a
   likely template (`likelyTemplate`, `plainOneFix`), its pages with title then address (the groups' and rows'
   reads Issues uses), Copy the list (Ctrl+Shift+C; full addresses and the technical name), Save as a spreadsheet
   (E5's `check` export through the save dialog and the export toast: use the handover slice's pieces if merged,
   else the API client directly with the same dialog and toast components), "See it in Advanced".
5. Owns: the **severity word** and the **plain check row** components, used by C's report screen if it needs them.

## Part C: the Report screen, and the score in Advanced

`v2-ui-simple.md`: "The report" (all of it, especially "The Report screen" and "The report at the end" 3),
"The score" (as Advanced shows it), "Keys" (Ctrl+P). The API doc's "Report (E5)" section on what a `srcdoc` frame
needs: the app's `style-src 'self' 'unsafe-inline'` works, a nonce beside it unstyles the report, and printing the
frame needs `sandbox="allow-same-origin allow-modals"`. Mockups: `report-screen.html`, `report.html`,
`advanced-overview.html`.

1. **The Report screen, in both modes, one screen**: the toolbar (title, site, date, page count; Full report |
   Summary page; Spreadsheet of everything…; Save…; Print or save as PDF, the primary action, Ctrl+P), the engine's
   HTML in a sandboxed frame with scripts off, light in both themes as paper is; Save through the native dialog and
   the export toast with Open folder, default names as the design gives; states: building (a skeleton sheet),
   never analysed (Work out the results), out of date, stopped early. It is the third sidebar item in Simple and
   the eighth in Advanced (Ctrl+3 / Ctrl+8): add its row to the mode table and its sidebar entry where round 3
   left the place for it. **Prove printing in the real shell** (`npm run smoke:shell` drives the release build:
   the architecture doc says how; it needs this desktop session): the print dialog opens on the frame, and the
   shell's CSP has no nonce that unstyles the report.
2. **The score in Advanced**: Overview shows it beside the answer, and it opens B's breakdown dialog.
3. **Links into the report** from Home's rows (A), the Summary (B) and the finished toast (A) all land here.

## All three

- **Every state** in Storybook (both modes where the screen is in both, both themes, forced colours, 200% zoom,
  reduced motion), and e2e for your flows on the engine's test site. A test that walks every figure of your screens
  on real crawls (garden and 100k copies, analysed by the current engine) and compares it with where it lands, and
  with the mockup's numbers on garden (85, Good; the design's counts).
- Keys and palette entries as the design's Keys table gives them, in the current mode only.
- Measure: a screen's first paint with its data inside the brief's budgets on the 100k crawl; the mode switch on
  your screens to a visible response inside 100 ms.
- Before your final checks, `git merge ui/step-3` into your branch (another part may have landed) and run every
  check on the merged result.

## Report back

As `ui-slice-rules.md` asks, plus: each screen against its mockup (what differs and why); the rows you added to the
mode table; the components you own and who uses them; every `TODO(round4-merge)` you left; what the engine
couldn't give you (don't work around it: report it); what you left out and why.
