namespace Frontier.Platform.Resilience.Tests;

/// <summary>S6.7 tests for <see cref="RetryBudget"/> and <see cref="RetryBudgetState"/> (doc 10 §5).</summary>
public sealed class RetryBudgetTests
{
    // ─── RetryBudget (public surface) ────────────────────────────────────────

    [Fact]
    public void TryConsume_NullExecutionId_Throws()
    {
        var budget = new RetryBudget();

        Assert.Throws<ArgumentNullException>(() => budget.TryConsume(null!));
    }

    [Fact]
    public void GetSnapshot_NullExecutionId_Throws()
    {
        var budget = new RetryBudget();

        Assert.Throws<ArgumentNullException>(() => budget.GetSnapshot(null!));
    }

    [Fact]
    public void GetSnapshot_NoCallsMade_ReturnsEmptyNonExhaustedWindow()
    {
        var budget = new RetryBudget();

        var snapshot = budget.GetSnapshot("exec-1");

        Assert.Equal("exec-1", snapshot.ExecutionId);
        Assert.Equal(0, snapshot.InvocationCount);
        Assert.Equal(0, snapshot.RetryCount);
        Assert.False(snapshot.IsExhausted);
    }

    [Fact]
    public void TryConsume_FirstCall_ReturnsTrue()
    {
        var budget = new RetryBudget();

        Assert.True(budget.TryConsume("exec-1"));
    }

    [Fact]
    public void TryConsume_IndependentExecutions_TrackSeparately()
    {
        var budget = new RetryBudget();

        for (var i = 0; i < RetryBudgetState.MinimumFloor + 1; i++)
            budget.TryConsume("exec-exhausted");

        Assert.True(budget.TryConsume("exec-fresh"));
    }

    [Fact]
    public void GetSnapshot_AfterCalls_ReflectsWindowCounts()
    {
        var budget = new RetryBudget();
        for (var i = 0; i < 5; i++) budget.TryConsume("exec-1");

        var snapshot = budget.GetSnapshot("exec-1");

        Assert.Equal("exec-1", snapshot.ExecutionId);
        Assert.Equal(5, snapshot.InvocationCount);
        Assert.Equal(5, snapshot.RetryCount);
        Assert.False(snapshot.IsExhausted);
    }

    // ─── RetryBudgetState (sliding window arithmetic) ────────────────────────

    [Fact]
    public void TryConsume_BelowFloor_AllSucceed()
    {
        var state = new RetryBudgetState();

        for (var i = 0; i < RetryBudgetState.MinimumFloor; i++)
            Assert.True(state.TryConsume());
    }

    [Fact]
    public void TryConsume_AtFloorPlusOne_ReturnsFalse()
    {
        var state = new RetryBudgetState();

        for (var i = 0; i < RetryBudgetState.MinimumFloor; i++)
            state.TryConsume();

        Assert.False(state.TryConsume());
    }

    [Fact]
    public void TryConsume_AfterExhaustion_ContinuesToReturnFalse()
    {
        var state = new RetryBudgetState();
        for (var i = 0; i <= RetryBudgetState.MinimumFloor; i++)
            state.TryConsume();

        Assert.False(state.TryConsume());
        Assert.False(state.TryConsume());
    }

    [Fact]
    public void TryConsume_AfterFullWindowSlides_RenewsBudget()
    {
        var state = new RetryBudgetState();

        // Exhaust the floor budget
        for (var i = 0; i < RetryBudgetState.MinimumFloor; i++)
            Assert.True(state.TryConsume());
        Assert.False(state.TryConsume()); // 11th = false

        // Fill the rest of the window with denied calls (slide them out)
        for (var i = 0; i < RetryBudgetState.WindowSize - RetryBudgetState.MinimumFloor - 1; i++)
            state.TryConsume(); // false — denied

        // The window is now full (50 entries: 10 allowed + 40 denied)
        // Next call slides out the oldest (an allowed entry) → retryCount drops → budget renews
        Assert.True(state.TryConsume());
    }

    [Fact]
    public void GetCounts_EmptyState_ReturnsZerosAndNotExhausted()
    {
        var state = new RetryBudgetState();

        var (invocationCount, retryCount, isExhausted) = state.GetCounts();

        Assert.Equal(0, invocationCount);
        Assert.Equal(0, retryCount);
        Assert.False(isExhausted);
    }

    [Fact]
    public void GetCounts_AfterExhaustion_ReportsExhausted()
    {
        var state = new RetryBudgetState();
        for (var i = 0; i <= RetryBudgetState.MinimumFloor; i++)
            state.TryConsume();

        var (_, _, isExhausted) = state.GetCounts();

        Assert.True(isExhausted);
    }

    // ─── ComputeBudget (pure function) ───────────────────────────────────────

    [Theory]
    [InlineData(0, RetryBudgetState.MinimumFloor)]
    [InlineData(1, RetryBudgetState.MinimumFloor)]
    [InlineData(RetryBudgetState.MinimumFloor, RetryBudgetState.MinimumFloor)]
    [InlineData(50, 10)]   // 50 * 0.20 = 10 = floor
    [InlineData(55, 11)]   // 55 * 0.20 = 11 > floor
    [InlineData(100, 20)]  // 100 * 0.20 = 20 > floor
    public void ComputeBudget_ReturnsExpectedAllowance(int invocationCount, int expectedBudget)
    {
        Assert.Equal(expectedBudget, RetryBudgetState.ComputeBudget(invocationCount));
    }
}
