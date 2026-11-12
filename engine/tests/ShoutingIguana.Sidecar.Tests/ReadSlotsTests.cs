using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>
/// The read slots a pass over the links may use beside its own (E6): only those free at that moment, never waited for,
/// and never the last one free, so a read that arrives while a pass runs starts at once.
/// </summary>
public sealed class ReadSlotsTests
{
    [Fact]
    public void APassTakesTheSlotsThatAreFreeButNeverTheLast()
    {
        var slots = Slots(4);

        // A read has its own slot: of the three left it may take two.
        Assert.True(slots.Free.Wait(0));
        Assert.True(slots.TryTakeHelper());
        Assert.True(slots.TryTakeHelper());
        Assert.False(slots.TryTakeHelper());
        Assert.Equal(1, slots.Free.CurrentCount);

        // So a read arriving now starts at once; with every slot then taken, a pass gets none, and doesn't wait for one.
        Assert.True(slots.Free.Wait(0));
        Assert.False(slots.TryTakeHelper());
        Assert.Equal(0, slots.Free.CurrentCount);

        // A slot given back is one a later pass may take, while it leaves one free.
        slots.Free.Release(3);
        Assert.True(slots.TryTakeHelper());
        Assert.True(slots.TryTakeHelper());
        Assert.False(slots.TryTakeHelper());
    }

    /// <summary>With two slots (a two-core machine) a read's own leaves one, which is the last: its pass is never split.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OnAMachineWithTwoSlotsAPassIsNeverSplit(int gridReads)
    {
        var slots = Slots(gridReads);

        Assert.True(slots.Free.Wait(0));
        Assert.False(slots.TryTakeHelper());
        Assert.Equal(gridReads - 1, slots.Free.CurrentCount);
    }

    /// <summary>
    /// Reads and passes all asking at once: whoever asks, a pass never ends up holding the last slot (each time it takes
    /// one, one more was free), nothing ever waits on a pass, and every slot comes back.
    /// </summary>
    [Fact]
    public async Task ReadsAndPassesAskingAtOnceLeaveNoSlotLostAndNoPassHoldingTheLast()
    {
        const int SLOTS = 6;
        var slots = Slots(SLOTS);
        var helpers = 0;
        var most = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var round = 0; round < 20_000; round++)
            {
                if (worker % 2 == 0)
                {
                    // A read: its own slot, held a moment.
                    slots.Free.Wait();
                    Thread.SpinWait(20);
                    slots.Free.Release();
                }
                else if (slots.TryTakeHelper())
                {
                    var held = Interlocked.Increment(ref helpers);
                    for (var seen = Volatile.Read(ref most); held > seen; seen = Volatile.Read(ref most))
                    {
                        Interlocked.CompareExchange(ref most, held, seen);
                    }

                    Thread.SpinWait(20);
                    Interlocked.Decrement(ref helpers);
                    slots.Free.Release();
                }
            }
        })));

        Assert.Equal(SLOTS, slots.Free.CurrentCount);
        Assert.InRange(most, 1, SLOTS - 1);
    }

    private static ReadSlots Slots(int gridReads) => new(new SidecarOptions { DataDirectory = "data", StateDirectory = "state", Token = "token", GridReads = gridReads });
}
