namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Queries that read a crawl's tables for comparison, leaving out what depends on timing or on discovery order: row
/// ids, timings, and where a URL happened to be found first.
/// </summary>
internal static class CrawlTables
{
    public const string URLS = "SELECT url, is_internal, fetch_mode, depth, state, skip_reason FROM urls";

    public const string RESPONSES = """
        SELECT u.url, r.outcome, r.status_code, r.content_type, r.size_bytes, r.body_bytes, target.url, r.block_reason,
               r.is_html, r.title, r.title_count, r.meta_description, r.meta_description_count, r.h1, r.h1_count,
               canonical.url, r.is_noindex, r.is_nofollow, r.indexability, r.indexability_reason, r.word_count,
               r.content_hash, r.simhash, r.simhash_1, r.simhash_2, r.simhash_3, r.language, r.outlink_count
        FROM responses r
        JOIN urls u ON u.id = r.url_id
        LEFT JOIN urls target ON target.id = r.redirect_url_id
        LEFT JOIN urls canonical ON canonical.id = r.canonical_url_id
        """;

    public const string LINKS = """
        SELECT source.url, target.url, l.kind, l.flags, l.position, l.text, l.fragment
        FROM links l JOIN urls source ON source.id = l.from_id JOIN urls target ON target.id = l.to_id
        """;

    public const string SITEMAP_URLS = """
        SELECT s.url, u.url FROM sitemap_urls su JOIN sitemaps s ON s.id = su.sitemap_id JOIN urls u ON u.id = su.url_id
        """;
}
