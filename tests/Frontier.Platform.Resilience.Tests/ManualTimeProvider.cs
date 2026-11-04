namespace Frontier.Platform.Resilience.Tests;

/// <summary>A <see cref="TimeProvider"/> whose clock moves only when a test calls <see cref="Advance"/>, so breaker durations elapse on demand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>Moves the clock forward by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by) => now += by;
}
