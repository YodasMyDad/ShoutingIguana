// One grid row per fetched URL: every column the crawl file can fill. Field names are camelCase, as the sidecar's JSON.
import {
    FETCH, FETCH_MODE, INDEXABILITY_REASON, KIND, LINK_KIND, OUTCOME, PAGE_SQL, REASON, SOURCE, UNLINKED_DEPTH,
    decodeDetails, fileType, hasTable, indexabilitySql, isResource, iso, noindexSource, statusClass, structuredDataTypes, textLength
} from './crawl-db.mjs';

const MAX_REDIRECT_HOPS = 20;

export function createUrlRows(db) {
    const { join, reason } = indexabilitySql(db);
    const analysed = hasTable(db, 'issues');

    const select = `
        SELECT u.id, u.url, u.is_internal, u.fetch_mode, u.depth, u.source, u.found_via, fo.url AS found_on,
               r.fetched_at, r.outcome, r.status_code, r.content_type, r.size_bytes, r.body_bytes, r.ttfb_ms, r.response_ms,
               r.http_version, r.redirect_url_id, ru.url AS redirect_url, r.block_reason, r.error, r.is_html,
               r.title, r.title_count, r.meta_description, r.meta_description_count, r.h1, r.h1_count,
               cu.url AS canonical_url, r.canonical_url_id, r.is_noindex, r.is_nofollow,
               ${reason} AS reason, r.word_count, r.language,
               (${PAGE_SQL}) AS is_page, d.data AS details
        FROM responses r
        JOIN urls u ON u.id = r.url_id
        ${join}
        LEFT JOIN urls fo ON fo.id = u.found_on_id
        LEFT JOIN urls ru ON ru.id = r.redirect_url_id
        LEFT JOIN urls cu ON cu.id = r.canonical_url_id
        LEFT JOIN response_details d ON d.url_id = r.url_id`;

    const byId = db.prepare(`${select} WHERE r.url_id = ?`);
    const firstByIdStatement = limit => db.prepare(`${select} ORDER BY r.url_id LIMIT ${limit}`);

    const hyperlinkInlinks = db.prepare(`
        SELECT COUNT(*) AS n, COUNT(DISTINCT from_id) AS u FROM links WHERE to_id = ? AND kind = ${KIND.hyperlink} AND from_id <> to_id`);
    const allInlinks = db.prepare('SELECT COUNT(*) AS n FROM links WHERE to_id = ? AND from_id <> to_id');
    const hyperlinkOutlinks = db.prepare(`
        SELECT COUNT(*) AS n, COUNT(DISTINCT l.to_id) AS u, COALESCE(SUM(t.is_internal = 0), 0) AS external
        FROM links l JOIN urls t ON t.id = l.to_id WHERE l.from_id = ? AND l.kind = ${KIND.hyperlink} AND l.to_id <> l.from_id`);
    const allOutlinks = db.prepare('SELECT COUNT(*) AS n FROM links WHERE from_id = ? AND to_id <> from_id');
    const hop = db.prepare(`
        SELECT u.url, r.outcome, r.status_code, r.redirect_url_id, ${reason} AS reason,
               (SELECT l.to_id FROM links l WHERE l.from_id = r.url_id AND l.kind = ${KIND.metaRefresh} AND l.to_id <> l.from_id ORDER BY l.rowid LIMIT 1) AS refresh_id
        FROM urls u LEFT JOIN responses r ON r.url_id = u.id ${join.replace('r.url_id', 'u.id')} WHERE u.id = ?`);
    const issuesOf = analysed ? db.prepare('SELECT issue FROM issues WHERE url_id = ? ORDER BY issue') : null;
    const sitemapEntries = new Set(db.prepare('SELECT DISTINCT url_id FROM sitemap_urls').all().map(row => row.url_id));

    /** Follows HTTP redirects, and instant meta refreshes the analysis treats as redirects, hop by hop. */
    function redirectChain(id) {
        const hops = [];
        const seen = new Set();
        let current = id;
        while (current != null && hops.length <= MAX_REDIRECT_HOPS) {
            const row = hop.get(current);
            const loop = seen.has(current);
            seen.add(current);
            const isMetaRefresh = row.redirect_url_id == null && row.reason === REASON.redirected && row.refresh_id != null;
            const next = row.redirect_url_id ?? (isMetaRefresh ? row.refresh_id : null);
            hops.push({
                id: current,
                url: row.url,
                fetched: row.outcome != null,
                statusCode: row.status_code ?? null,
                outcome: row.outcome == null ? null : OUTCOME[row.outcome],
                via: next == null ? null : isMetaRefresh ? 'metaRefresh' : 'http',
                loop
            });
            if (loop) {
                break;
            }

            current = next;
        }

        return hops;
    }

    function toRow(r) {
        const details = decodeDetails(r.details);
        const facts = details?.page ?? null;
        const headings = facts?.headings ?? [];
        const h2s = headings.filter(heading => heading.level === 2);
        const images = facts?.images ?? [];
        const successType = r.outcome === FETCH.success ? r.content_type : null;
        const chain = r.redirect_url_id != null || r.reason === REASON.redirected ? redirectChain(r.id) : null;
        const last = chain?.at(-1);
        const inHyper = hyperlinkInlinks.get(r.id);
        const inAll = allInlinks.get(r.id);
        const outHyper = hyperlinkOutlinks.get(r.id);
        const outAll = allOutlinks.get(r.id);
        const issues = issuesOf ? issuesOf.all(r.id).map(row => row.issue) : null;

        // Title, description, headings and content exist only for HTML whose body was parsed (word_count is set then).
        // Other internal URLs carry facts read from their headers alone, such as X-Robots-Tag.
        const parsed = r.word_count != null;
        return {
            id: r.id,
            url: r.url,
            finalUrl: last ? last.url : null,
            redirectUrl: r.redirect_url ?? null,
            redirectHops: chain ? chain.length - 1 : 0,
            finalStatusCode: last ? last.statusCode : null,
            isInternal: r.is_internal === 1,
            kind: isResource(r.url, successType) ? 'resource' : 'page',
            type: fileType(r.url, r.outcome, r.content_type),
            isPage: r.is_page === 1,
            fetchMode: FETCH_MODE[r.fetch_mode],
            statusCode: r.status_code ?? null,
            statusClass: statusClass(r.status_code),
            outcome: OUTCOME[r.outcome],
            contentType: r.content_type ?? null,
            depth: r.depth >= UNLINKED_DEPTH ? null : r.depth,
            responseMs: r.response_ms,
            ttfbMs: r.ttfb_ms,
            sizeBytes: r.size_bytes ?? null,
            bodyBytes: r.body_bytes ?? null,
            httpVersion: r.http_version ?? null,
            title: r.title ?? null,
            titleLength: textLength(r.title),
            titleCount: parsed ? r.title_count : null,
            metaDescription: r.meta_description ?? null,
            metaDescriptionLength: textLength(r.meta_description),
            metaDescriptionCount: parsed ? r.meta_description_count : null,
            h1: r.h1 ?? null,
            h1Count: parsed ? r.h1_count : null,
            h2: h2s[0]?.text ?? null,
            h2Count: parsed ? h2s.length : null,
            wordCount: r.word_count ?? null,
            canonicalUrl: r.canonical_url ?? null,
            canonicalStatus: r.canonical_url == null ? 'none' : r.canonical_url_id === r.id ? 'self' : 'other',
            indexable: r.reason === REASON.none,
            indexabilityReason: INDEXABILITY_REASON[r.reason],
            noindexSource: r.reason === REASON.noindex ? noindexSource(facts, details?.headers) : null,
            isNoindex: r.is_noindex === 1,
            isNofollow: r.is_nofollow === 1,
            robotsDirectives: facts ? facts.robots?.directives ?? [] : null,
            language: r.language ?? null,
            inlinks: inHyper.n,
            uniqueInlinks: inHyper.u,
            allInlinks: inAll.n,
            outlinks: outHyper.n,
            uniqueOutlinks: outHyper.u,
            externalOutlinks: outHyper.external,
            allOutlinks: outAll.n,
            imageCount: parsed && facts ? images.length : null,
            imagesMissingAlt: parsed && facts ? images.filter(image => !image.hasAlt).length : null,
            structuredDataTypes: parsed && facts ? structuredDataTypes(facts.jsonLd) : null,
            hreflangCount: parsed && facts ? (facts.hreflangs ?? []).length : null,
            issueCount: issues ? issues.length : null,
            issues,
            inSitemap: sitemapEntries.has(r.id),
            source: SOURCE[r.source],
            foundOn: r.found_on ?? null,
            foundVia: r.found_via == null ? null : LINK_KIND[r.found_via],
            blockReason: r.block_reason ?? null,
            error: r.error ?? null,
            crawledAt: iso(r.fetched_at)
        };
    }

    return {
        row: id => {
            const r = byId.get(id);
            return r ? toRow(r) : null;
        },
        /** Fetched URLs in id order (crawl discovery order), up to limit. */
        rows: (limit = -1) => firstByIdStatement(limit).all().map(toRow),
        redirectChain
    };
}
