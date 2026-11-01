using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The event stream itself: what it starts with, and the comments that keep an idle one open.</summary>
public sealed class EventStreamTests
{
    [Fact]
    public async Task AnIdleStreamGetsKeepAliveComments()
    {
        await using var sidecar = await TestSidecar.StartAsync(configure: options => options with { KeepAliveInterval = TimeSpan.FromMilliseconds(100) });
        await using var events = await EventStreamReader.ConnectAsync(sidecar.Client);

        Assert.Empty((await events.NextAsync("hello"))["live"]!.AsArray());
        var deadline = DateTime.UtcNow + EventStreamReader.TIMEOUT;
        while (events.Comments < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(events.Comments >= 3, $"{events.Comments} keep-alive comments");
        Assert.Single(events.Seen());
    }

    [Fact]
    public async Task EveryStreamEndsWhenTheSidecarShutsDown()
    {
        var sidecar = await TestSidecar.StartAsync();
        try
        {
            await using var first = await EventStreamReader.ConnectAsync(sidecar.Client);
            await using var second = await EventStreamReader.ConnectAsync(sidecar.Client);
            await first.NextAsync("hello");
            await second.NextAsync("hello");

            await sidecar.Host.StopAsync().WaitAsync(EventStreamReader.TIMEOUT);

            await first.Ended.WaitAsync(EventStreamReader.TIMEOUT);
            await second.Ended.WaitAsync(EventStreamReader.TIMEOUT);
        }
        finally
        {
            await sidecar.DisposeAsync();
        }
    }
}
