// Every check's detail in Simple's plain words (docs/v2-ui-simple.md, "Plain words"; the fixit audit's item 7): one
// representative detail or more per check in the engine's catalog, harvested from real crawls and the engine's own
// formats, old and new, each said without jargon or units Simple doesn't use. The catalog is read from the engine's
// source, so a new check fails here until it has a line.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { plainDetailWords } from './plainDetails';
import { plainDetail } from './plainEvidence';

const CATALOG = path.join(
  __dirname,
  '..',
  '..',
  '..',
  'engine',
  'src',
  'ShoutingIguana.Engine',
  'Analysis',
  'IssueCatalog.cs',
);

/** Each check in the engine's catalog: its id and its evidence columns. */
function catalogChecks(): { id: string; evidence: string[] }[] {
  const source = readFileSync(CATALOG, 'utf8');
  return source
    .split('public static readonly IssueDefinition ')
    .slice(1)
    .flatMap((part) => {
      const id = /new\("([a-z0-9-]+)", IssueCategory\./.exec(part)?.[1];
      if (!id) return [];
      const evidence = /Evidence = \[([^\]]*)\]/.exec(part)?.[1] ?? '';
      return [{ id, evidence: [...evidence.matchAll(/"([A-Za-z]+)"/g)].map((match) => match[1] ?? '') }];
    });
}

/**
 * Details as the engine writes them, for every check: real ones from the audit's crawl copies, the engine's other
 * forms, and the forms the engine now writes beside the ones older crawls keep. Null: the check writes none.
 */
