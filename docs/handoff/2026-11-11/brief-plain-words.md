# Brief: the plain words for every check (writing, no code)

Shouting Iguana v2 is a desktop SEO crawler with 86 checks (87 with one being added). Its texts were written for
technical SEOs. The app is gaining a Simple mode for people who aren't: a shop owner who has never heard of a
canonical, a marketing manager who must tell a developer what to fix. In Simple, and in the report the app makes,
every check is shown in plain words. The design is done, ten checks are written out as the reference, and you write
the rest. The owner's words for the goal: "super easy for anyone to understand".

This is writing. You change no code and nothing in the repo: you produce one JSON file, which an engine agent will
load into the catalog.

## Read first (repo, read-only: `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3`)

- `docs/v2-ui-simple.md`: "Simple's words", **"Plain words" in full (the template, the rules, and "Ten checks,
  written out")**, "What to fix" and "One problem" (where the texts appear), "The report" (where they are printed),
  "What the three readers got stuck on", and in "What the engine needs" the rows for plain texts, plain category
  titles and plain skip reasons.
- `docs/v2-ui-discovery.md`: the glossary (Simple's word for each technical term).
- Every check's technical texts: `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` (title, description,
  `why`, how to fix, severity, category, `subject`), and, wherever a text alone doesn't tell you exactly what a
  check flags, its rule in `engine/src/ShoutingIguana.Engine/Analysis/Rules/`. The plain text must be true of what
  the rule does, not of what its title suggests.
- The design's drafts: `app/design/simple/build/lib/words.mjs` (draft titles and good-news lines for all 86, and
  nine garden checks drafted in full). Drafts, not references: improve them.
- The mockups, to see the texts in place: `app/design/simple/problem.html`, `fix.html`, `report.html`.
- The check being added, `site-hidden-from-search`, is described in the design document's engine table: Error,
  site-wide, reported once: more than half of the pages read can't appear in search results because the site says
  so (noindex, or a canonical to another URL).
- The skip reasons: find every reason a check can be skipped for (the reason codes and their technical sentences)
  in the engine's analysis code, and the 17 categories in the catalog.

## What to write

For each of the 87 checks, the fields the design's template defines, to its rules exactly (lengths, sentence
pattern, reading level, the banned words, how a technical term is introduced, UK English): `plainTitle`,
`plainWhat` (the sentences), `plainFix` (the steps), `fixer`, `plainPassed`, and `plainOneFix` where a fault is
commonly one template's. The ten checks the design document writes out are taken from it word for word.

Also: `plainTitle` for each of the 17 categories, and `plainText` and `plainAction` for each skip reason.

What makes a text good here:

- **True.** It says what the check found, no more. If the check flags a URL that isn't a page, the text doesn't
  say "page". If a notice is usually fine, the text says so and says when it isn't. If the fix is someone else's
  (the host, the developer, the CMS vendor), it says who.
- **Something the reader can do.** "How to fix it" is steps a person can follow or hand to a developer, never
  "investigate" or "review your templates". Where the fix differs by platform, say the common case and what to
  ask for.
- **No word the reader has to look up**, unless the design's rules allow it with its gloss.
- **`plainPassed` stays true when the check had nothing to look at** (a site with no sitemap passes "sitemap can't
  be read": the line must not claim a sitemap was read).
- **Consistent.** The same thing has the same name in all 87: one word for a URL, one for a title, one for a
  description, as the glossary has them.

## How to work

1. Read the template, the rules and the ten references until you could recite the pattern. List the rules as a
   checklist in your scratch folder.
2. Write the checks category by category. For each check, read its catalog entry and, where needed, its rule.
3. **Check your own work mechanically**: write a small script (your scratch folder) that loads the JSON and fails
   on any rule that can be tested: a missing field, sentence 1 over the length limit, a banned word, a step count
   out of range, an id that isn't in the catalog, a catalog id that is missing.
4. **Then read all 87 again as the shop owner**, in the order the app would show them. For each, ask: do I know
   what is wrong, do I know whether to care, do I know what to do next or who to send it to? Rewrite what fails.
   Then read them as the developer who receives the report: is each still exact enough to act on?
5. Keep a list of every check where the plain text had to choose between simple and exact, and what you chose, and
   of any check whose technical text you believe is wrong or misleading (don't change it: report it).

## Deliver

- `plain-words.json` in your scratch folder:
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\plain-words\`
  with `{ "checks": { "<check id>": { "plainTitle", "plainWhat": [..], "plainFix": [..], "fixer", "plainPassed",
  "plainOneFix"? } }, "categories": { "<category id>": { "plainTitle" } }, "skipReasons": { "<reason code>":
  { "plainText", "plainAction" } } }`. Use the field shapes the design document gives; where it is silent, choose
  and say.
- Your checking script and `checkpoint.md` beside it (update it after each category: a pause must lose nothing).

## Report back

Short: the file's path; the counts (checks, categories, skip reasons); the script's result; the choices list
(simple against exact), the checks whose technical texts you think are wrong, and anything in the design's rules
that didn't survive contact with 87 checks, with what you did instead. Five of your texts you are least sure of,
quoted. No file dumps.
