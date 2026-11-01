using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace ShoutingIguana.Sidecar.Events;

/// <summary>
/// Writes <c>GET /api/events</c> as server-sent events: one stream for every live event, as Chromium allows only six
/// connections to the port. Events carry no ids: a client that reconnects starts again from <c>hello</c>. Written by hand
/// rather than with TypedResults.ServerSentEvents, which has no way to send the keep-alive comment an idle stream needs.
/// </summary>
internal static class EventStream
{
    private const string KEEP_ALIVE = ": keep-alive\n\n";

    /// <param name="stopping">Ends the stream when the sidecar shuts down, so the server needn't wait for the client.</param>
    public static async Task WriteAsync(HttpContext context, EventHub hub, TimeSpan keepAlive, CancellationToken stopping)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        var client = hub.Connect();
        try
        {
            StringBuilder text = new();
            while (!stop.IsCancellationRequested)
            {
                // Null: the client fell too far behind. Ending the stream disconnects it, and it reconnects.
                if (client.TakeAll() is not { } events)
                {
                    return;
                }

                if (events.Count == 0)
                {
                    if (!await client.WaitAsync(keepAlive, stop.Token).ConfigureAwait(false))
                    {
                        await context.Response.WriteAsync(KEEP_ALIVE, stop.Token).ConfigureAwait(false);
                        await context.Response.Body.FlushAsync(stop.Token).ConfigureAwait(false);
                    }

                    continue;
                }

                text.Clear();
                foreach (var sidecarEvent in events)
                {
                    text.Append("event: ").Append(sidecarEvent.Name).Append('\n').Append("data: ").Append(sidecarEvent.Data).Append("\n\n");
                }

                await context.Response.WriteAsync(text.ToString(), stop.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The client went away, or the sidecar is shutting down.
        }
        finally
        {
            hub.Disconnect(client);
        }
    }
}
