# CLAUDE.md

Guidance for Claude / agents working in this repo. Read this before making changes.

## What this is

**Shouting Iguana** — a Windows desktop SEO crawler & site auditor. WPF on .NET 10, SQLite per project, Playwright (headless Chromium) for JS rendering, plugin-based analysis. Think "self-hosted Screaming Frog with a plugin SDK."

## Working style with this user

- Be casual, terse, concise. Treat the user as an expert — no moral lectures, no "Here's how you can…" preamble.
- Give the answer first, then explanation if needed. If asked for a fix or explanation, provide actual code or the actual explanation — not high-level theory.
- Anticipate needs — suggest solutions the user didn't ask for when they're clearly better.
- Value good arguments over authority; flag speculation explicitly.
- Cite sources at the end, never inline. No knowledge-cutoff disclaimers, no "I'm an AI" disclosure.
- Split long answers across multiple responses rather than truncating.
- Fully implement requested functionality — no TODOs, placeholders, or "missing pieces."
- Readability wins over micro-optimization.
- **Never rename existing functions without asking permission.**

## Tech stack

| Area | Choice |
| --- | --- |
| Language / runtime | C# 12, .NET 10 (`net10.0` for libs, `net10.0-windows` for the WPF app) |
| UI | WPF + MVVM via `CommunityToolkit.Mvvm` 8.4.0 |
| Persistence | EF Core 10 + SQLite (one `.db` file per project) |
| Browser automation | `Microsoft.Playwright` 1.56.0 (Chromium, headless) |
| HTML parsing | HtmlAgilityPack + Fizzler (CSS selectors) |
| Resilience | Polly 8.x |
| Logging | Serilog 4.3 → `%LocalAppData%\ShoutingIguana\logs\shouting-iguana.log` |
| Export | ClosedXML (Excel), CsvHelper |
| Installer | Inno Setup 6 (x86 + x64) |

Windows-only. Several Core/Data types are marked `[SupportedOSPlatform("windows")]`.

## Solution layout

```
ShoutingIguana.sln
└── src/
    ├── ShoutingIguana/              WPF app — Views, ViewModels, App.xaml.cs, Styles.xaml
    ├── ShoutingIguana.Core/         Domain: Models, Services (CrawlEngine, LinkExtractor, Playwright, Robots), Repository INTERFACES, Configuration
    ├── ShoutingIguana.Data/         EF Core: DbContext, Repository IMPLEMENTATIONS, Migrations, entity Configurations
    ├── ShoutingIguana.PluginSdk/    NuGet-published SDK — IPlugin, IUrlTask, UrlContext, ReportSchema, UrlHelper
    └── ShoutingIguana.Plugins/      18 built-in plugins (BrokenLinks, Canonical, Hreflang, ... see README)
```

Plugins are built into `bin/{Config}/net10.0-windows/plugins/{AssemblyName}/` and loaded at runtime via `PluginLoadContext` (per-plugin `AssemblyLoadContext`).

## Build, run, migrate

```bash
# Build
dotnet build ShoutingIguana.sln

# Run the desktop app
dotnet run --project src/ShoutingIguana/ShoutingIguana.csproj

# Release build
dotnet build -c Release ShoutingIguana.sln
```

```powershell
# EF Core migration (interactive, prompts for name)
.\migrations.ps1
# or directly:
dotnet ef migrations add <Name> `
    --context SqliteShoutingIguanaDbContext `
    -o Migrations `
    --project src/ShoutingIguana.Data `
    --startup-project src/ShoutingIguana

# Inno Setup installers (Release, x86 + x64) → publish/installer/
.\build-installer.ps1 -Platform Both -Configuration Release

# Wipe Playwright browser cache if it gets stuck
.\cleanplaywright.ps1
```

There is **no test project** in this repo. If you add tests, create `ShoutingIguana.Tests` (**NUnit + Moq + Shouldly**) and mirror the `src/` layout.

## Entry point and bootstrapping

`src/ShoutingIguana/App.xaml.cs` does everything at startup:

1. Serilog file/debug/console sinks.
2. Global exception handlers (Dispatcher, AppDomain, TaskScheduler).
3. `Host.CreateDefaultBuilder()` → loads `appsettings.json`.
4. Registers DI: `SqliteDbContextFactory`, `ProjectDbContextProvider`, all repositories, `CrawlEngine`, `PluginRegistry`, `IPlaywrightService`, `ExcelExportService`, `NavigationService`, `ToastService`, all ViewModels (transient).
5. Background-initializes Playwright (auto-installs Chromium on first run) and loads plugins.
6. Shows `MainWindow`.

Shutdown disposes Playwright with a 5-second timeout and flushes Serilog.

## How a crawl works (the mental model)

`CrawlEngine` (`src/ShoutingIguana.Core/Services/CrawlEngine.cs`) runs in **two phases**:

1. **Discovery** — pulls URLs from `CrawlQueueItem`, opens each in a Playwright page, captures rendered HTML + headers, runs `LinkExtractor` to find more URLs, persists `Url` / `Link` / `Header` / `Image` / `Redirect` / `Hreflang` / `StructuredData` rows. Has an **adaptive loader**: starts with `waitForNetworkIdle`, falls back to a faster strategy when success rate drops.
2. **Analysis** — replays each crawled URL's stored HTML + headers through enabled `IUrlTask`s in priority order via `PluginExecutor`. No live browser. Tasks emit `Finding` rows (Severity Error/Warning/Info) and plugin-specific `ReportRow`s through the `ReportSink`.

State is thread-safe: `Interlocked` flags for `IsCrawling` / `IsPaused`, `ConcurrentDictionary` for shared maps, `ManualResetEventSlim` for pause gating. Default concurrency is 4 workers. Pause/resume works via `CrawlCheckpoint` snapshots.

URL normalization happens in `CrawlEngine.NormalizeUrl` and `PluginSdk/Helpers/UrlHelper.Normalize` — lowercase scheme/host, drop fragment, **keep trailing slash** (this matches what's stored, so dedup checks line up).

## Data layer — the per-project DB pattern

Each Project has its **own SQLite file**. The active context is resolved per scope:

```csharp
services.AddScoped<IShoutingIguanaDbContext>(sp =>
    sp.GetRequiredService<IProjectDbContextProvider>().GetDbContext());
```

`ProjectDbContextProvider` (singleton) holds the "current project" and hands back a context bound to that project's `.db`. Switching projects swaps the provider's target — every scoped resolution after that hits the new DB. **Don't cache `DbContext` references across project switches.**

Migrations live in `src/ShoutingIguana.Data/Migrations/`. The base context is `ShoutingIguanaDbContextBase`; the concrete one is `SqliteShoutingIguanaDbContext` (uses Split Query strategy).

## Plugin system

Plugins implement `IPlugin` (descriptor) + one or more `IUrlTask`s (per-URL work). They publish:

- A `ReportSchema` describing their output columns.
- Findings via `ctx.Reports.ReportAsync(...)` inside `ExecuteAsync(UrlContext, CancellationToken)`.

`PluginRegistry` discovers plugin assemblies on disk, loads each in its own `PluginLoadContext`, syncs schemas to the DB before a crawl, and exposes hot load/unload. `PluginExecutor` orders tasks by `Priority` and feeds them a `UrlContext` (URL row + rendered HTML + headers + project settings; **no live Playwright page in Phase 2**).

Build a new plugin against `ShoutingIguana.PluginSdk` (it's a NuGet package — `GeneratePackageOnBuild=true`). See `src/ShoutingIguana.PluginSdk/README.md`. Add the project to `src/ShoutingIguana.Plugins/` to ship it built-in.

## Coding conventions (match these)

- **Primary constructors** for DI everywhere — `class Foo(ILogger<Foo> logger, IBar bar) : IFoo`. Don't write old-style constructor bodies that just assign fields.
- **`.ConfigureAwait(false)` on every `await` in Core / Data / PluginSdk.** UI-layer code (ViewModels, Views) does not need it. This is enforced by precedent — commit `6b34691` was a sweep to fix violations.
- **File-scoped namespaces** (`namespace Foo;`).
- **Nullable reference types enabled** in every project. Use `?` for nullable; `null!` only when you can prove the value is set before first read (typical for EF nav properties).
- **Collection expressions** — prefer `[]` over `new List<T>()` / `Array.Empty<T>()` / `new T[0]` for empties and literals.
- **Pattern matching** — use it where it reads clearly, including multi-type `is` checks: `x is Url or Link or Redirect`.
- **C# 10+ features** — records, pattern matching, null-conditional/null-coalescing-assignment where they improve clarity.
- **`var`** for implicit typing when the right-hand side makes the type obvious.
- **LINQ + lambdas** for collection work, not hand-rolled loops, unless the loop is clearer.
- **Descriptive identifiers** — `IsUserSignedIn`, `CalculateTotalDepth` over short or cryptic names.
- **Object mapping is hand-written** — no AutoMapper or similar. Keep mapping helpers next to the type they produce.
- MVVM via `[ObservableProperty]` / `[RelayCommand]` / `[NotifyCanExecuteChangedFor(...)]`. Don't write manual `INotifyPropertyChanged`.
- Background work from the UI thread: `Task.Run(async () => { ... })`. Marshal back with `Application.Current.Dispatcher.InvokeAsync(...)`.
- ViewModels that subscribe to events implement `IDisposable` and unsubscribe in `Dispose()` (see `MainViewModel`).
- Naming: `PascalCase` types/methods/public members, `camelCase` locals, `_camelCase` private fields, `UPPERCASE` for constants, `I*` for interfaces, `*ViewModel` / `*View` / `*Dialog` / `*Service` / `*Repository` suffixes.
- Logging is `ILogger<T>` injected via DI; structured templates (`"... {Path}"`), not interpolated strings.
- Thread safety: `ConcurrentDictionary`, `Interlocked`, `lock(_lock)` are all in active use — pick the right one for the access pattern, don't lock everything.

There is **no `.editorconfig` or `Directory.Build.props`** today. Conventions live in code and in this file.

## Class / file layout

- Don't define models or records in the same file as a service, command, or handler.
- One model / record per file, named after the type.
- Put models/records in a nested `Models/` folder — inside the owning Service folder, feature folder, or alongside the handler that returns them.
- Apply this to new code. Don't retroactively split existing files unless asked.

## Error handling

- Exceptions for exceptional cases, never control flow.
- Log through injected `ILogger<T>` with structured templates.
- Global unhandled-exception handlers are already wired in `App.xaml.cs` (Dispatcher / AppDomain / TaskScheduler) — don't duplicate them; let exceptions surface so those handlers catch and log.
- Validate at boundaries (user input, deserialized JSON, plugin inputs). Trust internal callers — don't pad internal methods with defensive null-checks for values that can't be null.

## Configuration

- `src/ShoutingIguana/appsettings.json` — connection string + Serilog log levels (CrawlEngine + Data are `Debug`).
- Per-project crawl config: `ProjectSettings` (BaseUrl, MaxCrawlDepth, MaxUrlsToCrawl, CrawlDelaySeconds, ConcurrentRequests, UA, ProxyOverride, timeouts) — persisted to that project's SQLite.
- Global defaults: `CrawlSettings`, `BrowserSettings`, `ProxySettings` (in `src/ShoutingIguana.Core/Configuration/`).
- Recent-projects MRU stored in `appsettings.json` under `%LocalAppData%`.

## Gotchas worth knowing

- **First run installs Chromium.** `App.xaml.cs` calls Playwright's installer in the background. If it fails the user sees a toast — don't silently swallow.
- **Phase 2 has no live page.** Plugin tasks must work from stored `RenderedHtml` + `Header` rows. Don't write a task that assumes `ctx.Page` is non-null.
- **`Finding.DataJson` is JSON-in-a-string.** Use `GetDetails()` / `SetDetails()` on `Finding` — they handle the legacy dictionary shape.
- **Per-project SQLite isolation** sidesteps SQLite's writer-concurrency issues. Don't try to consolidate into one shared DB without redesigning the locking model.
- **Adaptive loader** in `CrawlEngine` switches strategies based on success counters. If you change loader behavior, update both branches and the counter logic, or the heuristic gets confused.
- **Styles.xaml is the design system.** Use the spacing tokens (`SpacingXs/M/L/...`) and named brushes; don't hardcode colors. High-contrast mode is supported via `SystemColors` — don't break it.
- **Plugin assemblies live in their own load contexts.** Don't share mutable static state between the host and a plugin — it won't be the same instance the plugin sees.
- **Old `net9.0` artefacts exist under `bin/`** from a prior target. Current target is `net10.0`; ignore the stale folders or `dotnet clean`.
- **Avoid N+1 EF Core queries.** Use `.Include()` / projections to `Select(...)` DTOs. Per-project SQLite makes round-trips cheap but not free, and report queries over crawled URLs blow up fast.
- **Paginate large result sets** in UI-bound repositories — don't materialize the whole Url / Finding table to the grid.

## Where things live (cheat sheet)

| Need | File |
| --- | --- |
| App startup / DI | `src/ShoutingIguana/App.xaml.cs` |
| Main window + keybindings | `src/ShoutingIguana/MainWindow.xaml` |
| Design system | `src/ShoutingIguana/Styles/Styles.xaml` |
| Crawl orchestration | `src/ShoutingIguana.Core/Services/CrawlEngine.cs` |
| Link extraction | `src/ShoutingIguana.Core/Services/LinkExtractor.cs` |
| Playwright wrapper | `src/ShoutingIguana.Core/Services/IPlaywrightService.cs` (+ impl) |
| URL helpers | `src/ShoutingIguana.PluginSdk/Helpers/UrlHelper.cs` |
| Plugin loading | `src/ShoutingIguana.Core/Services/PluginRegistry.cs`, `PluginExecutor.cs` |
| EF context | `src/ShoutingIguana.Data/SqliteShoutingIguanaDbContext.cs` (+ `Base`) |
| Per-project context resolution | `src/ShoutingIguana.Data/ProjectDbContextProvider.cs` |
| Excel export | `src/ShoutingIguana/Services/ExcelExportService.cs` |
| Repository interfaces | `src/ShoutingIguana.Core/Repositories/` |
| Repository implementations | `src/ShoutingIguana.Data/Repositories/` |
| Domain models | `src/ShoutingIguana.Core/Models/` |
| Built-in plugins | `src/ShoutingIguana.Plugins/<PluginName>/` |
| Plugin SDK guide | `src/ShoutingIguana.PluginSdk/README.md` |
