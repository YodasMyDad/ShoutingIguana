// Step 4: the URL inspector fixtures, <out>/garden/inspector/<slug>.json, for six representative URLs of the analysed
// garden copy: everything the IA's inspector tabs show (summary, issues, inlinks, outlinks, redirects, headers,
// structured data, hreflang, source).
import { rmSync } from 'node:fs';
import { join } from 'node:path';
import {
    INDEXABILITY_REASON, LINK_KIND, LINK_POSITION, OUTCOME, SKIP_REASON, decodeDetails, decodeHtml, indexabilitySql, linkFlags,
    openCrawl, statusClass, structuredDataTypes
} from './lib/crawl-db.mjs';
import { writeJson } from './lib/json.mjs';
import { crawlCopy, settings as readSettings } from './lib/options.mjs';
import { createUrlRows } from './lib/url-rows.mjs';

const LINK_ROWS = 100;
const IMAGE_ROWS = 100;
const MAX_SOURCE_BYTES = 300 * 1024;
const SITE = 'https://www.gardenfurnituresets.co.uk';

/** Picked from the garden crawl: see the README for why each one. The home page is the crawl's start URL. */
const PICKS = [
    { slug: 'home', role: 'Home page', url: null },
    { slug: 'category', role: 'Category page, with a link to a 404', url: `${SITE}/rattan-furniture/rattan-fire-pit-sets/` },
    { slug: 'product', role: 'Product page', url: `${SITE}/harpswell-metal-patio-set/` },
    { slug: 'redirect', role: 'Redirect: an external short link, two hops (the crawl has no internal redirects)', url: 'https://amzn.to/44j8xD6' },
    { slug: 'client-error', role: '4xx: an internal 404, linked from the category page', url: `${SITE}/outdoor-pe-rattan-dining-set-with-fire-pit-table/` },
    { slug: 'non-indexable', role: 'Non-indexable page: canonicalised to another URL', url: `${SITE}/blog/?p=2` }
];

const camel = text => (text ? text[0].toLowerCase() + text.slice(1) : text);
const settings = readSettings();
const db = openCrawl(crawlCopy(settings, 'garden'));
const rows = createUrlRows(db);
const { join: indexabilityJoin, reason } = indexabilitySql(db);

const statements = {
    id: db.prepare('SELECT id FROM urls WHERE url = ?'),
    startUrl: db.prepare('SELECT start_url FROM crawl WHERE id = 1'),
    issues: db.prepare(`
        SELECT i.issue, t.title, t.severity, t.category, i.detail, ru.url AS related_url
        FROM issues i JOIN issue_types t ON t.id = i.issue LEFT JOIN urls ru ON ru.id = i.related_url_id
        WHERE i.url_id = ?
        ORDER BY CASE t.severity WHEN 'Error' THEN 0 WHEN 'Warning' THEN 1 ELSE 2 END, t.title`),
    relatedIssues: db.prepare(`
        SELECT i.issue, t.title, t.severity, u.url, i.detail
        FROM issues i JOIN issue_types t ON t.id = i.issue JOIN urls u ON u.id = i.url_id
        WHERE i.related_url_id = ? AND i.url_id <> ?
        ORDER BY i.issue, i.url_id`),
    inlinkTotals: db.prepare('SELECT kind, COUNT(*) AS n, COUNT(DISTINCT from_id) AS pages FROM links WHERE to_id = ? AND from_id <> to_id GROUP BY kind'),
    inlinkUnique: db.prepare('SELECT COUNT(DISTINCT from_id) AS n FROM links WHERE to_id = ? AND from_id <> to_id'),
    inlinks: db.prepare(`
        SELECT l.from_id, f.url AS from_url, l.kind, l.flags, l.position, l.text, l.fragment,
               r.status_code, r.outcome, ${reason} AS reason
        FROM links l JOIN urls f ON f.id = l.from_id LEFT JOIN responses r ON r.url_id = l.from_id ${indexabilityJoin}
        WHERE l.to_id = ? AND l.from_id <> l.to_id
        ORDER BY l.from_id, l.rowid LIMIT ${LINK_ROWS}`),
    outlinkTotals: db.prepare(`
        SELECT l.kind, COUNT(*) AS n, COUNT(DISTINCT l.to_id) AS targets, COALESCE(SUM(t.is_internal = 0), 0) AS external
        FROM links l JOIN urls t ON t.id = l.to_id WHERE l.from_id = ? AND l.to_id <> l.from_id GROUP BY l.kind`),
    outlinkUnique: db.prepare('SELECT COUNT(DISTINCT to_id) AS n FROM links WHERE from_id = ? AND to_id <> from_id'),
    selfLinks: db.prepare('SELECT COUNT(*) AS n FROM links WHERE from_id = ? AND to_id = from_id'),
    outlinks: db.prepare(`
        SELECT l.to_id, t.url AS to_url, t.is_internal, t.state, t.skip_reason, l.kind, l.flags, l.position, l.text, l.fragment,
               r.status_code, r.outcome, ${reason} AS reason
        FROM links l JOIN urls t ON t.id = l.to_id LEFT JOIN responses r ON r.url_id = l.to_id ${indexabilityJoin}
        WHERE l.from_id = ? AND l.to_id <> l.from_id
        ORDER BY l.rowid LIMIT ${LINK_ROWS}`),
    redirectsHere: db.prepare(`
        SELECT u.id, u.url, r.status_code FROM responses r JOIN urls u ON u.id = r.url_id WHERE r.redirect_url_id = ? ORDER BY u.id`),
    details: db.prepare('SELECT data FROM response_details WHERE url_id = ?'),
    html: db.prepare('SELECT html FROM response_html WHERE url_id = ?'),
    target: db.prepare(`
        SELECT u.id, r.status_code, r.outcome FROM urls u LEFT JOIN responses r ON r.url_id = u.id WHERE u.url = ?`)
};

