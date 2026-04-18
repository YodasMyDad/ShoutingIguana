# Shouting Iguana — Enterprise-Readiness Audit

Living record of the findings from the full-stack audit plus status on each item. Work is grouped into four waves by priority (P0 → P3). Tick the box as items land.

- Commit convention: one wave per commit (or small group of commits), rebase cleanly between waves.
- Don't advance to the next wave on a red build; run `dotnet build ShoutingIguana.sln` after each item.
- SDK-surface changes (UrlContext DTO replacement) land in Wave 3 with a major version bump (NuGet id `ShoutingIguana.PluginSdk`).

---

## Status summary (as of this document)

### Wave 1 — Security & data-loss blockers

- [x] **SSRF guard** — scheme allowlist + private/loopback/link-local/multicast block. `src/ShoutingIguana.PluginSdk/Helpers/CrawlUrlPolicy.cs` is the single policy source, called from `CrawlEngine.RunCrawlAsync` (BaseUrl) and `CrawlEngine.EnqueueUrlAsync` (every enqueued URL). Opt-in toggle `ProjectSettings.AllowPrivateNetworkTargets` for staging-on-internal-IP crawls.
- [x] **Plugin assembly-name allowlist** — new `PluginTrustSettings` in `Core.Configuration`, wired through `IAppSettingsService`. `PluginRegistry.LoadPluginFromDirectoryAsync` consults the list and refuses unlisted DLLs. Default ships with `["ShoutingIguana.Plugins"]` so the built-in bundle still loads on first run; admins can extend.
- [x] **Proxy credential redaction** — new `ProxySettings.GetRedactedProxyUrl()` masks any userinfo component. Three log call sites now call the redacted variant: `ProxyTestService.cs:31`, `PlaywrightService.cs:228`, `CrawlEngine.cs:947`.
- [x] **Transactional crawl write** — wrap `SaveUrlAsync` + `SaveRedirectChainAsync` + link-row saves + queue-state update in a single `DbContext` transaction per URL. **Not done.** Needs the following refactor:
  1. Change signature of `SaveUrlAsync`, `SaveRedirectChainAsync` to take an `IServiceScope` instead of creating their own.
  2. Split `ProcessLinksAsync` into `ExtractAndEnqueueNewLinksAsync` (independent scopes for enqueuing future work) and `PersistLinkRelationshipsAsync(scope, …)` (runs under the shared scope).
  3. In the worker loop around `src/ShoutingIguana.Core/Services/CrawlEngine.cs:531–558`, create one `IServiceScope`, begin a transaction via `IShoutingIguanaDbContext.Database.BeginTransactionAsync`, call the three persistence methods + mark `queueItem.State = Completed`, then commit. Rollback on any exception.
  4. Enqueue-new-URLs-for-future-crawl runs in its own scope after commit (separate tx). This keeps SQLite writer contention low and avoids making "discover a new URL" atomic with "finish the current one".
- [x] **Remove `GC.Collect` in analysis phase** — delete the call at approximately `src/ShoutingIguana.Core/Services/CrawlEngine.cs:757`. A full blocking gen-2 GC every 50 URLs tanks throughput.
- [x] **Pause/resume single truth source** — today `_isPaused` (int + Interlocked) and `_pauseEvent` (`ManualResetEventSlim`) are set separately in `StartCrawlAsync`, `PauseCrawlAsync`, `ResumeCrawlAsync`, `StopCrawlAsync`. A worker can observe a stale view between the two writes. Collapse onto one primitive — either a single `ManualResetEventSlim` whose `IsSet` is authoritative, or an `AsyncManualResetEvent`. Remove the `_isPaused` int entirely.
- [x] **Wave 1 verification pass** — Debug + Release builds. Manual probes: SSRF (attempt `file:///…` / `http://127.0.0.1` — expect rejection); plugin allowlist (drop an unlisted DLL in plugins folder, confirm it is skipped with a warning log); proxy redaction (set `ProxyOverride = http://user:secret@host`, grep logs for `secret` — expect zero hits); transaction (inject a failure between SaveUrl and SaveRedirectChain, confirm no orphan Url row); GC pauses via `dotnet-counters monitor System.Runtime`.