const DETAILS: Readonly<Record<string, readonly (string | null)[]>> = {
  // Response codes
  'internal-client-error': ['404 Not Found', '403 Forbidden', '410 Gone', 'HTTP 499'],
  'internal-server-error': ['500 Internal Server Error', '503 Service Unavailable', 'HTTP 520'],
  'internal-unreachable': ['Timed out', 'Connection failed'],
  'internal-blocked': ['Cloudflare challenge', 'Rate limited (429 Too Many Requests)', '403 Forbidden'],
  'content-type-mismatch': [
    'Used as a script, sent as text/plain',
    'Used as a script and a stylesheet, sent as text/html; charset=utf-8',
    'Used as an image, sent with no Content-Type',
  ],
  'soft-404': [
    "The title or H1 says the page wasn't found",
    'The title reads as an error: "Sorry, this page was not found | Synthetic Store" · 255 words, under 300',
    'The H1 reads as an error: "Page not found" · 12 words, under 300',
  ],
  // Redirects
  'redirect-chain': [
    '2 redirects',
    '3 redirects',
    "More than 20 redirects, which browsers don't follow",
    '11 redirects, more than the 10 Googlebot follows',
  ],
  'redirect-loop': [
    '2 hops',
    '2 hops: /plants/loop-a (301) → /plants/loop-b (301) → back to /plants/loop-a',
    '1 hop: /plants/refresh-loop (meta refresh) → back to /plants/refresh-loop',
  ],
  'redirect-broken': [
    'Ends at a URL that returns 404 Not Found',
    'Ends at a URL that timed out',
    '301 Moved Permanently without a usable Location header',
  ],
  'redirect-temporary': ['302 Found', '307 Temporary Redirect'],
  'redirect-downgrade': [null],
  'redirect-temporary-cached': ['302 Found, cached for 30 days', '307 Temporary Redirect, cached for 36 hours'],
  'meta-refresh': [
    'Moves to /plants/refresh-target after 0 seconds',
    'Moves to /plants/thin after 30 seconds',
    'The Refresh header moves to /plants/thin after 0 seconds',
    'Reloads the page after 60 seconds',
    'Moves to https://example.com/elsewhere',
  ],
  // Links
  'broken-internal-links': [
    '1 broken URL',
    '4 broken URLs',
    '1 broken URL (it redirects to a URL that returns 404 Not Found)',
    '37 broken URLs (the first redirects to a URL that returns 404 Not Found)',
  ],
  'broken-external-links': ['1 broken URL', '2 broken URLs', '3 broken URLs (the first redirects in a loop)'],
  'links-to-redirects': ['1 redirecting URL', '6 redirecting URLs'],
  'internal-nofollow-links': ['Links to 1 URL', 'Links to 3 URLs'],
  'empty-anchor-text': ['Links to 1 URL', 'Links to 5 URLs'],
  'broken-fragment-links': [
    '2 links',
    '2 links to missing sections: #nowhere, /plants/fragment-target#missing',
    '1 link to missing sections: #specs',
  ],
  'orphan-page': ['Only found in a sitemap', 'Only nofollow links lead here'],
  'uncrawlable-links': ['1 link: "Thin page" (onclick)', '3 links: no text (javascript:), "Shop" (routerLink) …'],
  'broken-resources': ['1 broken file', '2 broken files (the first redirects to a URL that returns 404 Not Found)'],
  'links-to-localhost': ['2 links, such as http://localhost:8080/admin', '1 link, such as http://192.168.1.4/test'],
  'few-internal-outlinks': [
    'No links to other pages',
    '1 link to another page, under 3',
    '2 links to other pages, under 3',
  ],
  'generic-anchor-text': ['2 links: "Click here", "Read more…"', '4 links: "Read more" ×3, "here"'],
  'non-indexable-inlinks-only': [
    "Linked from 1 page, which isn't indexable",
    'Linked from 4 pages, none of them indexable',
  ],
  'deep-page': ['5 clicks from the start, over 4'],
  'too-many-links': ['1,026 internal links, over 1,000'],
  'slow-external-links': ['1 slow link, the slowest 3.2 s', '3 slow links, the slowest 12.0 s'],
  'pagination-not-linked': [
    'rel="next" http://127.0.0.1:54384/plants/unlinked-series/page/2/ has no link',
    'rel="prev" http://127.0.0.1:54384/plants/unlinked-series/ has no link',
  ],
  'pagination-broken': ['rel="next" returns 404 Not Found', 'rel="prev" redirects to a URL that returns 410 Gone'],
  'pagination-to-non-indexable': ['rel="next" is noindex', 'rel="prev" is canonicalised to another URL'],
  'pagination-sequence-error': [
    "/plants/noindex doesn't point back",
    'rel="next" names the page itself',
    '2 rel="next" URLs',
    'rel="next" leads back to /plants/series/',
  ],
  // Indexability
  'noindex-page': [null],
  'nofollow-page': [null],
  'blocked-by-robots': [
    null,
    'robots.txt unreachable: 500 Internal Server Error',
    'robots.txt unreachable: DNS lookup failed',
    'Disallow: /checkout, line 50',
  ],
  'snippet-blocked': ['nosnippet', 'max-snippet:0'],
  'robots-txt-unreachable': [
    'https://example.com/robots.txt (500 Internal Server Error)',
    'https://example.com/robots.txt (no response), https://www.example.com/robots.txt (DNS lookup failed)',
  ],
  'robots-txt-unreachable-other-origin': [
    'https://127.0.0.1:59203/robots.txt (TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.)',
    'https://cdn.example.com/robots.txt (503 Service Unavailable)',
  ],
  'robots-txt-missing': ['https://example.com/robots.txt (404 Not Found)'],
  'site-hidden-from-search': ['212 of 240 pages are noindex', '1 of 1 page is noindex'],
  'robots-directives-conflict': ['The HTML says "index, follow"; the X-Robots-Tag header says "noindex"'],
  'noindex-near-home': ['1 click from the start', '2 clicks from the start'],
  'unavailable-after-passed': ['unavailable_after: 2020-01-01, passed'],
  'media-preview-blocked': ['max-image-preview:none', 'max-video-preview:0'],
  'translation-blocked': [null],
  'indexifembedded-without-noindex': [null],
  // Canonicals
  'canonicalised-page': [null],
  'canonical-missing': [null],
  'canonical-multiple': [
    '2 different URLs',
    '2 URLs: http://127.0.0.1:54384/plants/canonical-multiple (head) · http://127.0.0.1:54384/plants/thin (head)',
    '2 URLs: https://example.com/a (HTTP header) · https://example.com/b (head)',
  ],
  'canonical-broken': [
    'The canonical URL returns 404 Not Found',
    'The canonical URL redirects to a URL that returns 500 Internal Server Error',
  ],
  'canonical-to-redirect': [
    'The canonical URL redirects (301 Moved Permanently) to http://127.0.0.1:54384/plants/thin',
    'The canonical URL redirects (301 Moved Permanently) and, after 2 redirects, ends at http://127.0.0.1:54384/plants/thin',
    'The canonical URL redirects back to this page',
  ],
  'canonical-to-non-indexable': [
    'The canonical URL is noindex',
    'The canonical URL is canonicalised to another URL',
    'The canonical URL is blocked by robots.txt',
  ],
  'canonical-outside-head': [
    "It's in the body (after <body> on line 121)",
    "It's in the body",
    '<div id="promo"> on line 12 ended the head early',
    'Img ended the head early',
  ],
  'canonical-relative': ['/plants/canonical-relative'],
  'canonical-invalid': ['http://[not a host]/'],
  'noindex-with-canonical': [null],
  'canonical-to-first-page': ['Page 2 names page 1 as its canonical', 'A later page names page 1 as its canonical'],
  'canonical-other-domain': ['On another host: 127.0.0.1:54385', 'On another host: shop.example.org'],
  'canonical-loop': [
    'A loop of 2: /plants/canonical-loop-a → /plants/canonical-loop-b → back to /plants/canonical-loop-a',
  ],
  'canonical-on-redirect': [
    'Redirects to http://127.0.0.1:54384/plants/thin; its canonical header names http://127.0.0.1:54384/plants/trailing/',
  ],
  'canonical-fragment': ['#top'],
  'canonical-invalid-attribute': ['media', 'hreflang'],
  'canonical-unlinked': ['Only canonicals lead here'],
  // Sitemaps
  'sitemap-non-indexable': [
    'The URL is blocked by robots.txt',
    'The URL returns 500 Internal Server Error',
    'The URL is noindex',
    'The URL redirects (301 Moved Permanently)',
    'The URL is canonicalised to another URL',
  ],
  'not-in-sitemap': [null],
  'sitemap-error': [
    'http://127.0.0.1:54384/sitemaps/missing.xml (404 Not Found)',
    'https://example.com/a.xml (DNS lookup failed), https://example.com/b.xml (500 Internal Server Error) and 2 more',
  ],
  'sitemap-missing': [null],
  'sitemap-url-other-host': [
    'The URL redirects (301 Moved Permanently)',
    'The URL returns 404 Not Found',
    "The URL wasn't checked",
    'The URL is http://, the sitemap https://',
  ],
  'robots-txt-no-sitemap': [
    'https://www.gardenfurnituresets.co.uk/robots.txt has no Sitemap: line; the sitemap was found at https://www.gardenfurnituresets.co.uk/sitemap.xml',
  ],
  'sitemap-over-limit': ['https://example.com/a.xml (62,113 URLs), https://example.com/b.xml (51.2 MB) and 2 more'],
  'sitemap-near-limit': ['https://example.com/a.xml (45,000 URLs)', 'https://example.com/index.xml (48,000 sitemaps)'],
  'sitemap-empty': ['http://127.0.0.1:54384/sitemaps/empty.xml', 'https://example.com/a.xml and 2 more'],
  'sitemap-url-repeated': ['In 2 sitemaps'],
  'sitemap-lastmod-invalid': [
    '1 entry: 1 in the future (http://127.0.0.1:54384/sitemaps/pages-0.xml)',
    '5 entries: 2 in the future, 3 unreadable (https://example.com/a.xml and 1 more)',
  ],
  // Titles
  'title-missing': [null],
  'title-multiple': [
    '2 titles',
    '2 titles: "Two titles | Synthetic Store planted pa…" · "A second title for the same planted page"',
  ],
  'title-too-long': [
    '61 characters, over 60',
    '61 characters (63 counting full-width characters as two), over 60',
    '736 px, over 580 (59 characters)',
    '853 px, over 580 (98 characters)',
  ],
  'title-too-short': ['16 characters, under 30', '157 px, under 200 (16 characters)', '12 characters'],
  'title-duplicate': ['1 other page', '3 other pages'],
  // Meta descriptions
  'description-missing': [null],
  'description-multiple': [
    '2 meta descriptions',
    '2 meta descriptions: "Two descriptions: a planted test page w…" · "A second meta description on the same p…"',
  ],
  'description-too-long': ['201 characters', '1,146 px, over 985 (201 characters)'],
  'description-too-short': ['68 characters, under 70', '63 px, under 400 (10 characters)', '68 characters'],
  'description-duplicate': ['1 other page'],
  // Headings
  'h1-missing': [null, 'The H1 has no text'],
  'h1-multiple': ['2 H1s', '2 H1s: "Two H1s" · "A second H1 on the page"'],
  'h1-duplicate': ['1 other page'],
  'heading-levels-skipped': ['An H3 before the H1', 'H1 → H3', 'H2 → H4', 'H3 → H5', 'H1 → H4: "Out Of Stock"'],
  'h1-image-only': ['The H1 is an image: "Synthetic Store logo"'],
  'heading-too-long': [
    'H1 of 77 characters, over 70',
    'H2 of 88 characters, over 70',
    'H2 of 86 characters, over 70: "Toddington Steel Garden Bench…"',
  ],
  'h2-missing': ['604 words, no H2', '1,036 words, no H2'],
  // Content
  'low-word-count': ['100 words', '100 words, under 200', '1 word, under 200'],
  'placeholder-text': ['"Lorem ipsum dolor sit amet conse ctetur adipisicing elit, s…"'],
  'readability-low': ['Reading ease -171, under 30 (very difficult)', 'Reading ease 12, under 30 (very difficult)'],
  'boilerplate-heavy': ["213 of 1,009 words are the page's own (21%)"],
  'duplicate-content': ['1 other page', '4 other pages'],
  'near-duplicate-content': ['1 similar page', '3 similar pages'],
  'duplicate-site-version': [
    'Answers 200 OK instead of redirecting to https://example.com/',
    'Redirects to http://www.example.com/, which answers 200 OK instead of redirecting to https://example.com/',
  ],
  // Images
  'image-missing-alt': ['1 image', '7 images'],
  'image-alt-too-long': ['1 image', '2 images'],
  'image-missing-dimensions': ['1 image', '21 images'],
  'image-broken': ['1 image', '1 image (it redirects to a URL that returns 404 Not Found)'],
  'image-too-large': [
    '2 images over 200 KB',
    '1 image over 200 KB: 2.9 MB',
    '2 images over 200 KB, the largest 306 KB',
  ],
  'image-missing-src': [
    '1 image',
    '1 image: alt "Product photo"',
    '1 image: class "hero lazy"',
    '1 image, in the header',
    '3 images, the first: alt "Sofa in the garden"',
  ],
  'image-no-srcset': ['1 image, 1,200 px wide', '3 images, the widest 1,600 px'],
  'image-legacy-format': ['1 image: 100 KB', '10 images, the largest 185 KB'],
  'image-alt-unhelpful': ['1 image', '4 images'],
  'image-hotlinked': ['1 image from 1 site', '5 images from 2 sites'],
  'image-inline-large': [
    '1 inline image: 12 KB',
    '2 inline images, the largest 48 KB',
    '1 inline image: 12 KB, alt "Logo"',
    '2 inline images, the largest 48 KB, alt "Hero banner"',
  ],
  // Hreflang
  'hreflang-invalid-code': ['en-UK'],
  'hreflang-missing-self': [
    'None of its 2 entries names this URL',
    "Its one entry doesn't name this URL",
    'None of its 3 entries names this URL; "fr" names https://example.com/fr/',
  ],
  'hreflang-missing-return': ["1 alternate doesn't link back", "3 alternates don't link back"],
  'hreflang-broken-target': ['1 URL', '2 URLs'],
  'hreflang-to-non-indexable': ['1 URL', '2 URLs'],
  'hreflang-duplicate-code': ['en', 'en: /plants/hreflang-conflict · /plants/hreflang-fr'],
  'hreflang-missing-x-default': [null],
  'hreflang-outside-head': [
    "2 entries in the body: it's in the body (after <body> on line 121)",
    '1 entry in the body',
    '1 entry in the body: <script> on line 9 ended the head early',
  ],
  'hreflang-inconsistent-code': ['Names /plants/hreflang-code-b as "fr"; /plants/hreflang-code-b calls itself "fr-CA"'],
  'hreflang-header-mismatch': [
    'The HTML names 3 versions, the header 2; only in the HTML: x-default',
    'The HTML names 2 versions, the header 2; only in the HTML: de; only in the header: fr',
  ],
  'hreflang-unlinked': ['No link or sitemap entry leads here'],
  'hreflang-lang-mismatch': ['hreflang en, lang fr'],
  'hreflang-self-only': [null],
  // Structured data
  'structured-data-invalid': [
    `'"' is invalid after a value. Expected either ',', '}', or ']'. LineNumber: 0 | BytePositionInLine: 54.`,
    `Block 2 of 3, line 1, position 55: '"' is invalid after a value. Expected either ',', '}', or ']'.`,
    'A property name escapes an invalid character, such as half of a surrogate pair',
    'Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 4 | BytePositionInLine: 1.',
  ],
  'structured-data-missing-required': [
    'Product: needs offers or review or aggregateRating',
    'Restaurant: needs address; MusicEvent: needs location',
  ],
  'structured-data-schema-errors': [
    "Organisation isn't a schema.org type",
    `Offer: "prise" isn't a schema.org property; Event: startDate "tomorrow" isn't an ISO 8601 date; and 2 more`,
    `Offer: priceCurrency "Dollars" isn't ISO 4217`,
    `@context "http://example.org" isn't schema.org`,
    `Product: "color" isn't a property of Product`,
  ],
  'structured-data-missing-recommended': [
    'Article: lacks image, datePublished, author',
    'Product: lacks offers.priceValidUntil',
  ],
  'structured-data-on-noindex': ['Product', 'Product, Recipe'],
  // Security
  'no-https': [null],
  'http-page': [null],
  'mixed-content': ['1 HTTP resource', '3 HTTP resources'],
  'security-headers-missing': [
    'Missing Referrer-Policy',
    'Missing Strict-Transport-Security, Content-Security-Policy',
    'Missing X-Content-Type-Options, Content-Security-Policy, X-Frame-Options, Referrer-Policy',
  ],
  'security-headers-weak': [
    'HSTS max-age 7889238 (91 days), under a year',
    'HSTS max-age 10886400 (126 days), under a year',
    'HSTS without max-age',
    'HSTS max-age "abc" unreadable',
    'X-Content-Type-Options "sniff"',
    'HSTS max-age 3600 (1 hour), under a year; X-Content-Type-Options "none"',
  ],
  'cookies-insecure': [
    'localization: no Secure; cart_currency: no Secure',
    'sessionid: no HttpOnly',
    'ASP.NET_SessionId: no Secure, no HttpOnly',
    'tracker: SameSite=None without Secure',
  ],
  'form-not-secure': ['Sends to http://example.com/subscribe', 'A password form on an http:// page'],
  'protocol-relative-links': [
    '15 URLs, such as //jitsunami.com/cdn/shop/files/favicon_32x32.png?v=1694436235',
    '1 URL, such as //127.0.0.1:54384/static/app.js',
  ],
  // URLs
  'url-uppercase': [null],
  'url-underscores': [null],
  'url-too-long': ['129 characters', '129 characters, over 115'],
  'url-tracking-parameters': ['utm_source', 'utm_source, utm_medium'],
  'url-multiple-slashes': [null],
  'url-space': [null],
  'url-parameters': ['sort', 'register'],
  'url-non-ascii': [null],
  'url-repetitive-path': [null],
  'url-internal-search': [null, 'q'],
  // Performance
  'slow-response': [
    '5,476 ms (first byte after 5,475 ms)',
    '1,200 ms, over 1,000 ms (first byte after 1,200 ms)',
    '3,480 ms, over 1,000 ms (first byte after 2,950 ms)',
  ],
  'large-page': ['1.1 MB', '1.1 MB, over 1 MB'],
  'html-past-crawl-limit': ['2.1 MB', '2.1 MB, past the 2 MB Google reads'],
  'resource-past-crawl-limit': ['2.1 MB', '2.1 MB, past the 2 MB Google reads'],
  // HTML
  'viewport-missing': [null],
  'lang-missing': [null],
  'charset-missing': [null, 'A meta tag declares it, but ends at byte 6,158, past the first 1,024 bytes browsers read'],
  'open-graph-missing': [
    'Missing og:title, og:description, og:image',
    'Empty og:description',
    'Empty og:title, og:description',
    'Missing og:image; empty og:title',
  ],
  'x-card-missing': [null],
  'x-card-incomplete': [
    'No title (twitter:title or og:title); No image (twitter:image or og:image)',
    "twitter: tags written as property=, which X doesn't read",
    `Card type "big" isn't one X shows`,
    'No twitter:player',
  ],
  'social-image-relative': ['og:image is /img/0.jpg', 'twitter:image is img/card.png'],
  'og-url-not-canonical': ['og:url is http://127.0.0.1:54384/'],
  'social-tags-multiple': [
    '2 og:title',
    '2 og:title, 2 twitter:card',
    '2 og:title: "A" · "B"; 2 twitter:card: "summary" · "summary_large_image"',
  ],
  'og-title-duplicate': ['1 other page'],
  'og-description-duplicate': ['6 other pages'],
  'social-image-broken': [
    'og:image returns 404 Not Found',
    'twitter:image redirects to a URL that returns 404 Not Found',
    'og:image is a page, not an image',
    "og:image isn't sent as an image",
  ],
  'social-image-too-large': ['og:image is 5.1 MB, over 5 MB'],
  'social-image-too-small': [
    'og:image is 100 × 100 px, under 200 × 200',
    "twitter:image is 280 × 150 px, under 300 × 157 (X's large card)",
  ],
  'head-tags-in-body': [
    '1 Open Graph tag is in the body: <div id="promo"> on line 121 ended the head early',
    'title, meta description and 3 Open Graph tags are in the body, after <body> on line 30',
    'meta robots and 2 X tags are in the body: Iframe ended the head early',
  ],
  'html-structure-errors': [
    '2 <head> tags; 2 <body> tags',
    '2 <head> tags (the second on line 14); 2 <body> tags (the second on line 40)',
    '<html> after content',
    '<head> after content',
  ],
  'viewport-invalid': ['user-scalable=no; maximum-scale=1', 'no width or initial-scale', '2 viewport tags that differ'],
  'charset-invalid': [
    '<meta charset> after the first 1,024 bytes',
    'Declares utf-16le, not UTF-8',
    'The header says iso-8859-1; the page says utf-8',
    '2 declarations: utf-8, windows-1252',
  ],
  'lang-invalid': ['english'],
  'mobile-alternate': ['Mobile version: http://127.0.0.1:54385/m/plants/mobile-alternate'],
};

