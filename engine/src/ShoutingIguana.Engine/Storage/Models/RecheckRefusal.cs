namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a recheck can't run now, in a sentence a person can read.</summary>
public sealed record RecheckRefusal(RecheckRefusalCode Code, string Title);
