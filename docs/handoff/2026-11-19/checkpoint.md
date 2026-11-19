# Orchestrator's checkpoint: the UX round of 2026-11-19

Session `da833503`. Scratchpad:
`C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/`.

## What the owner asked (2026-11-19, on the installed app, garden crawl of 19 Nov)

- "Still not very happy with a lot of the UX. The UI looks nice in most cases but the UX really falls short." The
  example: Issues > Internal URL returns 4xx shows each 404 and "Found on 1 page · /blog/…" as text; the page that
  holds the broken link can't be clicked; you expand, then "Show the link in Links", then another screen; the only
  direct control is an icon at the far right.
- "You need to do a UX audit. A high-level UX audit… of all the screens… from the perspective of the user and actually
  using them as an SEO to try and find faults, diagnose faults, look for things. Sort the issues out."
- "Similar in simple mode… people are going to be probably more interested as to where the broken link is."
- "Paginated page canonicalised to page 1" is good practice: "it shouldn't really be a warning, it just should be in
  the notices."
- "Links to broken external URLs": "It's a broken link… It should be in the errors."

## Decisions

- Audited at **2560 × 1380** (the owner's window; every earlier audit and gallery was 1280 × 720), then 1280 × 720.
- The rules every screen now follows are in [`fix-common.md`](fix-common.md) (URL text is a link; open-in-browser
  beside it; the page that holds a fault is first-class; open the page at the fault with a text fragment; counts are
  links; one scroller; columns share width by need; Back keeps your place).
- Rulings per lane are in [`lane-briefs.md`](lane-briefs.md). A row's single click still selects; the URL text
  inspects. Link faults are flat tables by default.
- Not in this round, for the owner to decide: URL include/exclude patterns, JavaScript rendering, a URL-list crawl
  mode, a per-URL new/gone comparison, a "worst pages" list in Simple.
- `canonical-to-first-page` → Notice and `broken-external-links` → Error: the severity agent, in the `ui-step-3`
  worktree itself.

## State

- `2a2cc140` on `ui/step-3`: the four audits, the briefs, `app/src/lib/textFragment.ts` (`urlAtText`).
- The audit app: sidecar on 49875 (data `scratchpad/audit/data`, state `audit/state`), Vite on 49874 from the
  `ui-step-3` worktree; driver `scratchpad/audit/scripts/drive.mjs`; shots in `audit/shots`. The shell auditor left two
  synthetic crawls in that library (`c16c8832510111b2`, `09868d3ea55d0d1d`).
- Ports 50000-50059, 52353-53052, 53212-53311 and 57924-58023 are reserved by Windows on this machine: never use them.

| Lane | Branch / worktree | Ports | State |
| --- | --- | --- | --- |
| severity | in `ui-step-3` | none | done: `e02b185a`, `4a7fab91` (engine tests and bench green; Vitest green at `--maxWorkers=8`; e2e and stories updated, not run; readbench not run) |
| issues | `ui/ux-issues` / `ux-issues` | 49884… | started 07:45 on `2a2cc140` |
| data | `ui/ux-data` / `ux-data` | 49894… | started 07:45 on `2a2cc140` |
| shell | `ui/ux-shell` / `ux-shell` | 49904… | started 07:45 on `2a2cc140` |
| simple | `ui/ux-simple` / `ux-simple` | 49914… | started on `4a7fab91` |
| engine | `ui/ux-engine` / `ux-engine` | 49924… | started on `4a7fab91` |

## Next

1. When the severity agent commits: check its report, start **simple** and **engine** on that commit.
2. As lanes report: read each report, look at its after shots myself at 2560 × 1380, send it back for what still
   fails the rules.
3. Merge the five into an integration branch off `ui/step-3` (order: data, issues, shell, simple, engine; expect
   conflicts in `Url`, `GridCore`, `routing/inspector.ts`, `modeRoutes.ts`, fixtures).
4. Whole-suite gates on the merge: `verify-engine.sh`, `verify-app.sh`, `e2e:filled`, `tauri build --no-bundle`,
   `smoke:shell`, Linux Docker if `src-tauri` changed.
5. Walk the owner's complaint and each audit's tasks myself on the merged build at 2560 × 1380. Fix round if needed.
6. Merge into `ui/step-3`, update "Where step 3 stands" in `docs/v2-rebuild.md`, build the installer, and upgrade
   the owner's install only while the app is closed. Never push.
7. Tell the owner: what changed, what's left for them to decide, and that D-01 means their own garden crawl's
   sitemap figures are wrong until they crawl again.
