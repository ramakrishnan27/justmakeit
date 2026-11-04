using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Collects every log message (and exception text) the hosted API writes, so a test can assert that no secret
/// (a password, a link, an address) ever reached the log.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Capture(_messages);

    public void Dispose()
    {
    }

    private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
    }
}
