using Microsoft.Extensions.Time.Testing;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Moves a fake clock on while a crawl runs, so its waits (backoff, pauses, cool-downs, timeouts) pass in moments. Each
/// step gives the real work that is due a moment to run first. How much fake time that work spans depends on the machine
/// (Task.Delay(1) takes 1 ms on Linux and macOS, about 15 ms on Windows), so tests measure the fake time between events the
/// clock itself drives, not the total.
/// </summary>
internal static class FakeClock
{
    public static readonly TimeSpan STEP = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan REAL_LIMIT = TimeSpan.FromMinutes(1);

    public static async Task<T> PumpAsync<T>(FakeTimeProvider time, Task<T> task, TimeSpan? step = null, TimeSpan? realLimit = null)
    {
        var deadline = DateTime.UtcNow + (realLimit ?? REAL_LIMIT);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Still running after {realLimit ?? REAL_LIMIT} of real time, at {time.GetUtcNow():O} on the fake clock.");
            }

            time.Advance(step ?? STEP);
            await Task.Delay(1);
        }

        return await task;
    }
}
