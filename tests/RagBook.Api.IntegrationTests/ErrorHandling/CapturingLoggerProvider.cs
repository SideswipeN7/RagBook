using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RagBook.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// A minimal in-memory <see cref="ILoggerProvider"/> that records every formatted log message, so a test can assert
/// the correlation id logged by <c>GlobalExceptionHandler</c> matches the one returned to the client (US-19 AC-4).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>Every formatted log line captured across all categories.</summary>
    public ConcurrentQueue<string> Messages { get; } = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
        }
    }
}
