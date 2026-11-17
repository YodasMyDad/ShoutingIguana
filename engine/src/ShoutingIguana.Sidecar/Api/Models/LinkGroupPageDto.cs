using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of the links a view matches, grouped by target (<see cref="LinkGroupPage"/>), its order as a request gives one.</summary>
internal sealed record LinkGroupPageDto(long Total, long Links, long Pages, int Offset, LinkGroupSortDto Sort, IReadOnlyList<LinkGroupRow> Rows, SummaryState Summary);
