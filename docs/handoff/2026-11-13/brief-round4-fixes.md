# Brief: fix the round 4 review's 20 findings (Simple's screens), on `ui/r4-fixes`

The Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack Router/Query, a Tauri 2 shell
in Rust, a .NET sidecar over a localhost API). Round 4 (Simple's Home, checking screen, Summary, What to fix, the
Report, the score) was integrated at `37a58c3` on `ui/r4-integrate` and reviewed with fresh eyes. Fix every finding.

- The findings with the orchestrator's decisions (follow the DECIDED lines):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\01db96e9-0efc-4b99-b15c-66d1db13a110\scratchpad\r4-review\findings.md`.
  The reviewer's proof tests and notes are in `...\scratchpad\r4-review\proofs\` and `proof-*.log`: reuse them.
- Yours: `...\scratchpad\r4-fixes\` (make `tmp` there for `TEMP` and `TMP`; keep `checkpoint.md` there, updated after
  every fix: what's done, commit, what's next).

## Where

From `C:\Projects\ShoutingIguana`: `git worktree add -b ui/r4-fixes C:\Projects\ShoutingIguana\.claude\worktrees\r4-fixes 37a58c3`.
Work only there. **Edit, commit or push nothing in any other worktree** (the orchestrator is running the full suites in
`r4-integrate` meanwhile). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`, then
`npm ci` and `npm run build:sidecar` in `app/`. Ports: `SI_DEV_SERVER_PORT=52874 SI_DEV_SIDECAR_PORT=52875
SI_PREVIEW_PORT=52876 SI_E2E_SIDECAR_PORT=52877`, Storybook `SI_STORYBOOK_PORT=6056` (never 6406). `npm run dev` runs
the app against copies of the real dev crawls; never touch `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. Only the
engine's synthetic test site may be crawled. If you use the Playwright MCP browser, use only your ports and never close
a tab that isn't yours. **Stop only processes you started, by process id or your own ports.** Never run
`dotnet build-server shutdown`.

## Rules

- Read `CLAUDE.md`, `docs/handoff/2026-11-11/ui-slice-rules.md`, `docs/v2-ui-simple.md` (Simple's words, Keys) first.
- **Each fix: a test that fails without it, seen failing, then the fix, then seen passing.** A test that passes with
  the fix removed doesn't count. One commit per finding (or per tight group), message `App: ...` / `Shell: ...` in the
  repo's style, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Simple's words and UK English; no raw engine ids; tokens only; extend components in `app/src/ui/`; a new state gets
  a story. Never rename an existing exported function without the orchestrator's say.
- While the orchestrator's suites run, run targeted things only: single vitest files (`npx vitest run <file>`),
  single e2e specs, `cargo test` for the shell. At the end run, one at a time: `npm run typecheck`, `npm run lint`,
  `npm run format:check`, `npm test` (whole unit suite), `node --test "scripts/**/*.test.mjs"`, in `src-tauri`
  `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`, then the e2e specs round 4 owns:
  `npx playwright test e2e/simple-home.spec.ts e2e/simple-results.spec.ts e2e/report.spec.ts e2e/simple-links.spec.ts`.
  The orchestrator runs the stories and full e2e after merging.
- Update `docs/v2-ui-simple.md` "As built" where behaviour changed (one short line each). Push `ui/r4-fixes` when done.

## Report back

Per finding: fixed (commit) / changed differently (why) / not fixed (why). The checks you ran with their counts. Any
new problem you saw but didn't fix. Then stop your processes and leave your worktree in place (the orchestrator
merges from the branch).
