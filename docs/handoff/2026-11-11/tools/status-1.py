# Updates "Where step 3 stands" for 2026-11-11, mid-morning: the Simple design merged, round 3's engine work begun.
import io

path = r'C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3\docs\v2-rebuild.md'
text = io.open(path, encoding='utf-8', newline='').read()
newline = '\r\n' if '\r\n' in text else '\n'
text = text.replace('\r\n', '\n')

start = text.index('**Resumed on 2026-11-11.**')
end = text.index('It had been paused in round 2 on the evening of 2026-11-10, to preserve usage.')
resumed = '''**Resumed on 2026-11-11.** The owner said: "Ok finish step 3". The work goes through "Left, in this order" below to
its end, without reports in between. Pausing is theirs to call. Merging to `master` or releasing still needs an
explicit yes. The checkpoint a new session resumes from, and every brief, are in the 2026-11-11 scratchpad folder
that the owner's memory file names (`checkpoint.md`, `brief-*.md`).

Done that day so far, and on this branch:

- [The comparison](v2-comparison.md) (item 3).
- **The Simple design (item 4)**: [`v2-ui-simple.md`](v2-ui-simple.md), with small edits to the IA, the design doc
  and the glossary, and 18 mockups on the garden crawl in `app/design/simple/` (merged `ui/simple-design`). It ends
  in three lists the build rounds are briefed from: what round 3 builds, what round 4 builds, what the engine
  needs. The owner's gallery of the mockups is published, private: https://claude.ai/artifact/R53PTE7wZb7rPZHX6Z3GfN
  (built by `gallery-simple\\build.cjs` in that scratchpad, published from
  `C:\\Projects\\ShoutingIguana\\.claude\\worktrees\\_gallery\\simple\\`: republish from that path to keep the URL).
  Decided there, and the owner's to overturn: notices cost the score nothing; a fault of the whole site costs 60
  points; the switch is at the right of the top bar. The design adds one check, `site-hidden-from-search`, so the
  catalog becomes 87.
- **The two reviews of round 2**: Links has 14 findings (1 high) and Sitemaps and robots.txt has 17 (2 high). Each
  list is saved as `<name>-review\\findings.md` in the scratchpad.

Running, each on its own local branch, unmerged (if this is the newest news here, read the checkpoint, then each
branch's log, and relaunch what hadn't reported):

| Work | Branch and worktree | Brief |
| --- | --- | --- |
| E3c fixer: the review's 7 findings, and two counts for each sitemap (`GET /sitemaps`) | `ui/e3c`, `e3c` | `brief-e3c-fixes.md` |
| Links fixer: 14 findings. A page check's share is over `pagesAnalyzed` | `ui/links`, `links` | `brief-links-fixes.md` |
| Sitemaps and robots.txt fixer: 17 findings. "Crawled" means requested, and URLs robots.txt refused get their own column | `ui/sitemaps-robots`, `sitemaps-robots` | `brief-sitemaps-fixes.md` |
| Round 3, engine: the score, `siteWide`, the new check, the plain-text fields, a code's name in details | `ui/score`, `score` | `brief-score.md` |
| Round 3, engine: export (E5), the workbook and the report | `ui/e5`, `e5` | `brief-e5.md` |

The order they merge in: E3c; Links (its fixer merges `ui/step-3` in first); Sitemaps (the same); the score; export
(it merges the score in before it builds the report's last part). Each merge gets the full checks, run again.

Queued behind the Links and Sitemaps merges, because they touch the shell both of those change:

- **The URL grid to the IA**: Ctrl+F, header sort, a row menu for every grid, the filter bar with Add, edit and
  Clear, facets, the column chooser and presets, "Show full URLs" and a whole-view copy. The Links review listed
  what URLs lacks, and the comparison names this as where Screaming Frog is plainly better for an expert.
- **The UI takes up E3c's fields**, with the shell pass's open findings (item 2).
- **Round 3's UI**: the mode setting and the switch, the top bar's order of giving way, Settings, New crawl in both
  modes, and export on every grid.
- **Round 3's second engine piece (E6 and the gaps round 2 found)**: full-text search, favicons, `POST /robots`
  search, `/urls/inspect` naming its sitemaps and robots.txt line, the URL grid filtered to the ends of links, a
  view's distinct ends, the `inlinks` gap, the parallel link pass.

'''
text = text[:start] + resumed + text[end:]

old_item = text[text.index('4. The Simple design. The IA, the design doc'):text.index('5. Round 3: New crawl settings')]
new_item = '''4. Done on 2026-11-11: the Simple design, [`v2-ui-simple.md`](v2-ui-simple.md), with its mockups published for
   the owner (above). The work goes on under the standing delegation unless the owner steers it.
'''
text = text.replace(old_item, new_item)
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', newline))
print('ok', len(text))
