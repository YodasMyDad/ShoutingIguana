using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Sidecar.Errors.Models;

namespace ShoutingIguana.Sidecar.Exports.Models;

/// <summary>One export this process runs or ran. <see cref="ExportManager"/> changes it under its lock.</summary>
internal sealed class ExportJob(string id, string crawlId, ExportRequest request, string path)
{
    public string Id { get; } = id;

    public string CrawlId { get; } = crawlId;

    public ExportRequest Request { get; } = request;

    /// <summary>The file it writes.</summary>
    public string Path { get; } = path;

    /// <summary>Cancels it. Never disposed: a cancel that arrives as the export ends finds it there, and changes nothing.</summary>
    public CancellationTokenSource Cancel { get; } = new();

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public ExportStatus Status { get; set; } = ExportStatus.Running;

    public long RowsWritten { get; set; }

    public long? RowsExpected { get; set; }

    public DateTime? FinishedAt { get; set; }

    public ExportResult? Result { get; set; }

    public ProblemDto? Error { get; set; }

    /// <summary>When its progress was last sent, as a stopwatch reads; 0 before the first.</summary>
    public long ProgressSentAt { get; set; }

    /// <summary>The export itself, from its first read to its file in place.</summary>
    public Task Lifetime { get; set; } = Task.CompletedTask;
}
