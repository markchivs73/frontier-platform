using Microsoft.Extensions.Logging;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>An <see cref="ILogger{T}"/> that records each entry's level, message and exception.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every entry logged, in order.</summary>
    internal List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
