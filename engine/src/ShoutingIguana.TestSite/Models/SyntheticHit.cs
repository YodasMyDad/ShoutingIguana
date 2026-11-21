namespace ShoutingIguana.TestSite.Models;

/// <summary>One request the synthetic site saw, once a test asked <see cref="SyntheticRequestLog.WatchHits"/>.</summary>
/// <param name="Path">The request path.</param>
/// <param name="FromBrowser">The request carried a browser's <c>Sec-Fetch-Mode</c> or <c>sec-ch-ua</c>.</param>
public readonly record struct SyntheticHit(string Path, bool FromBrowser);