/** Words Simple never says bare, as patterns. Quoted site text, addresses and a single term in brackets are allowed. */
const JARGON: readonly RegExp[] = [
  /\bpx\b/,
  /\d\s?ms\b/,
  /\bH[1-6]s?\b/,
  /\bog:/,
  /\btwitter:/,
  /srcset/i,
  /[<>]/,
  /LineNumber|BytePosition/,
  /max-age/i,
  /\b(?:text|application|image|font|audio|video)\/[\w.+-]+/,
  /\bHSTS\b/,
  /\bHttpOnly\b|\bSameSite\b|\bSecure\b/,
  /X-Robots-Tag|Strict-Transport-Security|Content-Security-Policy|X-Content-Type-Options|X-Frame-Options|Referrer-Policy/,
  /\bHTTP header\b|\bheader says\b/i,
  /\bcanonical/i,
  /\bindexable\b/i,
  /\bnoindex\b|\bnofollow\b|\bnosnippet\b/,
  /\bredirect/i,
  /robots\.txt/,
  /\bmeta\b/i,
  /\btags?\b/i,
  /\battribute/i,
  /hreflang/i,
  /\bschema/i,
  /\bJSON\b/,
  /first byte|\bTTFB\b|\bDOM\b|\brender/i,
  /\bdirective/i,
  /\bURLs?\b/,
  /\bOpen Graph\b/,
  /\bcrawl/i,
  /\bstatus code\b|\b[345]xx\b/i,
  /\bHTML\b/,
  /\bbytes?\b/,
  /\binline\b/,
  /\bviewport\b|\bcharset\b/i,
  /\bhops?\b|\balternates?\b/,
  /\bGooglebot\b/,
  /\bLocation\b/,
  /\bContent-Type\b/,
  /\bISO\b/,
  /\bproperty\b/,
];

