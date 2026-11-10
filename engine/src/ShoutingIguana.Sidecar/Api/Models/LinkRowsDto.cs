using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of links, written as one object per link keyed by column id.</summary>
[JsonConverter(typeof(LinkRowsConverter))]
internal sealed record LinkRowsDto(IReadOnlyList<string> Columns, IReadOnlyList<LinkGridRow> Rows);
