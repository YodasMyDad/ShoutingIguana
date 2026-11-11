namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>The score as a word (<see cref="ScoreCalculator.BandOf"/>).</summary>
public enum ScoreBand
{
    /// <summary>90 and over.</summary>
    VeryGood,

    /// <summary>70 to 89.</summary>
    Good,

    /// <summary>50 to 69.</summary>
    NeedsWork,

    /// <summary>Under 50.</summary>
    Poor
}
