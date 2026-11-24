# Shouting Iguana desktop app

The v2 desktop app: a React and TypeScript UI in a Tauri 2 shell, with the .NET engine as a sidecar behind a
token-protected localhost API. How it fits together is in [the architecture doc](../docs/v2-ui-architecture.md). The
bar it builds to is [the UI brief](../docs/v2-ui-brief.md).

`spike/` (the stack spike) and `design/` (the visual directions and fixtures) live here too, but aren't part of the
app: its build, lint, tests and Storybook leave them out.

## Requirements

- Node 24 and npm 11.
- The .NET 10 SDK, to build the sidecar and the CLI.
- For the shell: Rust through rustup (with the MSVC toolchain and the "Desktop development with C++" workload on
  Windows) and the WebView2 runtime, which Windows 11 has. `src-tauri/rust-toolchain.toml` pins the Rust version, and
  rustup installs it on the first build.

```powershell
npm ci
npm run build:sidecar   # the engine as src-tauri/binaries/si-sidecar-<target triple>.exe
```

Build the sidecar again whenever the engine changes. `npm run build:sidecar -- --engine-root <folder>` builds it from
another checkout of the engine, such as a clean worktree while someone edits this one.

## Run

| Command | What runs |
| --- | --- |
| `npm run dev` | The UI in a browser at `http://localhost:51874`, against a real sidecar on copies of your dev crawls |
| `npm run dev:mock` | The UI against the mock sidecar, which serves the design fixtures. No engine needed |
| `npm run tauri dev` | The desktop app, with hot reload. The shell starts its own sidecar |
| `npm run storybook` | Every component in every state, at `http://localhost:6006` |

`npm run dev` never touches your own library or crawls:

