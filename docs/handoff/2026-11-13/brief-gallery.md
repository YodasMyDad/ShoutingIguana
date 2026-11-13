# Brief: re-shoot the owner's screenshot gallery of the finished step 3

Shouting Iguana v2 is a desktop SEO crawler (a .NET engine, a React + TypeScript app in a Tauri 2 shell). The owner
follows the UI through a private gallery page of screenshots. The last one (36 views, 10 November) shows Advanced
only, before most of step 3's screens existed. You take a new set of shots of the finished app, in both modes, in the
real shell, and build the gallery page from them. The orchestrator publishes it.

## Where you work

- Branch `ui/step-3` at the head named in the message that launched you. Make your own detached worktree: from
  `C:\Projects\ShoutingIguana`, `git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\gallery-shoot <head>`.
  Edit nothing in any other worktree; commit nothing.
- Your folder: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\gallery\`
  (has `tmp`; keep `checkpoint.md` there).
- The last gallery's builder, to copy and adapt (read-only): `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad\gallery\build.cjs`
  (and `preview.cjs`). The published site is built into `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\`
  (page `shouting-iguana-screens.html` and `shots/`): build the new one INTO THAT SAME FOLDER, replacing its
  contents, so the page keeps its address. Keep the old page's look and structure (sections, a caption per shot,
  light and dark side by side or switchable, the facts line), as it was designed and approved.
- How the shell is driven: `npm run smoke:shell` and `docs/v2-ui-architecture.md` (the real-shell section): the
  release build with its own data folder, driven over WebView2's debugging port. Ports
  `SI_DEV_SERVER_PORT=49874 SI_DEV_SIDECAR_PORT=49875 SI_PREVIEW_PORT=49876 SI_E2E_SIDECAR_PORT=49877`.
- Data: copies of the garden crawl (`%LOCALAPPDATA%\ShoutingIguana\dev-crawls\www.gardenfurnituresets.co.uk-20261031.sicrawl`,
  copy it, never open the original) analysed by the current engine (it scores 85, Good), and live crawls of the
  engine's synthetic test site only. Never crawl a real site.
- Stop only processes you started, by their own process id or your own ports; never close a Playwright MCP tab that
  isn't yours. Another agent may be auditing the app at the same time: one heavy thing at a time.

## What to shoot

At 1280x720, in light and dark, in the real release shell. Every screen a user meets, in the order they meet it:

- **Simple**: first start; Home with the library; New crawl (Simple's four questions); checking a site (live,
  paused); the Summary (garden: 85, Good) and its score breakdown; What to fix; one problem (with a One fix
  callout if the garden crawl has one); the Report screen; Settings (Simple's four sections); the finished toast.
- **Advanced**: Home; New crawl (with a section open); Overview live and done (with the score); Issues; URLs (a
  preset and the filter bar, the row menu open); the inspector; Links; Sitemaps; robots.txt; the Report screen;
  Settings (General, Analysis); an export's progress toast; the palette with a search; the shortcut sheet; the crawl's
  settings.
- **The switch**: the same crawl in both modes side by side; the top bar at 200% zoom.

About 30 views, each in both themes (60 to 70 images). Fewer and sharp beats many: a shot shows a screen with real
data in a state worth seeing. Name each file `<nn>-<view>-<light|dark>.png`.

## Build

Adapt `build.cjs` to the new sections and captions (UK English, the app's own words, no engine ids); its facts line
says the date, the shell, the page size, the data and the branch head. Write the page and `shots/` into
`_gallery\site\`. Run the builder's preview step and look at the page at desktop and phone widths in both themes
(the page follows the OS theme). Keep the whole site under 15 MB (compress the PNGs losslessly if you need to).

## Report back

Short: the list of views with their files; the page's path and size; anything that looked wrong in the app while
you shot it (with the shot), as a list the orchestrator can act on; what you could not shoot and why.
