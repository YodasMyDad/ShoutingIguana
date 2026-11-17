# Step 3b: title and description length in pixels (owner said yes, 2026-11-17)

## The decision
The owner: "should those checks measure pixel width, as Screaming Frog does, instead of characters?" — "Yes".
Checks: `title-too-long`, `title-too-short`, `description-too-long`, `description-too-short` (ids never change).
Advanced detail like `612 px, over 561 (64 characters)`; Simple says where Google will likely cut it:
"Google will likely cut this title off after '…Garden Sofa Se'" (no pixels in Simple). Analysis settings move to pixels
(with a characters hint). Context: `docs/v2-comparison.md` ("In part (7)": SF "Over 561 Pixels"/"Below 200 Pixels",
description "Over 985 Pixels"/"Below 400 Pixels"), `docs/v2-new-checks.md` on branch `ui/3b-newchecks` (its "changes to
existing checks" entry for pixels), the real-site audit (`.../scratchpad/audit3b/accuracy-onpage.md`: 60 of garden's 84
title flags are 61-65 characters).

## Stage A now: research and design ONLY (no builds or tests: a long timing-sensitive verify is running on this
machine; reading code and the web is fine)
1. Research, with sources: how Google renders SERP titles and descriptions today (desktop and mobile): font family and
   size, the container width at which it truncates, how truncation works (word boundary, ellipsis), and current
   industry thresholds (Screaming Frog's 561/200 and 985/400 and why; Moz/Semrush/Sitebulb if documented). Mark
   anything unverified.
2. Design: an embedded width table (e.g. Arial/Roboto advance widths per code point at 1000 units/em from a font
   with a permissive licence or a standard metrics source; note the licence), fallback width for unknown/CJK/emoji
   characters, how to measure (sum of advances × size), the cut point for Simple (the last whole word that fits),
   default limits, settings shape (pixels, with the old character settings migrated how?), sidecar/API changes
   (AnalysisSettings fields, the analysis settings endpoint), app Settings > Analysis changes, the detail strings,
   plain words, planted cases on the synthetic site (titles that pass in characters but fail in pixels and vice
   versa), and `AnalysisFreshness` (stored analyses change).
3. Write it as `docs/v2-pixel-widths.md` in a new worktree
   `C:/Projects/ShoutingIguana/.claude/worktrees/3b-pixels` on branch `ui/3b-pixels` — create it off `ui/step-3`
   ONLY when told (an integration merge is landing on ui/step-3 now); until then keep the draft in
   `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/pixels-design.md`.
Report the design summary (under 400 words) with sources, and the 5 decisions you're least sure of. Then stop; the
caller will give the go for stage B (implementation) once the verify is done.
