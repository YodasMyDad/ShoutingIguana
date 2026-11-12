P = '../docs/v2-sidecar-api.md'

EDITS = [
# --- Sitemaps
(P,
'''              "statusCode":200,"isIndex":true,"entries":0,"sitemaps":3,"error":null,
              "urls":{"listed":265,"fetched":262,"indexable":250,"nonIndexable":12,"robotsDisallowed":1,"redirectingOrBroken":4}},
             {"id":4,"url":"https://www.example.com/sitemaps/missing.xml","parentId":1,"foundVia":"index","state":"failed",
              "statusCode":404,"isIndex":false,"entries":0,"sitemaps":0,"error":"HTTP 404",
              "urls":{"listed":0,"fetched":0,"indexable":0,"nonIndexable":0,"robotsDisallowed":0,"redirectingOrBroken":0}}]}
```
''',
'''              "statusCode":200,"isIndex":true,"entries":0,"sitemaps":3,"error":null,
              "urls":{"listed":265,"fetched":262,"indexable":250,"nonIndexable":12,"robotsDisallowed":1,"redirectingOrBroken":4},
              "listedSitemaps":3},
             {"id":4,"url":"https://www.example.com/sitemaps/missing.xml","parentId":1,"foundVia":"index","state":"failed",
              "statusCode":404,"isIndex":false,"entries":0,"sitemaps":0,"error":"HTTP 404",
              "urls":{"listed":0,"fetched":0,"indexable":0,"nonIndexable":0,"robotsDisallowed":0,"redirectingOrBroken":0},
              "listedSitemaps":null}]}
```
'''),
(P,
'''- **`entries`** is the URL entries the crawl read in the file, as it counted them: every `<loc>`, repeats included.
  **`sitemaps`** is the sitemaps an index lists that the crawl has.
''',
'''- **`entries`** is the URL entries the crawl read in the file, as it counted them: every `<loc>`, repeats included.
  **`sitemaps`** is the sitemaps an index lists that the crawl has.
- **`listedSitemaps`** (E6) is how many sitemaps the file itself lists, as the crawl read it: every `<sitemap>` entry,
  repeats and entries that aren't URLs included, so "the crawl has 3 of its 3 sitemaps" is `sitemaps` of
  `listedSitemaps`. It's never less than `sitemaps`.
  - **An index over the crawler's limits** says so here: the file is read to its end whatever the limits, so an index
    of 5,000 sitemaps has `listedSitemaps` 5,000, while `sitemaps` counts those the crawl has of it: the ones it
    read, up to its limit of 1,000 sitemaps in a crawl, and the first it left unread (`notRead`), whose `error` says
    which limit. The same goes for an index nested deeper than 3 levels.
  - It's 0 for a sitemap that lists only URLs. A file whose read failed part-way has what it listed up to there, as
    `entries` does.
  - **Null when it isn't known**: the file's body was never read (it failed first, as a 404 does; it was left unread;
    it's still pending), or the crawl was made by an engine from before E6, which didn't keep the count. A crawl
    continued by this engine has it for the sitemaps read from then on.
'''),
# --- robots.txt
(P,
'''`POST /api/crawls/{id}/robots` takes `{ offset, limit }` (limit 1 to 1,000, default 100):

```json
{"total":17,"offset":0,"respected":true,"userAgent":"ShoutingIguana","blocked":2,
 "origins":[{"origin":"https://www.example.com","isInternal":true,"state":"read","statusCode":200,
             "fetchedAt":"2026-10-31T20:13:58.588Z","error":null,"bytes":79,"blocked":0}]}
```
''',
'''`POST /api/crawls/{id}/robots` takes `{ offset, limit, search, blocked }` (limit 1 to 1,000, default 100), every field
optional:

```json
{"search":"example","blocked":true,"offset":0,"limit":100}
```

```json
{"total":17,"offset":0,"respected":true,"userAgent":"ShoutingIguana","blocked":2,
 "origins":[{"origin":"https://www.example.com","isInternal":true,"state":"read","statusCode":200,
             "fetchedAt":"2026-10-31T20:13:58.588Z","error":null,"bytes":79,"blocked":2}],
 "matching":1,"counts":{"all":3,"blocked":1,"notBlocked":2}}
```

- **`search`** (E6) finds origins by their name: those whose origin contains the text, case ignored, at most 1,000
  characters (`invalid-query` on `search` past that). **`blocked`** (E6) is the filter: `true` for only the origins
  that blocked at least one URL, `false` for only those that blocked none. The list keeps its order under both, and
  `offset` and `limit` page what matches.
- **`matching`** (E6) is the origins the search and the filter match, which the page is of. `total` stays every
  origin of the crawl, and the top-level `blocked` every URL the crawl was kept from, whatever is asked.
- **`counts`** (E6) is what a filter bar shows: of the origins the `search` finds (every origin with none), `all`, those
  that `blocked` something and those that didn't (`notBlocked`). Each is the `matching` its filter gives under the
  same search, so a chip's number is the rows it opens: `blocked` with `"blocked": true`, `notBlocked` with
  `"blocked": false`, `all` with neither. A sidecar from before E6 sends neither `matching` nor `counts`, and refuses
  `search` and `blocked` as fields it doesn't know (`bad-request`).
'''),
(P,
''' "bytes":79,"blocked":12,"userAgent":"ShoutingIguana",
 "lines":[{"number":1,"text":"User-agent: *","kind":"userAgent","applies":true,"blocked":null,"problem":null},
          {"number":2,"text":"Disallow: /private/","kind":"disallow","applies":true,"blocked":12,"problem":null},
          {"number":3,"text":"","kind":"blank","applies":null,"blocked":null,"problem":null},
          {"number":4,"text":"Sitemap: https://www.example.com/sitemap.xml","kind":"sitemap","applies":true,"blocked":null,"problem":null}],
 "unmatched":{"unreachable":0,"unknown":0}}
```
''',
''' "bytes":79,"blocked":12,"userAgent":"ShoutingIguana",
 "lines":[{"number":1,"text":"User-agent: *","kind":"userAgent","applies":true,"blocked":null,"problem":null},
          {"number":2,"text":"Disallow: /private/","kind":"disallow","applies":true,"blocked":12,"problem":null},
          {"number":3,"text":"","kind":"blank","applies":null,"blocked":null,"problem":null},
          {"number":4,"text":"Sitemap: https://www.example.com/sitemap.xml","kind":"sitemap","applies":true,"blocked":null,"problem":null}],
 "unmatched":{"unreachable":0,"unknown":0},"respected":true}
```

- **`respected`** (E6) is the list of origins' `respected`: false for a crawl told to ignore robots.txt, which blocked
  nothing by it. It's here so the file's page needn't read the list for it.
'''),
(P,
'''`POST /api/crawls/{id}/robots/blocked` with `{ origin, line, reason, offset, limit }` pages the origin's blocked URLs,
in crawl order; `line` narrows them to one line's and `reason` to one reason's, and both are optional:
''',
'''`POST /api/crawls/{id}/robots/blocked` with `{ origin, line, reason, urlId, offset, limit }` pages the origin's blocked
URLs, in crawl order; `line` narrows them to one line's, `reason` to one reason's and `urlId` (E6) to one URL, and all
three are optional:
'''),
(P,
'''- `total` is the origin's blocked URLs, and `matching` those the line or reason narrows to. Paging is by `offset` and
  `limit` (1 to 1,000, default 100): the list is worked out once per origin and kept, so any offset costs the same.
''',
'''- `total` is the origin's blocked URLs, and `matching` those the line or reason narrows to. Paging is by `offset` and
  `limit` (1 to 1,000, default 100): the list is worked out once per origin and kept, so any offset costs the same.
- **`urlId`** (E6) answers for one URL: its row, with why it was refused, and `matching` 1; or no rows and `matching`
  0 for a URL this origin didn't block (another origin's, one that was fetched, an id the crawl doesn't have). It's a
  number, as every URL's id is (`bad-request` otherwise). The inspector gives the same facts with the URL
  ([its `robotsTxt` test](#the-inspector-e3a)), so a screen that has inspected the URL needn't ask.
'''),
# --- The inspector
(P,
''' "namedBy":{"total":0,"checks":[]},
 "summary":{"available":true,"reason":null},
 "source":{"stored":false}}
```
''',
''' "namedBy":{"total":0,"checks":[]},
 "summary":{"available":true,"reason":null},
 "source":{"stored":false},
 "listedIn":{"total":1,"sitemaps":[{"id":3,"url":"https://www.example.com/sitemaps/pages-0.xml"}]}}
```
'''),
(P,
'''  | `robotsTxt` | robots.txt allowed the request | `unreachable`: true when robots.txt couldn't be read, which refuses everything |
''',
'''  | `robotsTxt` | robots.txt allowed the request | `unreachable`: true when robots.txt couldn't be read, which refuses everything. And (E6) `origin`, `reason`, `line` and `rule`, below |
'''),
(P,
'''  `indexable` and `reason` are the verdict, as the analysis judged it (`judgedBy: "analysis"`); without a URL summary
  they are the crawl's own judgement where the analysis hasn't seen the URL (`judgedBy: "crawl"`), and both are null
  for a page that was only status-checked.
''',
'''  `indexable` and `reason` are the verdict, as the analysis judged it (`judgedBy: "analysis"`); without a URL summary
  they are the crawl's own judgement where the analysis hasn't seen the URL (`judgedBy: "crawl"`), and both are null
  for a page that was only status-checked.

  **The `robotsTxt` test links exactly** (E6), so the inspector can open the file that decided the URL at the line
  that did:

  ```json
  {"test":"robotsTxt","outcome":"fail","unreachable":false,"origin":"https://www.example.com","reason":"rule","line":2,"rule":"Disallow: /private/"}
  ```

  - **`origin`** is the origin whose robots.txt decides the URL, named as the list of origins names it, to pass to
    `/robots/file` and `/robots/blocked`. It's there whatever the outcome when the crawl has a robots.txt answer for
    that origin, and always for a URL robots.txt refused; left out when the crawl never asked that origin for one (so
    there's no file to open).
  - **`reason`**, for a URL robots.txt refused, is why, as its row in `/robots/blocked` says: `rule` (with the
    **`line`** of the origin's stored file that refused it, and that line's text as **`rule`**), `unreachable`
    (robots.txt couldn't be read when the URL came up) or `unknown` (the file kept doesn't refuse it: a later run read
    it again, and it had changed). It's worked out by the crawler's own matcher from the stored file, as that list is,
    for this URL alone: the origin's file is parsed once and kept, so inspecting one of a hundred thousand refused URLs
    matches one. `unreachable` stays as it was: true where the crawl stored that robots.txt couldn't be read with the
    URL itself.
  - A field that doesn't apply is left out, as the other tests' are.
'''),
(P,
'''- **`source.stored`** says whether `/source` has the HTML.
''',
'''- **`source.stored`** says whether `/source` has the HTML.
- **`listedIn`** (E6) names the sitemaps that list the URL, `{ total, sitemaps: [{ id, url }] }`, in the order the
  crawl found them: `id` is the sitemap's in [`/sitemaps`](#sitemaps-e3b), which `/sitemaps/urls` takes, so the
  inspector opens the sitemap itself. `total` is how many list it and `sitemaps` the first 20 of them. Only the
  sitemaps whose own file lists the URL are named, not the indexes over them. It's read from what the crawl stored,
  so it's there with or without a URL summary and while a crawl runs, where `links.inSitemap` (the analysis's, which
  also says "in none, and every sitemap was read") is null without one. `total` 0 with `links.inSitemap` null means
  no sitemap read so far lists it.
'''),
]