function byKind(totals, field = 'n') {
    return Object.fromEntries(totals.map(total => [LINK_KIND[total.kind], total[field]]));
}

function inlinks(id) {
    const totals = statements.inlinkTotals.all(id);
    return {
        total: totals.reduce((sum, total) => sum + total.n, 0),
        uniqueUrls: statements.inlinkUnique.get(id).n,
        hyperlinks: totals.find(total => total.kind === 0)?.n ?? 0,
        byKind: byKind(totals),
        rows: statements.inlinks.all(id).map(link => ({
            fromId: link.from_id,
            fromUrl: link.from_url,
            kind: LINK_KIND[link.kind],
            anchorText: link.text ?? null,
            position: LINK_POSITION[link.position],
            flags: linkFlags(link.flags),
            fragment: link.fragment ?? null,
            fromStatusCode: link.status_code ?? null,
            fromIndexable: link.reason == null ? null : link.reason === 0
        }))
    };
}

function outlinks(id) {
    const totals = statements.outlinkTotals.all(id);
    return {
        total: totals.reduce((sum, total) => sum + total.n, 0),
        uniqueUrls: statements.outlinkUnique.get(id).n,
        hyperlinks: totals.find(total => total.kind === 0)?.n ?? 0,
        external: totals.reduce((sum, total) => sum + total.external, 0),
        linksToItself: statements.selfLinks.get(id).n,
        byKind: byKind(totals),
        rows: statements.outlinks.all(id).map(link => ({
            toId: link.to_id,
            toUrl: link.to_url,
            isInternal: link.is_internal === 1,
            kind: LINK_KIND[link.kind],
            anchorText: link.text ?? null,
            position: LINK_POSITION[link.position],
            flags: linkFlags(link.flags),
            fragment: link.fragment ?? null,
            fetched: link.outcome != null,
            statusCode: link.status_code ?? null,
            statusClass: link.outcome == null ? null : statusClass(link.status_code),
            outcome: link.outcome == null ? null : OUTCOME[link.outcome],
            indexable: link.reason == null ? null : link.reason === 0,
            indexabilityReason: link.reason == null ? null : INDEXABILITY_REASON[link.reason],
            notFetchedBecause: link.outcome == null && link.skip_reason ? SKIP_REASON[link.skip_reason] : null
        }))
    };
}

function source(id) {
    const html = decodeHtml(statements.html.get(id)?.html);
    if (html == null) {
        return { stored: false, bytes: null, truncated: false, html: null };
    }

    const bytes = Buffer.from(html, 'utf8');
    const truncated = bytes.length > MAX_SOURCE_BYTES;
    // Cut on a character boundary: drop a partial character the byte cut leaves at the end.
    const kept = truncated ? bytes.subarray(0, MAX_SOURCE_BYTES).toString('utf8').replace(/�$/, '') : html;
    return { stored: true, bytes: bytes.length, truncated, keptBytes: Buffer.byteLength(kept), html: kept };
}

