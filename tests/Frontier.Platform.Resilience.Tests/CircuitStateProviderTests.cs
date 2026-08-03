using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4 tests for the <see cref="CircuitStateProvider"/> Phase 1 stub (doc 10 §6).</summary>
public sealed class CircuitStateProviderTests
{
    private readonly CircuitStateProvider provider = new();

    [Fact]
    public void GetState_AnyProviderAndModel_ReturnsClosed()
    {
        Assert.Equal(CircuitState.Closed, provider.GetState("anthropic", "claude-fable-5"));
    }

    [Fact]
    public void Subscribe_ReturnsDisposableThatNeverInvokesCallback()
    {
        var invoked = false;

        using var subscription = provider.Subscribe(_ => invoked = true);

        Assert.False(invoked);
    }
}