- It copies the crawls in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls` into `%TEMP%\si-app-dev\data`, and keeps a copy
  from run to run while its original hasn't changed (its size and modified time, kept in
  `%TEMP%\si-app-dev\copied-from`), so a copy you analysed stays analysed. When an original changes, its copy is made
  again. With no dev crawls, or with `--synthetic`, it makes a small crawl of the engine's synthetic site with the CLI.
- The sidecar gets a fresh state folder each run, a fixed development token and only the dev server's origin.
- Ctrl+C stops both. Set `SI_DEV_LOG=1` to see the sidecar's log.

The mock needs the fixtures, which git ignores. Make them with `node app/design/data/extract/run-all.mjs` (from the
repo root; see `design/data/README.md`). Without them, every mock answer says so. It answers the grids as the sidecar
does, failures included: a running or unanalysed crawl has no URL summary (its columns are left out and
named), a sort or filter on them is `needs-analysis`, a query the engine can't run is `invalid-query`. The inspector
answers for the few URLs the fixtures hold it for.

## Test

| Command | Checks |
| --- | --- |
| `npm run typecheck` | TypeScript, strict |
| `npm run lint` | ESLint: typed rules, React hooks, jsx-a11y, TanStack Query, Storybook |
| `npm run format:check` | Prettier (`npm run format` fixes) |
| `npm test` | Vitest unit and component tests (`npm run test:watch` while working) |
| `npm run e2e` | Playwright against the real sidecar, with axe: zero violations allowed. `e2e/states.spec.ts` also checks what only a browser shows: state indicators at 3:1 and their text at 4.5:1 in both themes and under forced colours, focus coming back after an overlay, the grid's cursor kept in view, and toasts in the app's look |
| `npm run build-storybook` | Storybook builds |
| `npm run test:stories` | axe on every story of the built Storybook, in both themes: zero violations allowed (`-- --forced` under forced colours, `-- --shots=<folder>` to screenshot each) |
| `node --test "scripts/**/*.test.mjs"` | The scripts' own tests: ports, crawl copies, signing, the shell walk's options and steps |

`npm run e2e` builds the app for the test (`--mode e2e`), starts the sidecar on a temp data folder holding one crawl,
and serves the build. The crawl is `SI_E2E_CRAWL` if set, otherwise a small synthetic crawl made with the CLI (kept in
`.engine-build/crawls` for the next run). The tests need its analysis to stand, as the sidecar judges it: a kept
synthetic crawl an older engine analysed is made again, and an out-of-date `SI_E2E_CRAWL` has its copy analysed again
(never the file itself). An out-of-date analysis would put its "Analyse again" offer on the screens, a Tab stop of its
own. It needs `npm run build:sidecar` first, and Playwright's Chromium (`npx playwright install chromium`).

`e2e/links-sweep.spec.ts` walks every count on Links (the header's under each view, every value of every facet, the
inspector's "All N in Links") against the screen it lands on and the sidecar's own count. It runs with the rest on the
synthetic crawl; to sweep a real crawl, every preset included, give it the file:
`SI_E2E_CRAWL=<a .sicrawl file> npx playwright test e2e/links-sweep.spec.ts`. Most other specs look for the synthetic
site's planted pages, so they are run on the synthetic crawl only.

The setup also serves the engine's synthetic test site (the CLI's `testsite`, on a free port), which the Overview's
tests crawl live from Home: watched to the answer, paused, resumed, stopped and continued. They never crawl a real
site, and they stop and delete every crawl they start. The CLI is built into `.engine-build`, so a `si` left running
from there (a test site started by hand) keeps the build from replacing it: stop it first.

## Build the desktop app

```powershell
npm run tauri build      # src-tauri/target/release/shouting-iguana.exe, and a per-user NSIS installer in bundle/
npm run check:orphans    # closing the app, or killing it, leaves no sidecar behind
npm run smoke:tauri      # the built app, in its own WebView2: it shows the library from a temp data folder
npm run smoke:shell      # the five flows walked in the built app: keys, the tray, quitting, the engine restarted
powershell -File scripts/smoke-installer.ps1   # the installer: silent install, .sicrawl, the engine alone, a run, silent uninstall
node scripts/smoke-sidecar.mjs --sidecar <si-sidecar>   # a packaged engine alone: it crawls and analyses a two-page local site
```

On Linux and macOS the installer's test is `bash scripts/smoke-installer.sh deb|appimage|dmg <file>`: it installs
the `.deb` with apt (or runs the AppImage, or mounts the disk image and verifies its signature), runs the engine
alone, then the app on temporary folders under a virtual display, and checks the shell started its engine, the
window's page called it, and stopping the app leaves no `si-sidecar`. The release workflow runs it for Linux in fresh
`ubuntu:22.04` and `debian:13` containers. `docs/handoff/2026-11-24/tools` has the same build and test run in
Docker from Windows. `scripts/smoke-engine.ps1 -Exe <shell>` is the Windows form of the app check, for a machine
where `smoke:tauri` can't attach to the window.

Tauri's build needs the sidecar binary to exist, so on a fresh clone or in CI run `npm run build:sidecar` first.

The smoke tests and the orphan check run the app with its own temporary profile (`SI_APP_PROFILE_DIR`), so they
never touch your own copy of the app and run beside it. `smoke:shell` drives the real window over WebView2's DevTools
port and, for what that can't reach (keys as the webview itself receives them, the tray icon, native dialogs),
`scripts/shell-window.ps1`; what it walks and why is in the architecture doc's "The shell smoke walk". It crawls only
the engine's test site, takes about a minute, and needs a desktop session, so CI doesn't run it. The installer smoke test installs into a temporary folder,
but it does register `.sicrawl` and add Start menu and desktop shortcuts until it uninstalls: run it on a build machine
or with a test product name.

`scripts/sign.mjs` Authenticode-signs the sidecar (from `build:sidecar`), the shell, the uninstaller and the installer
(Tauri's `signCommand`) when a certificate is configured, and otherwise skips them, saying so. Releases come from the
`App release (v2)` workflow, on a push of a tag `app-v<version>`. The macOS app is ad-hoc signed (`signingIdentity`
`"-"` in `tauri.conf.json`, with the hardened runtime off, because the .NET engine has to JIT), and the AppImage is
given `libssl.so.3` from the build machine (`/usr/lib/x86_64-linux-gnu`, so an AppImage is built on Debian or Ubuntu,
x64) to go with the `libcrypto` it already carries.

## Environment variables

| Variable | Used by | Does |
| --- | --- | --- |
| `VITE_TRANSPORT` | The UI | `mock` serves the fixtures instead of calling a sidecar (`.env.mock`) |
| `VITE_SIDECAR_PORT`, `VITE_SIDECAR_TOKEN` | The UI in a browser | Where the development sidecar is. Set by `npm run dev` and the e2e setup; the shell passes them over IPC instead |
| `SI_APP_DATA_DIR`, `SI_APP_STATE_DIR` | The shell | Give its sidecar other data and state folders, for tests |
| `SI_APP_PROFILE_DIR` | The shell | A test run's own folder for preferences, window state, the WebView2 profile, the engine's unpacked files and the log; it also keeps the run out of single instance |
| `SI_SIGN_CERT_THUMBPRINT` | `scripts/sign.mjs` | Sign with this certificate from the Windows certificate store |
| `SI_SIGN_PFX_BASE64`, `SI_SIGN_PFX_PASSWORD` | `scripts/sign.mjs` | Sign with this PFX (base64) instead: for CI secrets |
| `SI_SIGN_TIMESTAMP_URL` | `scripts/sign.mjs` | The RFC 3161 timestamp server (DigiCert's by default) |
| `SI_DEV_SERVER_PORT`, `SI_DEV_SIDECAR_PORT`, `SI_PREVIEW_PORT`, `SI_E2E_SIDECAR_PORT` | The scripts and Vite | Move a development port (see Ports) |
| `SI_ENGINE_ROOT` | The scripts | Build the CLI from another engine folder |
| `SI_E2E_CRAWL` | `npm run e2e` | The crawl file to test against |
| `SI_DEV_LOG` | `npm run dev` | Print the sidecar's log |

## Ports

Fixed by default, so the shell's dev URL and the sidecar's allowed origins can name them (`scripts/ports.mjs`): the dev
server 51874, the development sidecar 51875, `vite preview` 51876 and the e2e sidecar 51877. Each moves with its
variable (`SI_DEV_SERVER_PORT`, `SI_DEV_SIDECAR_PORT`, `SI_PREVIEW_PORT`, `SI_E2E_SIDECAR_PORT`), so two runs on one
machine needn't collide. `npm run tauri dev` on another dev server port also needs `--config` with that `devUrl`.

## Dependencies

Every npm package is pinned to an exact version published at least two weeks before it was added, and installed with
`npm install --before=<that date>` so the lockfile's transitive packages are as old. Crates are pinned the same way
in `src-tauri/Cargo.toml`, the GitHub Actions the workflows use to a commit, and Rust in
`src-tauri/rust-toolchain.toml`.
