// Reading a .sicrawl copy with node:sqlite. Enum names, blob formats and the engine's own judgements are mirrored from
// engine/src/ShoutingIguana.Engine (file named next to each), so the fixtures say what the engine would.
import { DatabaseSync } from 'node:sqlite';
import { brotliDecompressSync } from 'node:zlib';

/** Opens a crawl copy read-only. Journal mode is DELETE, so reading leaves no -wal or -shm behind. */
export function openCrawl(path) {
    return new DatabaseSync(path, { readOnly: true });
}

export function hasTable(db, name) {
    return db.prepare("SELECT 1 AS found FROM sqlite_master WHERE type = 'table' AND name = ?").get(name) !== undefined;
}

// Stored as their numeric values; named as the sidecar names enums (camelCase).
/** Fetching/Models/FetchOutcome.cs */
export const OUTCOME = ['success', 'redirect', 'clientError', 'serverError', 'blocked', 'robotsDisallowed', 'timeout', 'networkError', 'processingError'];
/** Parsing/Models/IndexabilityReason.cs */
export const INDEXABILITY_REASON = ['none', 'noindex', 'canonicalised', 'redirected', 'clientError', 'serverError', 'blockedByRobots', 'blocked', 'unreachable'];
/** Parsing/Models/LinkKind.cs */
export const LINK_KIND = ['hyperlink', 'image', 'script', 'stylesheet', 'iframe', 'media', 'icon', 'canonical', 'hreflang', 'pagination', 'metaRefresh', 'redirect'];
/** Parsing/Models/LinkPosition.cs */
export const LINK_POSITION = ['content', 'navigation', 'header', 'footer', 'sidebar', 'head'];
/** Parsing/Models/LinkFlags.cs */
export const LINK_FLAGS = { noFollow: 1, sponsored: 2, ugc: 4, imageLink: 8, outsideHead: 16, samePage: 32, fromHttpHeader: 64 };
/** Storage/Models/DiscoverySource.cs */
export const SOURCE = ['seed', 'link', 'sitemap', 'siteVersion'];
/** Storage/Models/FetchMode.cs */
export const FETCH_MODE = ['crawl', 'check'];
/** Storage/Models/UrlState.cs */
export const URL_STATE = ['queued', 'fetching', 'done', 'skipped'];
/** Storage/Models/SkipReason.cs */
export const SKIP_REASON = ['none', 'noFollow', 'notFollowedByOption', 'resourcesDisabled', 'externalsDisabled', 'depthLimit', 'urlLimit'];
/** Crawling/Models/CrawlStatus.cs */
export const CRAWL_STATUS = ['running', 'paused', 'stopped', 'completed', 'failed'];
/** Crawling/CrawlDepth.cs: the depth of URLs known only from sitemaps. */
export const UNLINKED_DEPTH = 1_000_000;

export const FETCH = Object.fromEntries(OUTCOME.map((name, value) => [name, value]));
export const KIND = Object.fromEntries(LINK_KIND.map((name, value) => [name, value]));
export const REASON = Object.fromEntries(INDEXABILITY_REASON.map((name, value) => [name, value]));

export function linkFlags(value) {
    return Object.entries(LINK_FLAGS).filter(([, bit]) => (value & bit) !== 0).map(([name]) => name);
}

/** Serialization/BlobCodec.cs: Brotli. The details are camelCase JSON: { headers: [{ name, value }], page?: PageFacts }. */
export function decodeDetails(blob) {
    return blob == null ? null : JSON.parse(brotliDecompressSync(blob).toString('utf8'));
}

export function decodeHtml(blob) {
    return blob == null ? null : brotliDecompressSync(blob).toString('utf8');
}

/**
 * Indexability as the analysis judged it (Storage/CrawlStatsReader.cs): effective_indexability holds the analysis's
 * verdict where it differs from the crawl's (an instant meta refresh is a redirect; a canonical that leads back or to a
 * broken URL is ignored).
 */
export function indexabilitySql(db) {
    return hasTable(db, 'effective_indexability')
        ? { join: 'LEFT JOIN effective_indexability e ON e.url_id = r.url_id', reason: 'COALESCE(e.indexability_reason, r.indexability_reason)' }
        : { join: '', reason: 'r.indexability_reason' };
}

