# Shouting Iguana

A desktop app that crawls a website the way a search engine does, and tells you what to fix in plain words.

**2.0.0-beta.1.** Unsigned beta for Windows, macOS and Linux.

**[Download 2.0.0-beta.1](https://github.com/YodasMyDad/ShoutingIguana/releases/tag/app-v2.0.0-beta.1)**

## What it is

Paste an address. Shouting Iguana reads the site, gives it one score, and lists the problems worth fixing first. Each problem says what is wrong, why it matters, and how to fix it. The report at the end is that same check as a page you can save or print.

It opens in Simple. Advanced is one switch away: every URL, the links, the sitemaps, robots.txt, and an inspector for a single address.

A crawl is one file on this computer (`.sicrawl`). The app keeps the list of sites you have checked.

It reads the HTML the server sends. A page that is built in the browser, after JavaScript runs, is missing from the check.

## Screenshots

The beta, light and dark.

### Home

Paste an address, or open a site you have already checked. The score and what is left to fix are on the row.

| Light | Dark |
| --- | --- |
| ![Simple Home, light. Paste a site address, with sites you have already checked underneath.](docs/readme/02-simple-home-light.png) | ![Simple Home, dark.](docs/readme/02-simple-home-dark.png) |

### Where to fix it

Serious problems first, then the ones worth fixing, then the ones worth a look. One line says how many pages, and what it does to the score.

| Light | Dark |
| --- | --- |
| ![Where to fix it, light. Serious problems, then worth fixing, then worth a look.](docs/readme/14-simple-what-to-fix-light.png) | ![Where to fix it, dark.](docs/readme/14-simple-what-to-fix-dark.png) |

### A problem, and how to fix it

Open one problem and the fix is words: what to change, and where. The same link in the header of every page is one fix, not hundreds.

| Light | Dark |
| --- | --- |
| ![One problem with its fix, light. Links with nothing to read, where they are, and how to fix them.](docs/readme/15-simple-problem-one-fix-light.png) | ![One problem with its fix, dark.](docs/readme/15-simple-problem-one-fix-dark.png) |

### The URL inspector

Advanced. Pick an address and see how it answered, its title and headings, what it links to, and which checks fired.

| Light | Dark |
| --- | --- |
| ![URL inspector, light. The URL list with one page open beside it.](docs/readme/53-advanced-inspector-light.png) | ![URL inspector, dark.](docs/readme/53-advanced-inspector-dark.png) |

### The report

The check as a page: the score, what to fix first, and how the score was worked out. Save it, or print it.

| Light | Dark |
| --- | --- |
| ![The report, light. Score 84, and the problems to fix first.](docs/readme/20-simple-report-light.png) | ![The report, dark.](docs/readme/20-simple-report-dark.png) |

## Downloads

The installers are on the GitHub release [app-v2.0.0-beta.1](https://github.com/YodasMyDad/ShoutingIguana/releases/tag/app-v2.0.0-beta.1).

| System | File |
| --- | --- |
| Windows | NSIS installer, 64-bit, for the current user |
| macOS | Disk image for Apple silicon (arm64), and one for Intel (x64) |
| Linux | `.deb` and AppImage, 64-bit |

Each installer is attached with a SHA256 checksum (`SHA256SUMS-*.txt` on the release). Check the sum before you run it.

## Opening an unsigned build

This beta is not signed. Windows and macOS will say so the first time you open it.

### Windows (SmartScreen)

1. If the installer is blocked because it came from the internet: right-click it, **Properties**, tick **Unblock**, then **OK**.
2. Run it. If SmartScreen says Windows protected your PC, choose **More info**, then **Run anyway**.
3. The app itself can get the same prompt the first time it starts. **More info**, then **Run anyway**.

Some managed PCs refuse an unsigned installer. That is the organisation's policy.

### macOS (Gatekeeper)

1. Open the disk image. Apple silicon (M1 and later) uses the arm64 image. An Intel Mac uses the x64 image.
2. Drag **Shouting Iguana** to **Applications**.
3. The first open is blocked, because Apple cannot check the developer. Control-click the app, choose **Open**, then **Open** again.
4. The same choice is in **System Settings → Privacy & Security**, as **Open Anyway**, next to the block message.

### Linux

The `.deb` and the AppImage are unsigned. Install the `.deb` with your package manager, or mark the AppImage executable and run it. Both include the libraries the crawler needs to start.

## A first crawl

1. Open the app. It starts in Simple.
2. Paste the site address and choose **Check site**. The default pace is polite. A small site takes a few minutes.
3. When it finishes, open the site. **What to fix** puts serious problems first.
4. Open one problem. The text says what to change and where. **Recheck these pages** reads those addresses again.
5. **Report** is the check as a page you can save or print.
6. **Advanced**, at the top right, is the URL list. Select an address to open the inspector.

## What it checks

The same checks run in Simple and in Advanced. Each one explains what to change, and where, in words.

- Pages and files that error, time out, or refuse the crawler, and files sent as the wrong type
- Redirect chains, and redirects that do not land where they should
- Broken links, on the site and to other sites
- Pages a search engine is told not to index, and pages nothing links to
- Canonical addresses that disagree, or that point at a page which does not work
- Sitemaps: pages missing from them, and addresses in them that cannot appear in search
- Titles and meta descriptions that are missing, shared, or the wrong length
- Headings that are missing, duplicated, or out of order
- Thin pages, and pages that repeat each other
- Images that are broken, too large, missing a description, still in an older format, or missing a width and height
- Language and region tags (hreflang) that do not line up
- Structured data that is missing or invalid
- HTTPS, mixed content, insecure forms, and weak security headers
- Addresses that are too long, duplicated, or easy to confuse
- Slow responses and heavy pages
- HTML problems that change how the page is read (language, character set, and broken markup)

The app flags the problem and explains the fix. It does not edit the site.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 24](https://nodejs.org/), and Rust (rustup; on Windows, the MSVC toolchain). The app's own notes are in [`app/README.md`](app/README.md). The engine's are in [`engine/README.md`](engine/README.md).

The engine, from the repo root:

```bash
dotnet test engine/ShoutingIguana.Engine.slnx
dotnet run --project engine/src/ShoutingIguana.Cli -- crawl https://example.com --db example.sicrawl
```

The desktop app:

```bash
cd app
npm ci
npm run build:sidecar
npm run tauri dev      # the app, with the engine started for it
npm run tauri build    # a release build and the installer for this OS
```

## Plugins

The `ShoutingIguana.PluginSdk` NuGet package is no longer used. v2 has no plugin system. The package stays published.

## Licence

[MIT](LICENSE).
