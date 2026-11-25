namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// A page's turn on a host that started refusing the crawler (<see cref="RefusalRetreat"/>): held while the page is
/// asked, so the host's pages go one at a time, and given back when it ends. On a host asked at its own pace it holds
/// nothing, and only says which pace the page was asked at.
/// </summary>
internal sealed class RetreatTurn(int generation, Action? end = null) : IDisposable
{
    private Action? _end = end;

    /// <summary>The pace the page was asked at (<see cref="RefusalPace.Generation"/>).</summary>
    public int Generation => generation;

    public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
}
