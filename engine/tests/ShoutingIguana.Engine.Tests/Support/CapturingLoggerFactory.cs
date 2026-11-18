using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>Keeps every message logged through it, at every level, with its exception's: for tests that a secret never reaches a log.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyList<string> Messages => [.. _messages];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _messages);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {state} {exception}");
    }
}
