# Step 3b: build title and description length in pixels (stage B)

## Why
The owner (2026-11-17): "should those checks measure pixel width, as Screaming Frog does, instead of characters?" — "Yes".
Checks: `title-too-long`, `title-too-short`, `description-too-long`, `description-too-short`. Their ids never change.

## Read first
- `CLAUDE.md` (engine rules: only Storage writes; HTML parsed once into PageFacts; checks in IssueCatalog; never rename
  an issue id or a function; a planted case + expected count for every check change, because the tests compare the
  whole issues table; conventions).
- The design: `docs/handoff/2026-11-17/pixels-design.md` (on `ui/step-3`; research, sources, width table plan,
  measuring, cut point, settings, API, UI, words, plants) and its scripts in `docs/handoff/2026-11-17/tools/`
  (`measure.py`, `thresholds.py`, `plants.py`).
- `docs/v2-rebuild.md`, "Where step 3 stands", the step 3b block (context; don't edit it — the caller does).
- `docs/v2-evidence.md` (detail strings and evidence columns per check: keep the contract; update its rows for these
  four checks) and `docs/v2-sidecar-api.md` (analysis settings endpoint).

## The owner's orchestrator's rulings (these win over the design where they differ)
- Title: too long over **580 px** (measured Google cut ~580; Screaming Frog's 561 flags 97 garden titles vs 69 —
  too strict); too short under **200 px**.
- Description: too long over **985 px**, too short under **400 px** (Screaming Frog's).
- Widths: Liberation Sans 2.1.5 advance widths (SIL OFL; metric-compatible with Arial) embedded as a table; titles at
  20 px, descriptions at 14 px; CJK, emoji and unknown code points at 1 em (as the design says). Keep the font licence
  notice with the table (and in any third-party notices file the app ships, if there is one: find it).
- Simple's cut point: the last whole word that fits ("…Garden Sofa", not "…Garden Sofa Se"), as Google does.
- Saved character limits (custom settings already saved by users) are converted, not reset: 9.4 px per character
  for titles, 6.2 px per character for descriptions (rounded to whole px). Defaults that were never changed become the
  new pixel defaults.
- Renaming the SETTING FIELDS is fine where needed for correctness (e.g. `titleMaxLength` → `titleMaxPixels`), with
  the old names still read when loading saved settings/crawl options so nothing saved is lost. Do NOT rename any
  function or issue id.
- Advanced detail: `612 px, over 580 (64 characters)` (and the short/description equivalents). Simple: where Google
  will likely cut it, e.g. "Google will likely cut this title off after '…Garden Sofa'" — no pixels in Simple.
  Simple titles stay as the check audit set them ("Titles that may be cut off in search results", "Search
  descriptions that may be cut off").
- App Settings > Analysis: the limits in px, each with a characters hint (about N characters).
- `AnalysisFreshness.VERSION` 11 → **12**, with a test that 11 is stale.

## Where
Worktree `C:/Projects/ShoutingIguana/.claude/worktrees/3b-pixels`, new branch `ui/3b-pixels` off `ui/step-3` (at
50b2338): `git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-pixels C:/Projects/ShoutingIguana/.claude/worktrees/3b-pixels ui/step-3`.
Uppercase `C:/` paths always (a lowercase `c:` cwd breaks Vitest: "failed to find the runner"). Small logical commits,
each message ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (use a message file with `git commit -F`).
NEVER push. Don't touch other worktrees or the installed app (`%LOCALAPPDATA%\Shouting Iguana`). Stop only processes you
started.

## Build
1. Engine: the width table + a measurer (its own small class, tested against the design's measured examples), the four
   rules in pixels, detail strings, the cut point (exposed so Simple can show it: decide with the evidence contract
   whether it's a detail field, a target fact or computed in the app — prefer the engine computing it once), analysis
   settings in px with the old names still read, the sidecar settings endpoint, the CLI flags if any take these limits
   (keep old flags working as aliases if they exist).
2. Planted cases on the synthetic site: titles that pass in characters but fail in pixels and vice versa (wide "WWW…"
   and narrow "iiil…"), the same for descriptions, a CJK title, and expected counts; the whole-table tests updated
   deliberately; the 100k bench's planted counts if they move. Each change with a test seen failing first.
3. App: Settings > Analysis in px with the characters hint, types, words (Advanced and Simple's plain-words file),
   Simple's "cut off after" line where the check's pages are shown, saved-settings migration, unit tests. Recapture
   the engine fixtures from the merged sidecar (`app/src/test/simple/engine-checks.json`, `garden-analysis.json`; find
   how the integration captured them in git log of `3721374` and `c6a9b40`).
4. Docs: `docs/v2-pixel-widths.md` (the design as built, with sources), `docs/v2-evidence.md` rows, the sidecar API
   doc, the analysis-settings section of whatever doc lists them, `docs/v2-check-audit.md` rows for the four checks
   ("changed: pixels"), and the comparison doc's "In part (7)" if it says we count characters.

## Verify (the machine is quiet now)
- `bash docs/handoff/2026-11-13/tools/verify-engine.sh <worktree> <logDir>` (build, tests, 100k bench gate, readbench).
- `bash docs/handoff/2026-11-13/tools/verify-app.sh` (read its usage; pick free ports, not 62950/7695 if in use).
- e2e specs touching settings, issues, evidence and Simple; `e2e:filled`.
- Logs under `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/pixels/`.
- Then re-analyse COPIES of the three real crawls (`.../scratchpad/audit3b/crawls/*.sicrawl`, copy first; never the
  originals) with the CLI and report the four checks' counts before (characters) and after (pixels) per site, with 5
  sample flags each showing title, px, characters and the Simple cut text.

## Report (under 500 words)
Commits, test counts, verify summary lines, the real-site before/after counts with samples, anything you decided that
the design or this brief didn't settle, and anything not done.
