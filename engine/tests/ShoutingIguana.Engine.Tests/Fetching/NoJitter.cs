namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>A <see cref="Random"/> that always returns the minimum, so retry delays are exact on the fake clock.</summary>
internal sealed class NoJitter : Random
{
    public override int Next(int minValue, int maxValue) => minValue;
}
