# Lane shopify: crawling Shopify stores properly (2026-11-18)

The owner: "we can't have a crawler that doesn't work on Shopify sites." Product pages on a Shopify store return 429
after about 20 s, even at 1 to 2 requests a second, and stay refused for over 20 minutes.

## What research found
Each point is documented, with its source in the list at the end.
- Shopify limits bots and crawlers, "most strictly when unsigned". Since 30 May 2026 that covers storefront pages too.
  Shopify publishes no numbers. Users report 429s mostly on product URLs, Screaming Frog's users included.
- **The sanctioned way through is a crawler signature (Web Bot Auth), since 29 Aug 2025.**
  - The store owner creates it under Online Store > Preferences > Crawler access > Create signature, giving a name, one
    domain and an expiry.
  - Each signature covers one connected domain, so `www` and the bare domain need one each. It lasts at most 3 months
    and can't be edited.
  - Every request must carry three headers:
    - `Signature-Input` and `Signature`, both copied from the admin page;
    - `Signature-Agent: "https://shopify.com"`, with the quotes as part of the value.
  - The values are fixed text: we compute nothing.
  - Shopify: "If your crawler is receiving rate-limiting errors, then your signature isn't valid." Screaming Frog: a
    new signature can take 30 to 60 minutes to start working.
- **What other tools do:**
  - Screaming Frog (v23.1) has header presets for these, and its FAQ sends Shopify users who hit 429s to them.
  - Sitebulb has a Shopify panel with two paste fields.
  - Semrush has a "Crawl with Web Bot Auth signature" option.
  - None of them rotates user agents, headers or proxies, and Shopify treats rotation as evasion. We don't build it.
- **What triggers a block:** IP reputation and how fast one client asks for the same kind of page. Cloud IPs are refused
  more readily, and Cloudflare sits in front of Shopify.

Sources:
- https://help.shopify.com/en/manual/promoting-marketing/seo/crawling-your-store
- https://changelog.shopify.com/posts/authorize-custom-crawlers-and-tools-with-new-crawler-access-keys
- https://shopify.dev/changelog/bots-and-agents-should-identify-themselves-via-web-bot-auth
- https://www.screamingfrog.co.uk/seo-spider/faq/
- https://support.sitebulb.com/en/articles/12232037-crawling-shopify-websites-with-sitebulb
- https://www.semrush.com/kb/539-configuring-site-audit
- https://http.dev/signature-agent

Reading them is optional. Don't put any store's domain in a search query.

## Rules
Read `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/lanes-common.md`
and follow it, with these exceptions and additions:
- There is no GO-BUILD hold.
- **Base:** `ui/step-3` at 131ffad7. Create the worktree with
  `git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-shopify C:/Projects/ShoutingIguana/.claude/worktrees/3b-shopify 131ffad7`.
- **Ports:** 51240 for the app and 51246 for Storybook.
- **Notes:** `scratchpad/shopify/`, with a `checkpoint.md`.
- **The recheckfix lane is changing these files.** Don't touch them until the orchestrator tells you it has merged:
  - `Crawling/SectionTrickle.cs`, `CrawlSession.cs` (the 429 words);
  - `Storage/CrawlWriter.Recheck.cs`, the schema upgrade code, `Cli/Commands/RecheckCommand.cs`;
  - `MicrodataReader.cs`, `CrawlAnalyzer.cs` (its notes);
  - the recheck summary words in the app.

  If phase A needs a small hook in one of them, keep it minimal and say so in your report.
- Never crawl or fetch a real site. Use the synthetic test site and test servers only.
- **A signature is a secret.**
  - Never log it, print it, put it in a crawl file, an export, a report, a story or a fixture, or send it anywhere but
    the host it was made for.
  - Store it the way the app already stores secrets: Windows Credential Manager, only when the user ticks Remember, as
    the views lane built it (see Settings and the proxy password).
  - Test values are obviously fake.

## Phase A: build now
1. **Engine: a per-host Web Bot Auth signature.**
   - Options carry it keyed by the exact host, and the three headers go only to that host:
     - over https;
     - never to another host, a subdomain or `www`/bare unless that host has its own signature;
     - never across a redirect to another host;
     - never to external links or resources on other hosts;
     - on recheck too, and in a browser render if the engine renders anywhere.
   - Parse `Signature-Input` (RFC 9421 structured fields) enough to:
     - validate its form;
     - read `expires=` (and `created=`), so we can show when it ends;
     - refuse to send an expired one, with a clear reason.
   - `Signature` must look like `sig1=:<base64>:`, with the same label as `Signature-Input`.
   - Tests cover the scoping (each case above), expiry, malformed input, and that no value reaches a log, a crawl file
     (options JSON included) or an export.
