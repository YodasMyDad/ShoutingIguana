namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a check moved between two crawls (<see cref="CheckChange"/>), served as its <c>state</c> in camelCase.</summary>
public enum CheckChangeState
{
    /// <summary>It ran then and found nothing, and finds something now.</summary>
    New,

    /// <summary>It found something then and nothing now, and nothing it flagged then went unchecked this time.</summary>
    Fixed,

    /// <summary>Fewer now, a would-be fixed with some rows not checked this time among them.</summary>
    Fewer,

    /// <summary>More now.</summary>
    More,

    /// <summary>The same count; its rows may still have moved.</summary>
    Same,

    /// <summary>It didn't run then: newer than that analysis, or skipped.</summary>
    NewCheck,

    /// <summary>It ran then and is skipped now.</summary>
    NotCheckedNow
}
