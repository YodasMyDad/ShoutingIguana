namespace ShoutingIguana.TestSite.Models;

/// <summary>How a check moved between two crawls. The names are the engine's comparison's, so tests match them by name.</summary>
public enum SyntheticCheckState
{
    /// <summary>It ran then and found nothing, and finds something now.</summary>
    New,

    /// <summary>It found something then, nothing now, and nothing it flagged then went unchecked.</summary>
    Fixed,
    Fewer,
    More,
    Same,

    /// <summary>It didn't run then.</summary>
    NewCheck,

    /// <summary>It ran then and is skipped now.</summary>
    NotCheckedNow
}