2. **Detect Shopify**, as a crawl fact the Storage writer stores. Use response headers (`x-shopid`,
   `powered-by: Shopify`, `x-shopify-stage`, …) and markup (`cdn.shopify.com`, `Shopify.theme`). Verify which signals
   are reliable from Shopify's docs or the stored HTML of the Jitsunami crawl copies in `scratchpad/realsites/crawl4/`:
   copy them first and read the copies only. Facts come from the one parse (`PageFacts`), never from a second parse.
3. **Honest outcome.**
   - A URL refused with 429 on a Shopify store without a signature is "not checked: Shopify limits crawlers without a
     signature", never broken.
   - With a signature, a 429 says the signature may be invalid, expired or for another domain, and that a new one can
     take up to an hour to start working.
   - Reuse the existing not-checked reasons, adding one only if needed. Never rename an id.
4. **The CLI.**
   - `--signature-file <path>` (or a better name) reads the values as Shopify's admin shows them for copying: header
     lines or bare values. It fills in `Signature-Agent` itself and ties the signature to the crawl's start host, or to
     a host named in the file.
   - Crawl and recheck both take it.
   - It never prints the values. `--help` explains where they come from.
   - Read Shopify's admin wording in the help doc before you design the file format, so a user can paste exactly what
     Shopify gives them.
5. **The app, both modes. Settings stays full in both.**
   - **Where you add it:**
     - in the crawl options, a "Shopify crawler access" part, shown always in Advanced;
     - in Simple, inside Options;
     - also offered when the site is detected as Shopify.
   - **The form:**
     - two paste fields;
     - `Signature-Agent` shown as fixed;
     - the domain it's for, defaulting to the crawl's host, with a warning when the host differs;
     - the expiry, read from the input, with a warning in the last 7 days and a refusal once it has expired;
     - Remember, with the Credential Manager consent.
   - **Steps, in Shopify's own words:** "In Shopify admin, go to Online Store › Preferences › Crawler access, choose
     Create signature, pick this domain, then copy both values here." Simple keeps them plain but names Shopify's buttons
     exactly, so the owner can follow them.
   - **When a crawl finds a Shopify store and pages were refused without a signature:**
     - Show it plainly while the crawl runs, and in the results (Simple's Summary and the problem page; Advanced's
       Overview and the not-checked list): how many pages weren't checked, why, and "Add a Shopify signature".
     - After the user adds one, "Recheck these pages" uses the fix loop to fetch only the not-checked pages again.
   - Follow the framed workspace, with no accent stripes. Simple has no jargon: say "signature" because Shopify's
     button does, and explain it in one line.
6. **The test site.**
   - Give it a Shopify-shaped mode:
     - Shopify's headers;
     - product pages that return 429 with `Retry-After: 60` to unsigned requests beyond a small rate;
     - accepts one fixed fake signature, and refuses it when expired or for the wrong host.
   - Its tests:
     - an unsigned crawl reports the product pages as not checked for this reason;
     - a signed crawl reads them all;
     - a signed recheck of the not-checked pages fixes them;
     - the signature never reaches another host.
   - If you plant anything on the synthetic site, keep the whole issues table's expected counts right.

## Phase B: wait for the orchestrator's word
Unsigned pacing on Shopify, such as a slower start for `/products/`, or a longer silence after the first 429 instead of
gaps that double from 1 minute. This waits for the recheckfix merge and the 1-a-second crawl's result. Leave a seam
for it, and don't build it.

## Verify
- Engine: Release build and test, and `verify-engine.sh` (bench flat; readbench under load is noted, not chased).
- App: typecheck, lint and format; Vitest, including `classes.test.ts`; the stories for every component you touched,
  axe-clean in both themes, in forced colours and in reduced motion; the e2e specs for crawl options, the new-crawl
  flow, Summary and Overview, with `npm run build:sidecar` first and the stale e2e crawl deleted.
- Take shots of the new parts, before and after, light and dark, at 1440 and 960 px, with an `index.md`.

## Ready note
Write `scratchpad/shopify/ready.md`: the commit to merge, what was built against this list, the tests, the verify
lines, the shots, and the decisions the brief left open. Then report in under 450 words.
