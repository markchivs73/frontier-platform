namespace Frontier.Platform.Guardrails.Tests;

/// <summary>A <see cref="TimeProvider"/> whose clock moves only when a test advances it.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>Moves the clock forward by <paramref name="by"/>.</summary>
    internal void Advance(TimeSpan by) => now += by;
}
