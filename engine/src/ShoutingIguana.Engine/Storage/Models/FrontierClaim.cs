namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="InFlight">URLs claimed but not yet recorded, including this claim.</param>
internal sealed record FrontierClaim(IReadOnlyList<FrontierItem> Internal, IReadOnlyList<FrontierItem> External, int InFlight);
