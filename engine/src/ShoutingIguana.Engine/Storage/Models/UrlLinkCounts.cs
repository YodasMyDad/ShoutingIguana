namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL's links and sitemap listing, as the URL summary counts them (see <see cref="UrlSummary"/>).</summary>
public sealed record UrlLinkCounts(int Inlinks, int FollowedInlinks, int NofollowInlinks, int LinkingPages, int? Outlinks, bool? InSitemap);
