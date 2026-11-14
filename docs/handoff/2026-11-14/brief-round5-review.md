# Brief: fresh-eyes review of round 5 as integrated (finishing step 3), read-only, two reviewers

Review round 5 of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a token-protected localhost API). It is branch
`ui/r5-integrate` at `008b4b2`: four parts built at once (A `ui/r5-simple`, B `ui/r5-home`, C `ui/r5-grid`, D
`ui/r5-shell`), then merged and joined by an integration agent. Review `git diff 8ec31e6..008b4b2` (`8ec31e6` is
`ui/step-3` as merged in) and the code it touches. You did not write it and nobody has reviewed it: each part was
checked only by its builder, the integration by its integrator. **Two reviewers work at once, each one half (R1 or R2,
named in the message that launched you).** Review only your half; note anything you see in the other's in one line.

- What was asked, in the repo at `docs/handoff/2026-11-14/`: `closing-audit-findings.md` (the gaps, by number),
  `brief-round5.md` (the four parts), `brief-round5-integrate.md` (the merge), and the slice rules
  `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md`. What the builders say they did:
  `round5-reports.md`, and each part's `checkpoint.md` in the scratchpad below (`r5-simple`, `r5-home`, `r5-grid`,
  `r5-shell`, `r5-integrate`). Claims to check, not facts.
- Scratchpad: `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad`.
  Your folder there: `r5-review-1\` or `r5-review-2\` (make it, with `tmp` for `TEMP` and `TMP`; keep
  `checkpoint.md` there, updated at each phase).

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`, `git worktree add --detach
C:\Projects\ShoutingIguana\.claude\worktrees\r5-review-<1|2> 008b4b2`. **Edit, commit or push nothing in any other
worktree.** In your own you may run `npm ci` and checks. Ports: R1 `SI_DEV_SERVER_PORT=57874 SI_DEV_SIDECAR_PORT=57875
SI_PREVIEW_PORT=57876 SI_E2E_SIDECAR_PORT=57877`, Storybook 7006; R2 `58874-58877`, Storybook 7106, DevTools 58878.
Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows here. Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar; `npm run dev` runs the app against copies of the real dev crawls.
Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site is the only
site you may crawl. The integrator ran every suite green on `008b4b2`: run targeted tests, single specs, the dev
server and the engine's own tests only, never a whole app suite (`npm test`, `test:stories`, `npm run e2e`); one heavy
thing at a time. **Stop only processes you started, by their own process id or your own ports.** Never run `dotnet
build-server shutdown`. Never change the user's Windows Credential Manager entries other than ones your own test
creates and removes.

Already known, don't report again: a real screen reader not tried; installing a real update untested (needs a release
with `latest.json`); macOS menu and other OSes' installers out of scope; `GoToUrl` tests' time limit raised to 20 s
(say if you find why they're slow); Advanced Home names a site by host only (two crawls of one host on different ports
show the same name: the integrator left it, see the architecture doc).

## R1: the app (parts A, B, C and the joins, `app/src/`, `app/e2e/`)

1. **The joins**: the busy state after a stop (Home's running cards, Simple's live row, `saidWhileAnalysing`, a
   Delete confirmation that closes), the one running heading (`runningTitle` in both modes, every status), hidden
   checks in every count (`useVisibleIssueCounts`; Home, sidebar, toasts, Summary, What to fix, report, Issues'
   shares), the drop path, window titles, robots sentences on Overview and Simple.
2. **One count everywhere** (gap 3): the three nouns as `v2-ui-ia.md` now defines them, on garden, 100k and a live
   test-site crawl; every figure lands on itself (slice rules item 1); `% of total` against the right total.
3. **The stopped check** (gap 2): Simple's "so far" Summary and Advanced's "Score so far", a stop during the analysis,
   Continue after a stop, the toast said again, Open the report only with results.
4. **Advanced Home** (gap 4): every state and action, the delete confirmation, Remove with Undo, grouping, the
   running card's live figures (at most 4 updates a second, no layout jumps), keyboard and palette.
5. **The grid** (part C): resize, move, pin, saved views and their routes, Filter by / Exclude this value landing on
   exactly the rows, the folded toolbars at 200%, footer hints, hidden checks per crawl and in every crawl, exports
   leaving them out, the workbook taking filters, Ctrl+F in Source.
6. **Simple's words** (gaps 6, 7, 29): anything the words test misses (it only reads what it renders).
7. **Accessibility and the keyboard**, design-system and slice rules, tests that actually test (a test that passes
   with the feature removed is a defect).

## R2: the engine, the sidecar API and the shell (`engine/`, `app/src-tauri/`, `app/scripts/`, the API client)

1. **Engine changes**: the page definition (`CrawlSchema.PAGE`, `NOT_CLOUDFLARE`, `CrawlWriter.IsPage`,
   `ReadOutcome`): every reader of "page" agrees, nothing else's meaning shifted (issue counts, inlinks, the report,
   the score's coverage); `StartAddress.ChooseAsync` (both schemes at once: redirects, a site that answers both,
   timeouts, headers, robots, proxies); analysis on a user stop (only a user stop; quit, interrupt and failure don't;
   the busy refusals; cancel; a crash mid-analysis); `ReportOptions.HiddenChecks`/`Checks`, the export fields and
   `GET /report?hiddenChecks=`; `POST /api/proxy/test`. `CLAUDE.md`'s engine rules: only `Storage/` writes, one writer
   at a time, parse once.
2. **Security**, each with a concrete abuse scenario or none: the proxy test endpoint (what can it be made to fetch,
   and what does it return: SSRF to the user's network through the token-protected API, response bodies, timeouts);
   the updater (signature checked before install, the endpoint, downgrade, a tampered `latest.json`, installs only on
   the user's word and never mid-crawl, the public key in `tauri.conf.json`, the private key not in the repo or its
   history: `git log -p` the whole branch for it); the credential store (what is stored, under which names, what the
   page can read back, deletion when a crawl is deleted, nothing secret in settings or crawl files or logs); the data
   folder switch (paths the page can pass, relaunch, refusal while running, a folder that vanishes); the drop forwarding
   (`shell://drag-drop`: paths the page receives, what it can do with them); the window title copy; new Tauri
   capabilities and commands (each one's least privilege); the licences generator (what it reads at build time).
3. **The shell's mode words** (gap 5), the top bar re-measure (G1), `smoke:shell`'s new steps (do they fail when the
   thing breaks?).
4. Run `dotnet test engine/ShoutingIguana.Engine.slnx`, `cargo test` and `cargo clippy --all-targets -- -D warnings`
   in your worktree, and targeted engine tests of your own for what you suspect.

## Both

Verify each finding before reporting it (a failing test, Playwright against `npm run dev` or Storybook, axe, an
engine test, a request to the sidecar). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned). No style preferences, no
speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note. **Give the full list in
your final message** (the orchestrator saves it). Put proofs in your folder and name them. No file dumps. When done,
stop your processes and remove your worktree (`git worktree remove --force` on your own folder only).