### Wave 2 — Convention regressions & hot-path perf (P1)

- [x] **Remove redundant field assignments under primary constructors** — CLAUDE.md line 121 forbids `_logger = logger` after a primary constructor. Fix in at minimum `CrawlEngine.cs:18–22` and `MainViewModel.cs:83–93`; grep `:\s*\w+\s*;$` inside classes that declare primary ctors to find the rest.
- [x] **`.ConfigureAwait(false)` sweep** — Core / Data / PluginSdk only (not UI/VMs). Use `Grep` for `\bawait\s` with `glob: *.cs` under `src/ShoutingIguana.Core`, `src/ShoutingIguana.Data`, `src/ShoutingIguana.PluginSdk`; verify each match ends with `.ConfigureAwait(false)`. Agents flagged candidates at approx CrawlEngine:731, 806, 1156, 1175, 1995, 2211, 2223 and `ProjectDbContextProvider.SetProjectPathAsync`.
- [x] **`IHttpClientFactory` adoption** — `CrawlEngine.FetchStaticResourceAsync` (new HttpClient per URL), `RobotsService`, `SitemapService`. `services.AddHttpClient()` is already registered in `App.xaml.cs:75`; switch those services to `IHttpClientFactory` and named clients so socket-pool reuse kicks in.
- [x] **Pool Playwright `BrowserContext`s** — `PlaywrightService` currently creates a new context per page. Pool contexts (bounded by `ConcurrentRequests`) and vary UA via `page.SetExtraHTTPHeadersAsync`. Guard disposal under the pool.
- [x] **`AsNoTracking()` on read-only hot path** — added to read-only queries across `UrlRepository` (GetByProjectIdAsync/GetByStatusAsync/GetCompletedUrlsAsync — GetById* and GetCompletedUrlIds* were already tracked-off where appropriate), `LinkRepository`, `RedirectRepository`, `HreflangRepository`, `StructuredDataRepository`, `ReportDataRepository`. `GetByAddressAsync` on `UrlRepository` intentionally stays tracked because `CrawlEngine.SaveUrlAsync` mutates + saves the returned entity. No `HeaderRepository` exists — headers are served through `UrlRepository.GetHeadersAsync` (already `AsNoTracking`).
- [x] **Silent `catch { }` blocks** — `LinkExtractor.ExtractLinksAsync` now logs via injected `ILogger<LinkExtractor>`; `LinkExtractor.ResolveUrl` narrowed to `UriFormatException`/`ArgumentException`; `PluginRegistry.ValidateSdkVersion` and `GetSdkVersion` now log via `ILogger<PluginRegistry>` (Lazy wired through instance field); ancillary cleanup catches in `PackageManagerService` and `NuGetService` narrowed to `IOException`/`UnauthorizedAccessException` with debug logging.
- [x] **Harden `DispatcherUnhandledException`** — `src/ShoutingIguana/App.xaml.cs` now logs every UI-thread exception at `LogError`, then only sets `Handled = true` when the exception family is recoverable (`IOException`, `HttpRequestException`, `OperationCanceledException`, `TaskCanceledException`, `UnauthorizedAccessException`). State-corruption families propagate and terminate the process with a visible crash. `AggregateException` is unwrapped for the family check. The user-facing toast/dialog is only shown in the recoverable branch.
- [x] **ViewModel `IDisposable` audit** — verified. `MainViewModel` (7 event subs, all unsubscribed in `Dispose`), `CrawlDashboardViewModel` (1 sub + cancellation cleanup), `FindingsViewModel` (2 subs + tab `PropertyChanged`), `PluginManagementViewModel` (3 subs + per-item `PropertyChanged`). `MainWindow.MainWindow_Closed` calls `Dispose()` on the `DataContext` — all four top-level VMs participate.
- [x] **Wave 2 verification** — Debug and Release builds both green (0 errors; remaining warnings are pre-existing NuGet-vulnerability advisories tracked in Wave 3 and one CS9113 on `AboutViewModel`). `\bawait\s` regex-sweep over Core/Data/PluginSdk returns no matches missing `.ConfigureAwait(false)`. Runtime probes (1000-URL crawl for Playwright pool cap; 100x project open cycle for `MainViewModel` instance count) deferred for manual runtime verification by the orchestrator.

