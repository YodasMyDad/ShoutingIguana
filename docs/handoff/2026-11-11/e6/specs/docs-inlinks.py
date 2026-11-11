P = '../docs/v2-sidecar-api.md'

EDITS = [
(P,
''' "linkTotals":{"inlinks":4,"outlinks":63},
''',
''' "linkTotals":{"inlinks":4,"outlinks":63,"hyperlinkInlinks":3},
'''),
(P,
'''  a running crawl's included, where `links` (hyperlinks only, on the pages audited) is null; a running crawl's are
  kept for 2 s, as its other counts are.
''',
'''  a running crawl's included, where `links` (hyperlinks only, on the pages audited) is null; a running crawl's are
  kept for 2 s, as its other counts are.
  - **`linkTotals.hyperlinkInlinks`** (E6) is the hyperlinks among those to the URL, every one the crawl stored: the
    `total` of `/urls/inlinks` with `"kinds": ["hyperlink"]`, and **the links grid's `matching`** under
    `to.id` `eq` this URL, `kind` `in` `["hyperlink"]`, `selfLink` `is` false. It's the figure to show as a link to
    Links ("N hyperlinks"), because it lands.
  - **`links.inlinks` can be less, and isn't that figure.** The analysis counts the hyperlinks on the pages it
    audited, and it doesn't audit Cloudflare's own pages (a URL with `/cdn-cgi/` in it, such as the email-protection
    page), which the crawl fetches and parses like any other and whose links it stores. So for a URL such a page
    links to, `hyperlinkInlinks` is `links.inlinks` plus that page's hyperlinks to it, and for every other URL the
    two are equal. On the 100k benchmark crawl that is 24 of 112,590 URLs (the menu's targets, each one more, the
    home page two); on the garden crawl, none. The URL grid's `inlinks` column, sort and filters are the analysis's.
'''),
]
