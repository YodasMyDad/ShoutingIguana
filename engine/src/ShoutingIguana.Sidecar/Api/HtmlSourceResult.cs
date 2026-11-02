using System.Buffers;
using System.Text.Json;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// A page's stored HTML as <c>{"stored":true,"html":"…","bytes":96340,"truncated":false}</c>, written as it's unpacked, a
/// piece at a time, so a view of a 16 MB page holds a few hundred kilobytes, not the page. The JSON is escaped as every
/// answer's is (<c>&lt;</c> as <c><</c>, and so on), so its bytes hold no markup.
/// </summary>
internal sealed class HtmlSourceResult(HtmlSourceReader source) : IResult
{
    /// <summary>Characters unpacked at a time.</summary>
    private const int PIECE_CHARS = 16 * 1024;

    /// <summary>Escaped JSON is sent on once there's this much of it.</summary>
    private const int SEND_BYTES = 64 * 1024;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;
        var piece = ArrayPool<char>.Shared.Rent(PIECE_CHARS);
        var writing = false;
        try
        {
            // The first piece before anything is written, so a damaged page is a problem, not an answer cut short.
            int count;
            try
            {
                count = source.Read(piece);
            }
            catch (InvalidDataException exception)
            {
                throw Problems.FileError("This page's stored HTML is damaged, so it can't be shown.", exception.Message);
            }

            writing = true;
            response.ContentType = "application/json; charset=utf-8";

            // One buffer, reused: the server's own buffers are 4 KB, and it allocates afresh for anything larger, which a
            // piece of escaped HTML is.
            var escaped = new ArrayBufferWriter<byte>(SEND_BYTES * 2);
            await using var json = new Utf8JsonWriter(escaped);
            json.WriteStartObject();
            json.WriteBoolean("stored", true);
            json.WritePropertyName("html");
            do
            {
                json.WriteStringValueSegment(piece.AsSpan(0, count), isFinalSegment: false);
                if (json.BytesPending + escaped.WrittenCount >= SEND_BYTES)
                {
                    json.Flush();
                    await response.Body.WriteAsync(escaped.WrittenMemory, cancellationToken).ConfigureAwait(false);
                    escaped.ResetWrittenCount();
                }
            }
            while ((count = source.Read(piece)) > 0);

            json.WriteStringValueSegment(ReadOnlySpan<char>.Empty, isFinalSegment: true);
            if (source.Bytes is { } bytes)
            {
                json.WriteNumber("bytes", bytes);
            }
            else
            {
                json.WriteNull("bytes");
            }

            json.WriteBoolean("truncated", source.Truncated);
            json.WriteEndObject();
            json.Flush();
            await response.Body.WriteAsync(escaped.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (writing && !cancellationToken.IsCancellationRequested)
        {
            // Part-way through, an answer can't become a problem: ending the connection tells the client it's incomplete.
            httpContext.Abort();
            throw;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(piece);
            source.Dispose();
        }
    }
}