function structuredData(facts) {
    return (facts?.jsonLd ?? []).map((raw, index) => {
        try {
            const json = JSON.parse(raw);
            return { index, valid: true, types: structuredDataTypes([raw]), json, raw };
        } catch (error) {
            return { index, valid: false, types: [], json: null, error: error.message, raw };
        }
    });
}

function hreflang(facts) {
    return (facts?.hreflangs ?? []).map(entry => {
        const target = entry.url ? statements.target.get(entry.url) : undefined;
        return {
            language: entry.language,
            href: entry.href,
            url: entry.url ?? null,
            inHead: entry.inHead,
            fromHttpHeader: entry.fromHttpHeader,
            targetFetched: target?.outcome != null,
            targetStatusCode: target?.status_code ?? null
        };
    });
}

function pageFacts(facts) {
    if (!facts) {
        return null;
    }

    const images = facts.images ?? [];
    return {
        titles: facts.titles ?? [],
        metaDescriptions: facts.metaDescriptions ?? [],
        metaTags: facts.metaTags ?? [],
        canonicals: facts.canonicals ?? [],
        robots: facts.robots ?? null,
        metaRefresh: facts.metaRefresh ?? null,
        language: facts.language ?? null,
        declaredCharset: facts.declaredCharset ?? null,
        baseHref: facts.baseHref ?? null,
        wordCount: facts.wordCount ?? null,
        headings: facts.headings ?? [],
        images: { total: images.length, missingAlt: images.filter(image => !image.hasAlt).length, rows: images.slice(0, IMAGE_ROWS) },
        anchorIdCount: (facts.anchorIds ?? []).length
    };
}

const outDir = join(settings.out, 'garden', 'inspector');
rmSync(outDir, { recursive: true, force: true });
const startUrl = statements.startUrl.get().start_url;
const index = [];
for (const pick of PICKS) {
    const url = pick.url ?? startUrl;
    const id = statements.id.get(url)?.id;
    const summary = id == null ? null : rows.row(id);
    if (!summary) {
        throw new Error(`${pick.slug}: ${url} isn't a fetched URL of this crawl.`);
    }

    const details = decodeDetails(statements.details.get(id)?.data);
    const facts = summary.wordCount != null ? details?.page ?? null : null;
    const issues = statements.issues.all(id).map(issue => ({
        checkId: issue.issue,
        title: issue.title,
        severity: camel(issue.severity),
        category: camel(issue.category),
        detail: issue.detail ?? null,
        relatedUrl: issue.related_url ?? null
    }));
    const related = statements.relatedIssues.all(id, id);
    const chain = summary.redirectHops > 0 ? rows.redirectChain(id) : null;

    const inspector = {
        slug: pick.slug,
        role: pick.role,
        crawl: { startUrl },
        summary,
        issues: { total: issues.length, rows: issues },
        issuesNamingThisUrl: {
            total: related.length,
            rows: related.slice(0, LINK_ROWS).map(issue => ({
                checkId: issue.issue, title: issue.title, severity: camel(issue.severity), url: issue.url, detail: issue.detail ?? null
            }))
        },
        inlinks: inlinks(id),
        outlinks: outlinks(id),
        redirects: {
            chain: chain && chain.map(({ id: hopId, url: hopUrl, fetched, statusCode, outcome, via, loop }, hop) => ({
                hop, id: hopId, url: hopUrl, fetched, statusCode, outcome, redirectsVia: via, loop
            })),
            redirectsHere: statements.redirectsHere.all(id).map(row => ({ id: row.id, url: row.url, statusCode: row.status_code ?? null }))
        },
        headers: details ? { stored: true, rows: details.headers ?? [] } : { stored: false, rows: [] },
        page: pageFacts(facts),
        structuredData: structuredData(facts),
        hreflang: hreflang(facts),
        source: source(id)
    };

    const size = writeJson(join(outDir, `${pick.slug}.json`), inspector);
    index.push({ slug: pick.slug, role: pick.role, id, url, file: `${pick.slug}.json`, bytes: size });
    console.log(`${pick.slug}: ${url} -> ${size.toLocaleString('en-GB')} bytes (issues ${issues.length}, inlinks ${inspector.inlinks.total}, outlinks ${inspector.outlinks.total}, source ${inspector.source.bytes ?? 'none'}${inspector.source.truncated ? ', truncated' : ''})`);
}

writeJson(join(outDir, 'index.json'), index);
db.close();
