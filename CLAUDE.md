# CLAUDE.md

Guidance for agents working in this repo. Read this before making changes.

## What this is

**Shouting Iguana** `2.0.3` — a cross-platform desktop SEO crawler and site auditor.

- `engine/` — headless .NET 10 crawl engine, the `si` CLI, and the localhost sidecar. One SQLite file per crawl.
- `app/` — React and TypeScript UI in a Tauri 2 shell. The shell starts the engine as a sidecar.
- `docs/` — the rebuild plan and the UI notes. **Where step 4 stands** in [`docs/v2-rebuild.md`](docs/v2-rebuild.md) is the only status. Update that section before you stop, and commit it.

All work is on `master`. Steps 1 to 4 of the rebuild are shipped; step 5 (integrations) starts only at the owner's word.

The WPF app is not in this branch. It is the tag `legacy-v1`. Do not move or delete that tag. v2 has no plugin system. The `ShoutingIguana.PluginSdk` NuGet package is unused here and stays published.

## Pushing and releasing

The owner's rule since 2026-11-24: push and release without asking.

- Push `master` when a piece of work is finished and checked. A push to `master` runs no workflow.
- When a batch is done and checked, release it: set the version (the four places below), commit, push `master`, then push the tag `app-v<version>`. The tag runs the release workflow, which builds every OS installer. That run costs GitHub minutes (macOS bills at ten times Linux), so get it green locally first and do not tag twice for one batch.
- Run every check locally before pushing: `dotnet test engine/ShoutingIguana.Engine.slnx`, and the app checks in [`app/README.md`](app/README.md) (`smoke:shell` included). The app and engine workflows stay manual-only (`workflow_dispatch`): do not add a push or pull-request trigger to any workflow, and do not run them by hand without the owner's word.
- Work goes on `master`. Do not push work branches or backups.

Do not force-push.

## Layout and commands

```
engine/ShoutingIguana.Engine.slnx
engine/src/ShoutingIguana.Engine/      crawler, parsing, storage, SEO checks
engine/src/ShoutingIguana.Cli/         si
engine/src/ShoutingIguana.Sidecar/     si-sidecar, the localhost API
engine/src/ShoutingIguana.TestSite/    synthetic site
engine/tests/
app/                                   React UI
app/src-tauri/                         Tauri 2 shell
```

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -- crawl <url>
```

The desktop app: see [`app/README.md`](app/README.md). `npm run tauri dev` from `app/` runs the shell.

`engine/Directory.Build.props` targets `net10.0` with warnings as errors, and sets `Version` and `InformationalVersion`. Package versions live only in `engine/Directory.Packages.props` (central package management), never in a csproj. The app version is the same string in `app/package.json`, `app/src-tauri/tauri.conf.json` and `app/src-tauri/Cargo.toml`.

Gate: `si bench --pages 100000` must find every planted page and issue, flag nothing else, and keep memory flat.

## Engine rules

- One SQLite file per crawl. Only `engine/src/ShoutingIguana.Engine/Storage/` writes to it: `CrawlWriter` while a crawl runs, `IssueWriter` for analysis, never both at once. Never open a write path anywhere else.
- HTML is parsed once (`Parsing/HtmlPageParser`). Add new facts to `PageFacts` instead of re-parsing.
- SEO checks live in `Analysis/Rules`. Every issue is defined in `Analysis/IssueCatalog.cs`. Issue ids are stored in crawl files: never rename one. A new check needs a planted case on the synthetic site (`TestSite`, products or `SyntheticSite.Plants.cs`) with its expected count, because the tests compare the whole issues table.
- The sidecar contract is [`docs/v2-sidecar-api.md`](docs/v2-sidecar-api.md).

## Coding conventions

Match the engine.

- Primary constructors for DI: `class Foo(ILogger<Foo> logger, IBar bar) : IFoo`.
- `.ConfigureAwait(false)` on every `await` in `engine/`.
- File-scoped namespaces.
- Nullable reference types on. Use `?` for nullable; `null!` only when the value is set before first read.
- Collection expressions: `[]` for empties and literals.
- `var` when the right-hand side makes the type obvious.
- LINQ for collection work, unless a loop is clearer.
- No AutoMapper. Hand-written mapping, next to the type it produces.
- One model or record per file, named after the type, in a `Models/` folder. Don't define a model in the same file as a service or command.
- Never rename an existing function without asking.
- Logging is `ILogger<T>` with structured templates, not interpolated strings.
- Exceptions for exceptional cases. Validate at boundaries (user input, deserialized JSON). Trust internal callers.

## Working style

Be casual, terse, and concise. The owner is an expert. Give the answer first, then the code or the explanation. Fully implement what was asked: no TODOs, placeholders, or missing pieces. Readability over micro-optimisation.