/** The library's and si stats' page: an internal HTML page fetched successfully and parsed (Storage/CrawlSummaryReader.cs). */
export const PAGE_SQL = 'u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL';

export function statusClass(statusCode) {
    return statusCode == null ? 'noResponse' : `${Math.floor(statusCode / 100)}xx`;
}

export const iso = ms => (ms == null ? null : new Date(Number(ms)).toISOString());

// Analysis/ResourceTypes.cs, mirrored: files (images, video, audio, fonts, stylesheets, scripts) against pages. A fetched
// URL is judged by its content type, one that wasn't fetched successfully by the extension of its path's last segment.
const RESOURCE_EXTENSIONS = new Set([
    'jpg', 'jpeg', 'png', 'gif', 'webp', 'avif', 'svg', 'bmp', 'ico', 'tif', 'tiff', 'heic', 'heif', 'jxl',
    'mp4', 'm4v', 'webm', 'mov', 'avi', 'mkv', 'ogv', 'mp3', 'm4a', 'wav', 'ogg', 'oga', 'opus', 'aac', 'flac',
    'woff', 'woff2', 'ttf', 'otf', 'eot', 'css', 'js', 'mjs'
]);
const MEDIA_TYPE_PREFIXES = ['image/', 'video/', 'audio/', 'font/'];
const MEDIA_TYPES = new Set([
    'text/css', 'text/javascript', 'application/javascript', 'application/x-javascript', 'application/ecmascript',
    'application/font-woff', 'application/x-font-woff', 'application/x-font-ttf', 'application/x-font-otf',
    'application/vnd.ms-fontobject'
]);

function mediaType(contentType) {
    const semicolon = contentType.indexOf(';');
    return (semicolon < 0 ? contentType : contentType.slice(0, semicolon)).trim().toLowerCase();
}