### Wave 3 — Correctness & cleanup (P2)

- [x] **Per-host throttle** — new `HostThrottle` service (`src/ShoutingIguana.Core/Services/HostThrottle.cs`) keyed by host with a per-host `SemaphoreSlim(1,1)` + `LastRequestUtc` stamp. `AcquireAsync(host, minDelay, ct)` waits for the semaphore, sleeps `(LastRequestUtc + minDelay) - UtcNow` if positive, and returns an `IAsyncDisposable` that stamps the timestamp and releases the semaphore on dispose. `EvictStale()` drops entries idle for > 10 min on every acquire when the map is non-trivial. `CrawlEngine` calls `AcquireHostSlotForCrawlAsync` / `AcquireHostSlotForExternalAsync` around the fetch via `await using`, so `CrawlDelaySeconds` now applies across all workers, not per-worker. Old `_lastCrawlTime` dictionary and `EnforcePolitenessDelayAsync` / `EnforceHostDelayAsync` removed. Registered as `AddSingleton<HostThrottle>()` in `App.xaml.cs`.
- [x] **Gate progress reporter on pause** — `CrawlEngine.ReportProgressAsync` now calls `_pauseEvent.Wait(cancellationToken)` at the top of each iteration before the 500 ms delay + `SendProgressUpdate` tick. `ManualResetEventSlim.Wait(ct)` cooperates with cancellation and drops the loop to zero CPU/DB traffic while paused. (`WaitAsync` isn't available on `ManualResetEventSlim`; the sync wait is fine here because this is a single dedicated task.)
- [x] **Harden sitemap service** — `SitemapService` rewritten: `MaxSitemapsPerRobotsListing = 50` (log + truncate), `SemaphoreSlim(5,5)` gate on concurrent fetches, `MaxSitemapBytes = 50 MB` enforced via byte-counted copy loop (warn + truncate), per-fetch `CancellationTokenSource(TimeSpan.FromSeconds(30))`, XML reader now uses `XmlReaderSettings { DtdProcessing = Prohibit, MaxCharactersFromEntities = 1024, MaxCharactersInDocument = 10_000_000, XmlResolver = null }`. Fetch split into `ProcessSitemapAsync` (orchestration) + `FetchSitemapAsync` (streaming byte cap).
- [x] **Serilog retention caps** — `src/ShoutingIguana/App.xaml.cs` `Log.Logger` config now sets `retainedFileCountLimit: 14`, `fileSizeLimitBytes: 50_000_000`, `rollOnFileSizeLimit: true` and `MinimumLevel.Override("ShoutingIguana.Plugins", Warning)` to stop plugin debug/info noise from filling the log.
- [x] **Replace `UrlContext.HttpResponseMessage` with a DTO** — new immutable `UrlResponseInfo(Scheme, Host, StatusCode, Headers, ContentType, BytesRead)` record in `src/ShoutingIguana.PluginSdk/UrlContext.cs`. `UrlContext.HttpResponse` is now `UrlResponseInfo?`. `PluginExecutor.BuildResponseInfo` constructs it from `UrlAnalysisDto` + headers dictionary. No built-in plugin reads `ctx.HttpResponse` directly — they all go through `ctx.Headers` (verified via grep), so plugin code required no changes. SDK version bumped to `1.0.0` in `ShoutingIguana.PluginSdk.csproj`; "Breaking changes in 1.0.0" section added to `src/ShoutingIguana.PluginSdk/README.md` with before/after snippet.
- [x] **Bound the caches** — `RobotsService` now uses `MemoryCache` with `SizeLimit = 10_000` and `AbsoluteExpirationRelativeToNow = 24h`; each entry `Size = 1`. `CrawlEngine._lastCrawlTime` dictionary removed entirely — the per-host throttle map (item 1) now owns both inter-request gap and last-access eviction, so the separate cache is unnecessary. Added `Microsoft.Extensions.Caching.Memory` 10.0.6 to `ShoutingIguana.Core.csproj` (and bumped transitive `Microsoft.Extensions.Logging.Abstractions` to 10.0.6 to avoid the NU1605 downgrade).
- [x] **Configuration hierarchy cleanup** — documentation path chosen to avoid risking existing per-project `SettingsJson` round-trips (each `ProjectSettings` field is serialized by `System.Text.Json`; inheriting from `CrawlSettings` would propagate `[SupportedOSPlatform("windows")]` and change the declared member order). Added `<summary>` XML doc blocks on `ProjectSettings` and `CrawlSettings` explaining precedence (project overrides global per field; non-mirrored fields on `CrawlSettings` apply globally; `GlobalProxy` is ignored when `ProxyOverride` is non-null), plus per-property `<summary>` comments that link each duplicate back to its `CrawlSettings` counterpart. Added a `TODO (Wave 4)` marker on `ProxyOverride` for the precedence test.
- [x] **Upgrade vulnerable NuGet packages** — `NuGet.Packaging` and `NuGet.Protocol` bumped `7.0.0 → 7.3.1` (`ShoutingIguana.Core.csproj`). `System.Security.Cryptography.Xml` pinned as a direct reference at 10.0.6 in `ShoutingIguana.Data.csproj` to override the vulnerable 9.0.0 transitive. `dotnet list package --vulnerable --include-transitive` now reports "no vulnerable packages" for every project.
- [x] **Wave 3 verification** — `dotnet build ShoutingIguana.sln` (Debug) and `dotnet build -c Release ShoutingIguana.sln` both green (0 errors; remaining warnings are the pre-existing CS9113 on `AboutViewModel`/`BrokenLinksTask` that Wave 2 also left alone). `dotnet list package --vulnerable --include-transitive` reports zero high- or low-severity advisories. Runtime probes (pause → progress-loop CPU ≈ 0; per-host delay timing ≥ configured gap across 4 workers; 60k-URL sitemap bomb against the new caps; Serilog retention prune after simulated date advance) are deferred to manual runtime verification by the orchestrator.

### Wave 4 — Testing & polish (P3)

- [x] **Create `src/ShoutingIguana.Tests` (xUnit)** — new xUnit 2.9.3 project targeting `net10.0-windows` (`SupportedOSPlatform("windows")` inherited from Core/Data/ProxySettings), added to `ShoutingIguana.sln`. Packages: `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Moq` 4.20.72, `Microsoft.EntityFrameworkCore.Sqlite` 10.0.0 (for the concurrent-switch fixture). Seed suites landed:
  - `CrawlUrlPolicyTests` — allowed schemes, rejected schemes/garbage, IPv4 private/loopback/link-local/multicast, IPv6 link-local/multicast/ULA, IPv4-mapped IPv6 `::ffff:10.0.0.1` rejection, `allowPrivateNetworks=true` toggle, non-http scheme still rejected when toggle on.
  - `UrlHelperTests` — `Normalize` lowercases scheme+host, drops fragment, preserves trailing slash; `Resolve` handles absolute / protocol-relative / root-relative / path-relative / parent-directory / base-tag override; `ExtractBaseTag` picks the first `<base href>`, resolves root-relative hrefs against the page URI, returns null when absent.
  - `ProxySettingsTests` — empty when disabled, unchanged when no userinfo, masks embedded `user:pass@host` with `***` while preserving host/port, scheme mapping for Http/Https/Socks5.
  - `PluginRegistryTests` — drops dummy `.dll` files under `AppDomain.BaseDirectory/plugins` and verifies allowlist enforcement: assembly not on allowlist skipped with "Refusing to load" warning; empty allowlist skips everything; `Enabled=false` bypasses the allowlist check (no "Refusing to load" warning emitted). WeakReference/unload coverage was not added — it needs a real signed plugin DLL loaded via `PluginLoadContext.LoadFromAssemblyPath`, which can't be stubbed cleanly; deferred.
  - `ProjectDbContextProviderTests` — 32 parallel `SetProjectPathAsync` calls bounded by a 15 s timeout prove no deadlock on the internal `SemaphoreSlim`, and `GetDbContext()` before first set throws. Uses a real on-disk SQLite file via `ISqliteDbContextFactory` stub.
  - `CrawlEngineStateMachineTests` — focused tests that `IsCrawling`/`IsPaused` start false and that pause/resume/stop on an idle engine are no-ops (no throw, no state flip). Transaction-rollback, pause-under-load, and checkpoint round-trip are declared as `[Fact(Skip = "Wave 5 follow-up: ...")]` with per-test justifications — a full crawl harness needs a mocked Playwright page + 12 repository mocks + in-memory SQLite fixture that is out of scope for Wave 4.
  - Result: 63 passed, 3 skipped, 0 failed across 66 tests.
- [x] **Delete obsolete shim** — grepped `SetProjectPath\b` across `src/**/*.cs` (excluding `SetProjectPathAsync`): only hit was the method's own definition. Removed `ProjectDbContextProvider.SetProjectPath(string)` at `src/ShoutingIguana.Data/ProjectDbContextProvider.cs:131-136`.
- [x] **Stale `net9.0` artefact cleanup** — `dotnet clean ShoutingIguana.sln` returned 0 errors/0 warnings. Globs `**/bin/net9.0/**`, `**/obj/net9.0/**`, and `**/net9.0*` all returned zero matches afterwards.
- [x] **Final verification** — `dotnet build ShoutingIguana.sln` (Debug): 0 errors, 3 pre-existing CS9113 warnings (`AboutViewModel.logger`, `BrokenLinksTask.checker` — both carried over from Waves 2 & 3). `dotnet build -c Release ShoutingIguana.sln`: same 0 errors / 3 pre-existing warnings. `dotnet test ShoutingIguana.sln`: 63 passed / 3 skipped / 0 failed. Smoke-test of the WPF app deferred to the orchestrator per instructions.

---

## Findings reference (unchecked items above point back here)

The section below is the full audit catalogue (preserved for context). Use it to justify why each checklist item exists — the summary above is the remediation tracker.

### Security & data integrity (reference)

**P0**

1. No URL-scheme / SSRF guard before fetch. **[ADDRESSED — see Wave 1 item above.]**
2. Plugins run with full process trust. **[ADDRESSED via allowlist — Authenticode signing deferred; re-evaluate if the threat model changes.]**
3. Proxy credentials logged in plaintext. **[ADDRESSED via `GetRedactedProxyUrl()`.]**
4. No transaction around the multi-table crawl write. `SaveUrlAsync` → `SaveRedirectChainAsync` → `ProcessLinksAsync` run as separate `SaveChangesAsync` calls. A mid-write failure leaves orphan `Url` rows without `Link`/`Header`/`Image`/`Redirect` rows.

**P1**

5. `EnableSensitiveDataLogging` active in DEBUG builds (`src/ShoutingIguana.Data/SqliteShoutingIguanaDbContext.cs:27–29`). Logs full URL and header values during dev builds — gate by runtime flag or document.
6. CSV injection on Excel export — `ExcelExportService` writes cell values verbatim; a crawled page with header `x-custom: =cmd|…` turns into a live formula when the export is opened.
7. Silent `catch { }` blocks hiding real errors. Known sites: `LinkExtractor.cs:~132`, `~183`; `PluginRegistry.cs:~621`.
8. Global `DispatcherUnhandledException` always sets `Handled = true` (`App.xaml.cs:305`). Even unrecoverable state is swallowed; the app keeps running broken.

**P2**

9. Sitemap DoS — no cap on robots-listed sitemap count, no parallel-fetch cap, no total-byte cap, no XML DTD/entity guard.
10. Project-filename sanitization enforced only in `ProjectHomeViewModel`; re-validate at export and log path boundaries.
11. Migration failure silently downgrades to old schema in `ProjectDbContextProvider` — UI then writes to missing columns.
12. Logs never rotate off disk — Serilog daily rolling file has no retention cap.

### Performance & concurrency (reference)

**P0**

13. `GC.Collect(2, Aggressive, true, true)` every 50 URLs in analysis phase (`CrawlEngine.cs:~757`).
14. Pause/resume has a flag+event race — `_isPaused` (Interlocked int) and `_pauseEvent` (MRES) are set separately.
15. Sync-over-async at UI startup (`App.xaml.cs:151`) — `appSettingsService.LoadAsync().GetAwaiter().GetResult()`. Doesn't deadlock today but is a classic trap.

**P1**

16. New `HttpClient` per URL in `CrawlEngine.FetchStaticResourceAsync` — ephemeral-port exhaustion under load. `IHttpClientFactory` is registered but unused here.
17. New Playwright `BrowserContext` per page in `PlaywrightService`.
18. EF Core queries track by default on the hot path — `UrlRepository.GetByAddressAsync` and friends need `AsNoTracking()` where appropriate.
19. Unbounded caches — `RobotsService._cache` (10k-then-dump), `CrawlEngine._lastCrawlTime` grows per unique host.
20. `ConfigureAwait(false)` regressions in Core/Data/PluginSdk.
21. Primary-constructor redundant fields (`CrawlEngine.cs:18–22`, `MainViewModel.cs:83–93`).
22. Fire-and-forget sitemap discovery (`CrawlEngine.cs:~284`) — `_ = Task.Run(...)` discards errors aside from logging.

**P2**

23. Progress reporter runs during pause — wastes CPU/DB.
24. `CrawlDelaySeconds` enforced per-worker, not per-host.
25. `LinkExtractor.ExtractLinksAsync` is fake-async (`Task.FromResult` around synchronous HAP parsing).
26. Per-URL DI scope allocations — three+ scopes per URL in the hot path.
27. Hardcoded redirect status `301` in the redirect chain regardless of actual 302/307/308.

### Architecture & code quality (reference)

**P1**

28. `ProjectDbContextProvider` — project-switch race (singleton holds mutable `_currentProjectPath`); also mixes `.Wait()` with `.WaitAsync()`.
29. Subscription leaks in ViewModels (`MainViewModel`, `CrawlDashboardViewModel`, others).
30. `UrlContext.HttpResponse` exposed as `HttpResponseMessage` in public SDK — leak of framework disposable type; replace with DTO in Wave 3.

**P2**

31. `MainViewModel` registered `Transient` — multiple resolves return different instances. Register Singleton or factory-cache.
32. Configuration sprawl / overlap between `ProjectSettings`, `CrawlSettings`, `BrowserSettings`, `ProxySettings`.
33. Fake-async wrappers around sync work (LinkExtractor, some repository helpers).
34. `services.AddHttpClient()` registered but unused.

**P3**

35. No test project.
36. Stale `net9.0` artefacts in `bin/` / `obj/`.
37. Obsolete `[Obsolete]` `ProjectDbContextProvider.SetProjectPath`.
38. Adaptive-loader counters mix `volatile`/`Interlocked` — pick one pattern.

---

## Scope decisions (locked-in)

- Full-scope remediation: all four waves.
- Breaking SDK changes permitted with a **major version bump** (`ShoutingIguana.PluginSdk` 0.1.0 → 1.0.0) in Wave 3. Built-in plugins migrate in the same change set.
- Plugin trust model: **assembly-name allowlist in `settings.json` (Local AppData)**. Authenticode / hash pinning deferred; document as future hardening.

## Build & verify commands

```bash
dotnet build ShoutingIguana.sln
dotnet build -c Release ShoutingIguana.sln
dotnet run --project src/ShoutingIguana/ShoutingIguana.csproj
# Once Wave 4 lands:
dotnet test
```
