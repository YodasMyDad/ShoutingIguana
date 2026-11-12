# Brief: how our SEO crawler compares with Screaming Frog and Sitebulb (research only, no code)

We are rebuilding a desktop SEO crawler and site auditor (a "v2": a .NET engine with 86 checks, and a React desktop
UI). Its owner asked for "a quick comparison with Screaming Frog": **what we're missing compared to them, and
whether our UI is as good or better.** Sitebulb goes beside it for one thing: how it explains issues to people who
aren't technical SEOs, because our app is about to gain a Simple mode for exactly those people (a summary, a score,
what to fix first in plain words, and a report), with an Advanced switch for technical SEOs.

You write one document. No code, and no changes to the repo.

- Repo, read-only: `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3`.
- Your folder: `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\comparison\`.
  Write the document there as `v2-comparison.md`. Keep notes in `checkpoint.md` there as you go.

## What we have (read these first)

- `docs/v2-rebuild.md`: "Plan" (steps 4 and 5 are the known gaps), "Simple first, Advanced on a switch", "Where
  step 3 stands" (what the UI has), "Known limitations".
- `docs/v2-ui-ia.md` (the screens), `docs/v2-ui-brief.md` (the bar), `docs/v2-ui-discovery.md` (the parity list, the
  glossary, and "Backlog: checks from the old app").
- The 86 checks: `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` (id, title, severity, category).
- What the screens look like: the screenshots in `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\`
  (look at a dozen: Home, Overview live and finished, URLs, the inspector, Issues).

## What to read about them

Their own sites, documentation, tutorials, release notes and screenshots only: neither tool is run (there is no
licence, and the owner asked for no more than this). Screaming Frog SEO Spider: the user guide, the issues library,
the feature and pricing pages, the configuration guide, release notes for the last two or three major versions.
Sitebulb: the features pages, the "hints" documentation (how each issue is explained and prioritised), the audit
score, the reports. If a page can't be fetched, use another of their pages or a search result's summary, and say
what you couldn't read. Do not put our product's name, the owner's name or anything about this project into a
search or a request: search for their products only.

## The document

`v2-comparison.md`, written for the owner (an expert, wants it terse) and for the agents who will design Simple
mode. UK English, plain words, short sentences, no marketing tone. Start with the answer in five or six lines: where
we are behind, where we are level, where we are ahead, and the three things most worth doing about it.

Then three lists, each a table. Every gap gets a verdict from this set: **step 3** (the UI, in progress),
**the check audit** (step 3b: checks we should add or fix), **step 4** (JavaScript rendering, include and exclude
rules, logins, list mode, custom extraction), **step 5** (Search Console, analytics and other integrations),
**later**, or **not doing** (with the reason).

1. **Features.** What Screaming Frog does that we don't, what we both do, what we do that it doesn't. Cover at
   least: crawl modes (spider, list, sitemap, SERP), JavaScript rendering, robots.txt handling and testing, URL
   rewriting, include and exclude, crawl limits and speed, authentication, custom extraction and custom search,
   custom JavaScript, structured data validation, hreflang, canonicals, pagination, AMP, duplicate and
   near-duplicate content, spelling and grammar, link score, crawl depth, orphan pages, redirect chains and audits
   for a migration, XML sitemap generation, visualisations (crawl tree, force-directed), crawl comparison,
   scheduling, command line, storage mode and scale (database against memory), exports and reports, the
   integrations (Google Analytics, Search Console, PageSpeed Insights, Ahrefs, Majestic, Moz, AI providers), log
   file analysis (a separate product), accessibility checks, segments, Looker Studio, and price and licence.
2. **Checks.** Screaming Frog's issue list (its "Issues" tab and the filters under each tab) against our 86. Give a
   row for each of theirs we lack, grouped by area, with our nearest check if there is one. Then ours they lack. Be
   exact: use their names. Say how many of theirs we cover.
3. **UI.** Judge honestly, screen for screen, from their screenshots and ours: how much one screen holds, how a
   user gets from "something is wrong" to "these URLs", what a first-time user sees, how issues are explained and
   prioritised, filters and search, the detail pane for one URL, live crawl feedback, exports, theming,
   accessibility, keyboard. Where theirs is better, say so and say what to take. One column for Screaming Frog, one
   for Sitebulb where it differs, one for us, one for the verdict.

Then a short section, **For Simple mode**: what Sitebulb's hints, scores, prioritisation and reports do well and
badly for a non-expert, with examples quoted briefly from their documentation, and concrete things our Simple mode
should take or avoid: how a score is built and explained, how many things it shows at once, the words it uses, what
the report holds.

End with sources: the pages you read, as a list of links, and anything you couldn't check.

Aim for something the owner can read in ten minutes: about 250 to 400 lines. Facts over adjectives. Where you
inferred something from a screenshot or aren't sure, say so. State a fact about their product only if you read it
on one of their pages; don't rely on memory for version-specific claims.

## Report back

The path of the document, the five-line answer, the count of their checks we cover and lack, anything you couldn't
read. Nothing else.
