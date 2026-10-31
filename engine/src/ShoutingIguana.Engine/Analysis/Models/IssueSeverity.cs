namespace ShoutingIguana.Engine.Analysis.Models;

public enum IssueSeverity : byte
{
    /// <summary>Broken for search engines or users now: fix first.</summary>
    Error,

    /// <summary>Likely to cost rankings or traffic: worth fixing.</summary>
    Warning,

    /// <summary>Worth a look: often fine when it is intended.</summary>
    Notice
}
