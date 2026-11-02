using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page's rows, written as one object per row keyed by column id.</summary>
[JsonConverter(typeof(UrlRowsConverter))]
internal sealed record UrlRowsDto(IReadOnlyList<string> Columns, IReadOnlyList<UrlRow> Rows);
