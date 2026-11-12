namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The room a host has for a read to use more than one connection at once: a pass over every link of a large crawl is
/// split by link id over several, each on a core of its own (<see cref="LinkGridReader"/>). A read takes only the helpers
/// that are free the moment it asks, never waiting for one, and gives them back as its pass ends; with none free, or none
/// given, it reads on its own connection as it always did, and answers the same.
/// </summary>
/// <param name="tryTake">Takes one helper if one is free right now: true when it did. Never waits.</param>
/// <param name="release">Gives one helper back.</param>
public sealed class ReadHelpers(Func<bool> tryTake, Action release)
{
    /// <summary>Up to this many helpers at once, shared by every read that's given these: for a host with no queue of reads of its own.</summary>
    public static ReadHelpers UpTo(int most)
    {
        if (most <= 0)
        {
            return new ReadHelpers(() => false, () => { });
        }

        SemaphoreSlim free = new(most, most);
        return new ReadHelpers(() => free.Wait(0), () => free.Release());
    }

    /// <summary>As many helpers as are free now, up to <paramref name="most"/>: given back when the result is disposed.</summary>
    internal Borrowed Borrow(int most)
    {
        var taken = 0;
        while (taken < most && tryTake())
        {
            taken++;
        }

        return new Borrowed(taken, release);
    }

    /// <summary>The helpers one read holds for as long as its pass lasts.</summary>
    internal sealed class Borrowed(int count, Action release) : IDisposable
    {
        private int _held = count;

        public int Count { get; } = count;

        public void Dispose()
        {
            for (var held = Interlocked.Exchange(ref _held, 0); held > 0; held--)
            {
                release();
            }
        }
    }
}
