using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// A logger that cancels as soon as something logs a failure, so a test can cancel an analysis at an exact point: the
/// page where a rule fails.
/// </summary>
internal sealed class CancelOnLoggedFailure(CancellationTokenSource cancel) : ILoggerFactory, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning && exception is not null)
        {
            cancel.Cancel();
        }
    }

    public void Dispose()
    {
    }
}
