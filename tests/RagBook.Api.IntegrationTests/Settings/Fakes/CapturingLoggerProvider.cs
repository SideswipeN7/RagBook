using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RagBook.Api.IntegrationTests.Settings.Fakes;

/// <summary>
/// Captures every formatted log message (and its state/scope text) so a test can prove the full API key
/// never reaches the logs (US-02 AC-5, FR-011).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary>All captured log text since the last <see cref="Clear"/>.</summary>
    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    /// <summary>Resets the captured log buffer.</summary>
    public void Clear()
    {
        _messages.Clear();
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(_messages);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            sink.Enqueue(state.ToString() ?? string.Empty);

            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Enqueue(formatter(state, exception));

            if (exception is not null)
            {
                sink.Enqueue(exception.ToString());
            }
        }
    }
}
