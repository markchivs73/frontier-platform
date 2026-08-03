namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S6.5 tests for <see cref="KillSwitch"/> (doc 07 §7, ADR-G1 emergency brake).</summary>
public sealed class KillSwitchTests
{
    private readonly KillSwitch killSwitch = new();

    [Fact]
    public async Task IsActiveAsync_InitialState_ReturnsFalse()
    {
        var result = await killSwitch.IsActiveAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ActivateAsync_NullReason_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            killSwitch.ActivateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ActivateAsync_WithReason_IsActiveBecomeTrue()
    {
        await killSwitch.ActivateAsync("cost runaway detected", CancellationToken.None);

        var result = await killSwitch.IsActiveAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ActivateAsync_WhenAlreadyActive_RemainsActive()
    {
        await killSwitch.ActivateAsync("first trigger", CancellationToken.None);
        await killSwitch.ActivateAsync("second trigger", CancellationToken.None);

        var result = await killSwitch.IsActiveAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task DeactivateAsync_AfterActivation_IsActiveBecomesFalse()
    {
        await killSwitch.ActivateAsync("emergency stop", CancellationToken.None);

        await killSwitch.DeactivateAsync(CancellationToken.None);

        var result = await killSwitch.IsActiveAsync(CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task DeactivateAsync_WhenAlreadyInactive_RemainsInactive()
    {
        await killSwitch.DeactivateAsync(CancellationToken.None);

        var result = await killSwitch.IsActiveAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ActivateAndDeactivate_ToggleCycle_ReturnsCorrectStateAtEachStep()
    {
        Assert.False(await killSwitch.IsActiveAsync(CancellationToken.None));

        await killSwitch.ActivateAsync("cycle test", CancellationToken.None);
        Assert.True(await killSwitch.IsActiveAsync(CancellationToken.None));

        await killSwitch.DeactivateAsync(CancellationToken.None);
        Assert.False(await killSwitch.IsActiveAsync(CancellationToken.None));

        await killSwitch.ActivateAsync("second activation", CancellationToken.None);
        Assert.True(await killSwitch.IsActiveAsync(CancellationToken.None));
    }
}
