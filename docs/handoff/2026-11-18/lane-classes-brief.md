# Lane classes: every class the app writes must compile (2026-11-18)

Read `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/lanes-common.md`
and follow it, with these exceptions:
- **No GO-BUILD hold.** Every other lane has finished, and the machine is yours.
- **Base:** `ui/step-3` at 5e327f35. Create the worktree with
  `git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-classes C:/Projects/ShoutingIguana/.claude/worktrees/3b-classes 5e327f35`.
- **Ports:** 51220 for the app and 51226 for Storybook.
- **Logs and checkpoint:** `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/classes/`.
- **Fixtures:** if Storybook needs `app/design/data/fixtures/`, copy it from the ui-step-3 worktree.

## The problem
The design system's Tailwind theme is closed (`app/src/styles/tokens.css`: `--spacing-*: initial` and its siblings), so
a utility off the scale compiles to **nothing**, silently. `app/src/styles/tokens.test.ts` proves that for samples, but
nothing checks the classes the components actually use.

A scan of the static `className` literals in `app/src/**/*.tsx` found 18 classes with no CSS:
- **Tailwind no-ops:**
  - `max-w-72`: `screens/rechecks/RecheckControl.tsx`, twice;
  - `basis-80`: `simple/RecheckCard.tsx`, `SummaryScreen.tsx`, `WhatToFixScreen.tsx`;
  - `basis-64`: `simple/SummaryScreen.tsx`;
  - `text-text-link`: `screens/links/LinkGroupsGrid.tsx`;
  - `pl-11`: `ui/Fold.tsx`;
  - `pl-5.5`: `screens/settings/about/Licences.tsx`;
  - `font-normal`: `simple/home/SimpleHome.tsx`, `ui/ScoreRing.tsx` (the weights are regular, medium and semibold);
  - `text-24`: `test/simpleScreens.filled.tsx`.
- **`si-*` names with no CSS:**
  - `si-switcher-menu__name`, `si-switcher-menu__state`;
  - `si-row-evidence`, `si-where-row`, `si-where-line__pending`;
  - `si-found-on`, `si-target-words`;
  - `si-evidence-lines`, `si-file-name`, `si-cause-facts`.
  Some are test or e2e hooks; some are dead.

The scan itself is at
`C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/classcheck/check.mjs`.
**Gotcha:** in the Bash tool, `\\` in a heredoc collapses to one backslash; the scan uses `String.fromCharCode(92)`. A
file written with the Write tool is fine.

## What to build
1. **A guard test, seen failing first on today's classes.** It sits beside `tokens.test.ts`, in the node environment,
   and compiles with `@tailwindcss/node` the same way.
   - Every static class in the app's `.tsx` must compile to CSS. Include stories and test helpers, since those
     render screenshots.
   - Cover `className` literals. Also cover the string arguments to `cn(...)`, `clsx(...)` or whatever helper the app
     uses, where that's reasonable. Say what the test can't see (interpolated template parts).
   - An `si-*` name with no CSS passes only if a test, an e2e spec or a CSS selector refers to it. Remove dead ones.
   - The failure message names the class and the file.
2. **Fix each no-op as intended, never with arbitrary values.** Find what the author meant: the nearest scale step, or a
   named token in `tokens.css` beside its siblings, named for its purpose. A link colour comes from the link token.
3. **The Advanced check head** (`screens/issues/CheckDetail.tsx` with `RecheckControl`). Today the plan line ("Fetches
   9 URLs again, and up to 36 with the pages linking to them…") runs wide and pushes Copy URLs and Hide onto a second
   row. See `scratchpad/fixloop/shots/u2/after/screens-issues--garden-light.png`.
   - Make it one action row: Recheck with its menu, Copy URLs, Hide.
   - Put the plan line, or the refusal line, beneath the row, right-aligned, at a named measure.
   - "Check the site again", and its line, follow the same pattern.
   - Keep the test hooks the existing tests use.
4. **Look at each visibly changed story before and after,** in light and dark. Take shots with `scripts/check-stories.mjs --shots`
   or the way the fix-loop lane did; see `scratchpad/fixloop/checkpoint.md`. Check that a flex basis, once it works,
   doesn't squeeze or wrap anything badly at 1440 px and at 960 px. Write `shots/index.md`.

## Verify
- typecheck, lint, format check, and the whole Vitest run (`C:/` uppercase cwd);
- the stories for every component you touched, axe clean in both themes, in forced colours and reduced motion;
- the e2e specs for Issues, the fix loop and Simple's Summary, What to fix and problem screens.
The engine isn't touched. Commit small and logical, with the guard test first.

## Ready note
When done, write `scratchpad/classes/ready.md`: the commit to merge, what changed per class, the shots folder, the
test counts, and anything you couldn't settle. Then report under 400 words.
