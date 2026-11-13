# Brief: the export gaps the hand-over UI found (engine E7, small) and the app's flaky inspector test

Shouting Iguana v2: a .NET 10 engine (`engine/`) with a sidecar API, and a React app (`app/`). Export (E5) exports
every grid as CSV or Excel. The UI slice that put export on every grid found three things the engine can't export,
and one unit test that fails under load. You close them.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e7`, branch `ui/e7`, cut from `ui/step-3` (head named in
  the message that launched you). Work ONLY there. Small commits, each ending with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push, never merge your branch anywhere, never touch
  another worktree. No bare `git stash`. **Never rename an existing function, type or issue id.**
- Scratch folder: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\e7\`
  (has `tmp` for `TEMP` and `TMP`). Keep `checkpoint.md` there at each phase boundary.
- Ports for anything in `app/`: `SI_DEV_SERVER_PORT=52874 SI_DEV_SIDECAR_PORT=52875 SI_PREVIEW_PORT=52876
  SI_E2E_SIDECAR_PORT=52877`, `SI_STORYBOOK_PORT=6056`. Stop only processes you started, by their own process id or
  your own ports. Never run `dotnet build-server shutdown`. One heavy thing at a time; other agents load the machine.
  If a single-file vitest run says "failed to find the runner", clear `node_modules/.vite*` (a stale cache).

## Read first

`CLAUDE.md` (engine rules: only `Storage/` writes a crawl file; an export only reads). `docs/v2-sidecar-api.md`:
"Export (E5)" and both E5 engine-change sections, "Sitemaps", "robots.txt", "Links to URLs" (`linkEnds`). The
hand-over UI's notes: `docs/v2-ui-architecture.md`, section "Hand over…", and `app/src/export/offers.ts` (where the UI
says these can't be exported).

## What to do

1. **Export of the sitemaps list** (what `GET /sitemaps` lists, with the list's columns and the counts the screen
   shows) and **of robots.txt origins** (what `POST /robots` lists, with its `search` and `blocked` filter): new export
   kinds, the same job, event, problems and CLI (`si export --kind …`) as every other kind, the same column titles and
   value words the screens show, read-back tests row for row against the screens' own reads.
2. **`linkEnds` on a URL export**: `ExportFields` takes `linkEnds` for the `urls` kind exactly as the URL grid's read
   takes it; a read-back test row for row against the grid's read with the same `linkEnds`.
3. **The app**: remove the "can't be exported" paths in `app/src/export/` for these three and offer them as every
   other grid's export is offered (the shared dialog and toast), with their default file names in the same pattern;
   the TypeScript types, client functions and the mock transport's routes; tests; stories for any new state.
4. **The flaky unit test** `app/src/inspector/Inspector.test.tsx:1303` ("shows the summary's skeleton only once a
   load has taken a moment"): it fails in about half of full `vitest` runs under load. Make it deterministic (fake
   timers or an explicit gate on the load, not a longer wait), with the behaviour it checks unchanged; prove it with
   the whole unit suite run five times, two of them while something heavy runs.
5. **Docs**: the API doc ("Engine changes E7 made" at the end, after every other section), the CLI's README.

Every engine change gets a test you have seen fail without it.

## Checks, all seen green by you on the final commit

Merge the latest `ui/step-3` into your branch first if it has moved. Then: Release build (warnings are errors),
`dotnet test engine/ShoutingIguana.Engine.slnx`, the 100k bench once, `si readbench --gate`; from `app/`: `npm ci`,
typecheck, lint, format:check, `npm test` (both projects), build-storybook, the three story runs, build:sidecar,
the full e2e.

## Report back

Short: commits; each new kind with its CLI and default file name; the flaky test's cause and fix; test counts; what
was verified and what was not.