/** A number over 9,999 with no unit after it. */
const BARE_NUMBER =
  /(?<![\w.,-])(?:\d{2,3}(?:,\d{3})+|\d(?:,\d{3}){2,}|\d{5,})(?![\d,]*\s?(?:words?|characters?|pages?|links?|images?|address(?:es)?|pixels|seconds?|days?|months?|hours?|KB|MB|GB|entries|entry|sitemaps?|files?|clicks?|times))/;

/**
 * What a plain line says outside the site's own words, its addresses (a host with its port among them), a status
 * code with its name ("(307 Temporary Redirect)": docs/v2-ui-simple.md, "The rules", keeps it) and a lone term in
 * brackets ("(noindex)", "(head)").
 */
function saidInWords(line: string): string {
  return line
    .replace(/“[^”]*”?/g, '“”')
    .replace(/(?:https?:)?\/\/\S+/g, ' ')
    .replace(/(?:^|\s)[/#]\S*/g, ' ')
    .replace(/\b[\w.-]+:\d{2,5}\b/g, ' ')
    .replace(/\((?:\d{3}|HTTP \d{3})(?: [A-Za-z][A-Za-z '-]*)?\)/g, '()')
    .replace(/\((?:\w[\w-]*)\)/g, '()');
}

function jargonIn(line: string): string[] {
  const words = saidInWords(line);
  return [...JARGON, BARE_NUMBER].filter((pattern) => pattern.test(words)).map((pattern) => `${pattern}`);
}

describe('every check’s detail in plain words', () => {
  const checks = catalogChecks();

  it('reads the engine’s catalog, and has details for each of its checks and no other', () => {
    expect(checks.length).toBeGreaterThan(160);
    expect(Object.keys(DETAILS).sort()).toEqual(checks.map((check) => check.id).sort());
  });

  it.each(Object.entries(DETAILS))('says %s’s details plainly, with no jargon', (id, details) => {
    for (const detail of details) {
      const said = plainDetail({ id }, { detail });
      if (detail === null) {
        expect(said).toBeNull();
        continue;
      }
      // Every form the engine writes is translated: the engine's own text is the fallback only for one it doesn't.
      expect(said, detail).not.toBeNull();
      expect(jargonIn(said ?? ''), `${detail} → ${said}`).toEqual([]);
    }
  });

  it('leaves the engine’s detail to say a form it doesn’t know', () => {
    expect(plainDetailWords('heading-levels-skipped', 'Something new')).toBeNull();
    expect(plainDetailWords('no-such-check', '404 Not Found')).toBeNull();
    expect(plainDetail({ id: 'internal-client-error' }, { detail: '' })).toBeNull();
  });
});

describe('a check’s detail, plainly: the forms the fixit audit named', () => {
  const say = (id: string, detail: string) => plainDetail({ id }, { detail });

  it('names headings in words, and quotes the heading where the engine does', () => {
    expect(say('heading-levels-skipped', 'H1 → H3')).toBe(
      'It goes from the main heading to a level-3 heading, skipping level 2',
    );
    expect(say('heading-levels-skipped', 'H1 → H4: "Out Of Stock"')).toBe(
      'It goes from the main heading to a level-4 heading, “Out Of Stock”, skipping levels 2 and 3',
    );
    expect(say('heading-levels-skipped', 'H2 → H4')).toBe(
      'It goes from a section heading to a level-4 heading, skipping level 3',
    );
    expect(say('heading-too-long', 'H1 of 87 characters, over 70')).toBe(
      'The main heading is 87 characters long, more than 70',
    );
    expect(say('heading-too-long', 'H2 of 86 characters, over 70: "Toddington Steel Garden Bench…"')).toBe(
      'A section heading is 86 characters long, more than 70: “Toddington Steel Garden Bench…”',
    );
    expect(say('h1-multiple', '2 H1s: "Two H1s" · "A second H1 on the page"')).toBe(
      'The page has 2 main headings: “Two H1s” · “A second H1 on the page”',
    );
    expect(say('h2-missing', '1,036 words, no H2')).toBe('1,036 words with no subheadings');
  });

  it('says where the details for search engines break, counting lines from 1', () => {
    expect(
      say(
        'structured-data-invalid',
        `'"' is invalid after a value. Expected either ',', '}', or ']'. LineNumber: 0 | BytePositionInLine: 54.`,
      ),
    ).toBe(
      'The details for search engines have a mistake on line 1, at character 55: a comma or closing bracket is missing',
    );
    expect(
      say(
        'structured-data-invalid',
        `Block 2 of 3, line 1, position 55: '"' is invalid after a value. Expected either ',', '}', or ']'.`,
      ),
    ).toBe(
      'The details for search engines (block 2 of 3) have a mistake on line 1, at character 55: a comma or closing bracket is missing',
    );
  });

  it('says which image, and how big, where the engine says it', () => {
    expect(say('image-missing-src', '1 image')).toBe('1 image with no picture file');
    expect(say('image-missing-src', '1 image: alt "Product photo"')).toBe(
      '1 image with no picture file, with alt text “Product photo”',
    );
    expect(say('image-missing-src', '1 image: class "hero lazy"')).toBe(
      '1 image with no picture file, marked “hero lazy”',
    );
    expect(say('image-missing-src', '1 image, in the header')).toBe('1 image with no picture file, in the header');
    expect(say('image-missing-src', '3 images, the first: alt "Sofa"')).toBe(
      '3 images with no picture file; the first has alt text “Sofa”',
    );
    expect(say('image-inline-large', '1 inline image: 12 KB')).toBe('1 image written into the page’s code: 12 KB');
    expect(say('image-inline-large', '2 inline images, the largest 48 KB, alt "Hero banner"')).toBe(
      '2 images written into the page’s code, the largest 48 KB, with alt text “Hero banner”',
    );
    expect(say('image-no-srcset', '3 images, the widest 1,600 px')).toBe(
      '3 images with one size for every screen, the widest 1,600 pixels',
    );
  });

  it('says what share settings repeat, and how small a share image is against what sites need', () => {
    expect(say('social-tags-multiple', '2 og:title, 2 twitter:card')).toBe(
      '2 different share titles; 2 different X card types',
    );
    expect(
      say('social-tags-multiple', '2 og:title: "A" · "B"; 2 twitter:card: "summary" · "summary_large_image"'),
    ).toBe('2 different share titles: “A” · “B”; 2 different X card types: “summary” · “summary_large_image”');
    expect(say('social-image-too-small', 'og:image is 100 × 100 px, under 200 × 200')).toBe(
      'The share image is 100×100 pixels; sites need at least 200×200',
    );
    expect(say('open-graph-missing', 'Empty og:title, og:description')).toBe(
      'For sharing, its title and description are empty',
    );
    expect(say('open-graph-missing', 'Missing og:title, og:description, og:image')).toBe(
      'For sharing, it has no title, description or image',
    );
  });

  it('says where the page’s basic parts repeat', () => {
    expect(say('html-structure-errors', '2 <head> tags; 2 <body> tags')).toBe(
      'The page’s code has 2 top sections (head); 2 main sections (body)',
    );
    expect(
      say('html-structure-errors', '2 <head> tags (the second on line 14); 2 <body> tags (the second on line 40)'),
    ).toBe(
      'The page’s code has 2 top sections (head), the second on line 14; 2 main sections (body), the second on line 40',
    );
  });

  it('says times in seconds, sizes in KB and MB, and scores with what they mean', () => {
    expect(say('slow-response', '1,200 ms, over 1,000 ms (first byte after 1,200 ms)')).toBe(
      'It took 1.2 seconds to open, more than 1 second',
    );
    expect(say('slow-response', '3,480 ms, over 1,000 ms (first byte after 2,950 ms)')).toBe(
      'It took 3.5 seconds to open, more than 1 second; it began to answer after 3 seconds',
    );
    expect(say('readability-low', 'Reading ease -171, under 30 (very difficult)')).toBe(
      'Very hard to read: it scores -171 for reading ease, where under 30 is very hard',
    );
    expect(say('resource-past-crawl-limit', '2.1 MB, past the 2 MB Google reads')).toBe(
      'The file is 2.1 MB; Google reads only the first 2 MB',
    );
    expect(
      say(
        'charset-missing',
        'A meta tag declares it, but ends at byte 6,158, past the first 1,024 bytes browsers read',
      ),
    ).toBe('The page names its text encoding too far down its code, past where browsers look');
  });

  it('says security settings, cookies and forwarding in words', () => {
    expect(say('security-headers-weak', 'HSTS max-age 7889238 (91 days), under a year')).toBe(
      'It tells browsers to use the secure connection for 3 months; a year is advised',
    );
    expect(
      say('security-headers-weak', 'HSTS max-age 3600 (1 hour), under a year; X-Content-Type-Options "none"'),
    ).toBe(
      'It tells browsers to use the secure connection for 1 hour; a year is advised; the setting that stops browsers guessing file types is set wrongly (“none”)',
    );
    expect(say('security-headers-missing', 'Missing Strict-Transport-Security, Content-Security-Policy')).toBe(
      'Your site doesn’t send the security settings that keep browsers on the secure connection and limit what the pages may load',
    );
    expect(say('cookies-insecure', 'localization: no Secure; cart_currency: no Secure')).toBe(
      'The cookies “localization” and “cart_currency” can be sent without the secure connection',
    );
    expect(say('cookies-insecure', 'sessionid: no HttpOnly')).toBe(
      'The page’s scripts can read the cookie “sessionid”, which looks like a login',
    );
    expect(say('redirect-loop', '2 hops: /plants/loop-a (301) → /plants/loop-b (301) → back to /plants/loop-a')).toBe(
      'It forwards round in a loop: /plants/loop-a, then /plants/loop-b, then back to /plants/loop-a',
    );
    expect(say('internal-client-error', '404 Not Found')).toBe('It is missing (404 Not Found)');
    expect(say('internal-unreachable', 'Connection failed')).toBe('It couldn’t be reached');
    expect(say('content-type-mismatch', 'Used as a script, sent as text/plain')).toBe(
      'Pages use it as a script, but the server says it’s plain text',
    );
  });

  it('says how the X1-X7 checks’ details read: unlinked pages, depths and linking pages', () => {
    expect(say('canonical-unlinked', 'Only canonicals lead here')).toBe(
      'Other pages name it as their main address, but none links to it',
    );
    expect(say('hreflang-unlinked', 'No link or sitemap entry leads here')).toBe(
      'Other pages name it as a language version, but no link or sitemap leads here',
    );
    expect(say('deep-page', '5 clicks from the start, over 4')).toBe(
      '5 clicks from the home page; at most 4 is advised',
    );
    expect(say('noindex-near-home', '1 click from the start')).toBe('1 click from the home page');
  });
});
