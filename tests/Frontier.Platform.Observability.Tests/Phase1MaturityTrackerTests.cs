namespace Frontier.Platform.Observability.Tests;

/// <summary>S6.8 tests for <see cref="Phase1MaturityTracker"/> (doc 11 §6).</summary>
public sealed class Phase1MaturityTrackerTests
{
    private readonly Phase1MaturityTracker _tracker = new();

    [Fact]
    public async Task GetAsync_AnyPair_ReturnsNull()
    {
        var result = await _tracker.GetAsync("writer", "advisory-sow", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList()
    {
        var result = await _tracker.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}
