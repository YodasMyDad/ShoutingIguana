# Brief: review the plain words for the 87 checks (read-only)

Shouting Iguana v2 is a desktop SEO crawler with 87 checks. Its new Simple mode, for people who are not SEOs, shows
every check in plain words: a title, what is wrong and why it matters, how to fix it, who usually fixes it, and a
good-news line when the check passes. A writer has just written all of them. You didn't write them. Review them
before they go into the product, where a wrong or unclear sentence is read by someone who can't tell it is wrong.

You change nothing in the repo and nothing in the writer's files.

## What to read

- The texts: `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\plain-words\plain-words.json`
  (87 checks, 17 categories, 15 skip reasons), the writer's `notes.md` beside it (where it chose simple over
  exact, and the rules it bent) and its `check.py`.
- The rules they were written to, in the repo (read-only: `C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3`):
  `docs/v2-ui-simple.md`, sections "Simple's words", "Plain words" (the template, the rules, "Ten checks, written
  out": those ten are the reference and are not under review, except to say if one is wrong), "What to fix", "One
  problem", "The Summary" (its "What's fine" and "Couldn't be checked"), "The report". The glossary in
  `docs/v2-ui-discovery.md`.
- The truth each text must match: `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` and each check's
  rule in `engine/src/ShoutingIguana.Engine/Analysis/Rules/`; the skip reasons in the analysis code
  (`SkippedCheckReasons.cs`).
- Where the texts appear: the mockups `app/design/simple/problem.html`, `fix.html`, `summary.html`, `report.html`.

## What to look for, check by check

1. **True?** Read the rule, not just the catalog's words. Does the plain text claim more than the rule finds, or
   less? Does it say "page" for a URL that isn't one? Does the title fit every case the rule flags? Is the "why it
   matters" a real consequence, stated no stronger than it is (no promise of rankings, no threat that isn't
   real)? Is the fix actually the fix? Is `fixer` right? Is `plainPassed` true for a site where the check had
   nothing to look at? Is `plainOneFix` only on checks where one template fault is common, and true when shown?
2. **Clear to a shop owner?** Read each as someone who has never heard of a canonical, a redirect or structured
   data. In five seconds: what is wrong, should I care, what do I do or who do I send it to? Mark every word they
   would have to look up, every sentence they would read twice, every step they couldn't follow or hand over.
3. **Usable by the developer who receives the report?** Exact enough to act on without the app?
4. **To the template and rules?** Lengths, the sentence pattern, UK English, banned words, how a term is
   introduced, one name for one thing across all 87 (address, page, title, search description, and so on).
5. **Notices.** A Notice is "worth a look" and often fine as it is: does each say when it's fine and when it isn't,
   without alarming?
6. **The set as a whole.** Read the 87 titles as one list, then the 87 good-news lines as one list, as the app
   shows them: duplicates, near-identical titles a reader couldn't tell apart, inconsistent grammar, tone.
7. **Categories and skip reasons**: each plain title and sentence true and clear; each skip reason's action
   something the reader can do.

Also judge three things the writer raised, and recommend:

- Ten texts carry a number that is a setting's default (60 characters, 200 KB, 1 second...). How do the technical
  texts handle the same limits, and can a plain text be wrong when a limit is changed? Say what the engine should
  do (fill the number in, or something else).
- The skipped and passed lists show a check by its problem title, and twelve titles are sentences ("No sitemap
  found"). Look at how those lists read in the mockups with these titles. Is a further short field needed (what
  the check looks for), or do the titles and good-news lines serve?
- `security-headers-missing` is reported once for the whole site but counted as a page: how will "1 of N pages"
  read under its text, and are there other checks reported once for the site that the design's `siteWide` list
  misses?

## Report back

Every check that needs a change, most serious first (untrue, then misleading, then unclear, then rule breaks):
the check id, the field, the present text, what is wrong in one line, and **your replacement text**, written to
the template so it can be dropped in. Don't list checks that are fine; say how many are. Then the three
recommendations, a short note on the set as a whole, and any technical text in the catalog you found to be wrong
(the writer listed some: confirm or correct each).

Write your replacements as data the writer can apply, `review.json` in
`C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\plain-words-review\`
(`[{ "id", "field", "problem", "kind": "untrue|misleading|unclear|rule", "replacement" }]`), and give the same
list in your final message, which is all the orchestrator sees. Keep a `checkpoint.md` there as you go.
