using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page's rows, written as one object per row keyed by column id.</summary>
[JsonConverter(typeof(UrlRowsConverter))]
/// <param name="Changes">
/// With <c>compareWith</c> on a check's rows (the fix loop): how each row moved since the other crawl, in the rows' order,
/// written as each row's <c>change</c> (null where the comparison is counts only). Null without one, and no row has it.
/// </param>
internal sealed record UrlRowsDto(IReadOnlyList<string> Columns, IReadOnlyList<UrlRow> Rows, IReadOnlyList<RowChange?>? Changes = null);