function extension(url) {
    const end = url.search(/[?#]/);
    const path = end >= 0 ? url.slice(0, end) : url;
    const segment = path.slice(path.lastIndexOf('/') + 1);
    const dot = segment.lastIndexOf('.');
    return dot >= 0 ? segment.slice(dot + 1).toLowerCase() : null;
}

/** @param successContentType The Content-Type when the fetch succeeded, otherwise null (as the engine passes it). */
export function isResource(url, successContentType) {
    if (successContentType == null) {
        return RESOURCE_EXTENSIONS.has(extension(url));
    }

    const type = mediaType(successContentType);
    return MEDIA_TYPE_PREFIXES.some(prefix => type.startsWith(prefix)) || MEDIA_TYPES.has(type);
}

const TYPE_BY_EXTENSION = {
    image: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'avif', 'svg', 'bmp', 'ico', 'tif', 'tiff', 'heic', 'heif', 'jxl'],
    video: ['mp4', 'm4v', 'webm', 'mov', 'avi', 'mkv', 'ogv'],
    audio: ['mp3', 'm4a', 'wav', 'ogg', 'oga', 'opus', 'aac', 'flac'],
    font: ['woff', 'woff2', 'ttf', 'otf', 'eot'],
    css: ['css'],
    javascript: ['js', 'mjs'],
    pdf: ['pdf'],
    xml: ['xml']
};

function typeOfMedia(type) {
    if (type === 'text/html' || type === 'application/xhtml+xml') return 'html';
    if (type.startsWith('image/')) return 'image';
    if (type === 'text/css') return 'css';
    if (type.includes('javascript') || type.includes('ecmascript')) return 'javascript';
    if (type.startsWith('font/') || type.includes('font')) return 'font';
    if (type.startsWith('video/')) return 'video';
    if (type.startsWith('audio/')) return 'audio';
    if (type === 'application/pdf') return 'pdf';
    if (type.includes('xml')) return 'xml';
    if (type.includes('json')) return 'json';
    if (type.startsWith('text/')) return 'text';
    return 'other';
}

/**
 * A display type for the grid's Type column (derived here, not by the engine): the content type of a successful fetch,
 * else the path's extension, else the content type of the error response (a 404 page is text/html), else null.
 */
export function fileType(url, outcome, contentType) {
    if (outcome === FETCH.success && contentType) {
        return typeOfMedia(mediaType(contentType));
    }

    const ext = extension(url);
    const byExtension = Object.entries(TYPE_BY_EXTENSION).find(([, extensions]) => extensions.includes(ext));
    if (byExtension) {
        return byExtension[0];
    }

    return contentType ? typeOfMedia(mediaType(contentType)) : null;
}

const graphemes = new Intl.Segmenter('en', { granularity: 'grapheme' });

/**
 * Analysis/Rules/DisplayWidth.cs, the character half: user-perceived characters after trimming. The engine judges
 * titles and descriptions by display width, which equals this for Latin text (wide CJK characters and emoji count 2).
 */
export function textLength(text) {
    if (text == null) {
        return null;
    }

    let count = 0;
    for (const _ of graphemes.segment(text.trim())) {
        count++;
    }

    return count;
}

/** The @type values in JSON-LD blocks, @graph items included. Blocks that don't parse are skipped. */
export function structuredDataTypes(jsonLd) {
    const types = new Set();
    const visit = node => {
        if (Array.isArray(node)) {
            node.forEach(visit);
        } else if (node && typeof node === 'object') {
            const type = node['@type'];
            (Array.isArray(type) ? type : type ? [type] : []).forEach(name => types.add(String(name)));
            if (node['@graph']) {
                visit(node['@graph']);
            }
        }
    };
    for (const block of jsonLd ?? []) {
        try {
            visit(JSON.parse(block));
        } catch {
            // Invalid JSON-LD has its own check; it has no types to list.
        }
    }

    return [...types];
}

// Parsing/RobotsMetaParser.cs, mirrored for the one question asked here: does a meta tag or header say noindex?
const VALUED_DIRECTIVES = new Set(['max-snippet', 'max-image-preview', 'max-video-preview', 'unavailable_after']);
const KNOWN_DIRECTIVES = new Set(['all', 'none', 'index', 'noindex', 'follow', 'nofollow', 'noarchive', 'nocache', 'nosnippet',
    'noimageindex', 'notranslate', 'indexifembedded', 'noodp', 'noydir', ...VALUED_DIRECTIVES]);

function directivesOf(part) {
    const lower = part.toLowerCase();
    const words = lower.split(/\s+/).filter(Boolean);
    return words.length > 1 && words.every(word => KNOWN_DIRECTIVES.has(word.split(':')[0])) ? words : [lower];
}

function metaSaysNoindex(content) {
    const directives = content.split(',').map(part => part.trim()).filter(Boolean).flatMap(directivesOf);
    return directives.includes('noindex') || directives.includes('none');
}

function headerSaysNoindex(value, robotsToken) {
    const directives = [];
    let applies = true;
    for (const raw of value.split(',')) {
        const part = raw.trim();
        if (!part) continue;
        let directive = part;
        const colon = part.indexOf(':');
        if (colon > 0) {
            const prefix = part.slice(0, colon).trim().toLowerCase();
            if (!VALUED_DIRECTIVES.has(prefix) && !prefix.includes(' ')) {
                applies = prefix === 'googlebot' || prefix === robotsToken.toLowerCase();
                directive = part.slice(colon + 1).trim();
            }
        }

        if (applies && directive) {
            directives.push(...directivesOf(directive));
        }
    }

    return directives.includes('noindex') || directives.includes('none');
}

/**
 * Where a noindex came from: "meta", "header" (X-Robots-Tag) or "both". Derived here; the engine stores only that the
 * page is noindex. Meta tags named robots, googlebot or the crawler's robots token apply, as in the engine.
 */
export function noindexSource(facts, headers, robotsToken = 'ShoutingIguana') {
    const applies = name => name === 'robots' || name === 'googlebot' || name === robotsToken.toLowerCase();
    const meta = (facts?.metaTags ?? []).some(tag => applies(tag.name) && metaSaysNoindex(tag.content));
    const header = (headers ?? []).some(header => header.name === 'x-robots-tag' && headerSaysNoindex(header.value, robotsToken));
    return meta && header ? 'both' : meta ? 'meta' : header ? 'header' : null;
}
