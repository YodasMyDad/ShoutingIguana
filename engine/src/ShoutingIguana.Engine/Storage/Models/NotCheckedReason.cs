namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a row flagged then is not checked this time, with what the URL does now in a few words ("500 Internal Server Error").</summary>
public sealed record NotCheckedReason(NotCheckedCode Code, string Text);
