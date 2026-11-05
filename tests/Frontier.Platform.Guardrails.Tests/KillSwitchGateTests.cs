using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S13.187: admission's cached read of the kill switch and its failure modes (doc 07 §5a, §10).</summary>
public sealed class KillSwitchGateTests
{
    private static readonly TimeSpan Cache = TimeSpan.FromSeconds(10);
    private static readonly GuardrailPolicy FailOpen = Phase1GuardrailPolicyCatalogue.Default;
    private static readonly GuardrailPolicy FailClosed = Phase1GuardrailPolicyCatalogue.Default with { OnInfrastructureFailure = FailureMode.FailClosed };

    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger<KillSwitchGate> logger = new();
    private readonly Mock<IKillSwitch> killSwitch = new();

    [Fact]
    public void Constructor_RejectsNullsAndANegativeCache()
    {
        Assert.Throws<ArgumentNullException>(() => new KillSwitchGate(null!, Cache, clock, logger));
        Assert.Throws<ArgumentNullException>(() => new KillSwitchGate(killSwitch.Object, Cache, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new KillSwitchGate(killSwitch.Object, Cache, clock, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KillSwitchGate(killSwitch.Object, TimeSpan.FromTicks(-1), clock, logger));
    }

    [Fact]
    public async Task EnsureNotSuspendedAsync_SwitchNotThrown_Returns()
    {
        Reads(false);

        await Gate().EnsureNotSuspendedAsync(FailOpen, CancellationToken.None);

        killSwitch.Verify(k => k.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureNotSuspendedAsync_SwitchThrown_DeniesWithGuardrailsSuspended()
    {
        Reads(true);

        var exception = await Assert.ThrowsAsync<GuardrailsSuspendedException>(() => Gate().EnsureNotSuspendedAsync(FailOpen, CancellationToken.None));

        Assert.Equal(GuardrailsSuspendedException.DefaultMessage, exception.Message);
    }

    [Fact]
    public async Task IsActiveAsync_WithinTheCacheWindow_ReadsTheSwitchOnce()
    {
        Reads(false);
        var gate = Gate();

        await gate.IsActiveAsync(FailOpen, CancellationToken.None);
        clock.Advance(Cache - TimeSpan.FromMilliseconds(1));
        await gate.IsActiveAsync(FailOpen, CancellationToken.None);

        killSwitch.Verify(k => k.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsActiveAsync_ASwitchThrownAfterARead_StopsAdmissionOnceTheCacheExpires()
    {
        killSwitch.SetupSequence(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false).ReturnsAsync(true);
        var gate = Gate();

        Assert.False(await gate.IsActiveAsync(FailOpen, CancellationToken.None));
        clock.Advance(Cache);

        Assert.True(await gate.IsActiveAsync(FailOpen, CancellationToken.None));
        killSwitch.Verify(k => k.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task IsActiveAsync_AZeroCache_ReadsEveryTime()
    {
        Reads(false);
        var gate = new KillSwitchGate(killSwitch.Object, TimeSpan.Zero, clock, logger);

        await gate.IsActiveAsync(FailOpen, CancellationToken.None);
        await gate.IsActiveAsync(FailOpen, CancellationToken.None);

        killSwitch.Verify(k => k.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task IsActiveAsync_ReadFailsUnderFailOpen_AdmitsLogsTheBypassAndCachesNothing()
    {
        var outage = new InvalidOperationException("store unreachable");
        killSwitch.SetupSequence(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(outage).ReturnsAsync(true);
        var gate = Gate();

        Assert.False(await gate.IsActiveAsync(FailOpen, CancellationToken.None));
        Assert.True(await gate.IsActiveAsync(FailOpen, CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(outage, entry.Exception);
        Assert.Contains("guardrail_bypass", entry.Message, StringComparison.Ordinal);
        Assert.Contains(FailOpen.PolicyId, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsActiveAsync_ReadFailsUnderFailClosed_Rethrows()
    {
        var outage = new InvalidOperationException("store unreachable");
        killSwitch.Setup(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(outage);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Gate().IsActiveAsync(FailClosed, CancellationToken.None));

        Assert.Same(outage, thrown);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task IsActiveAsync_ReadFailsUnderFailClosed_CachesNothing()
    {
        killSwitch.SetupSequence(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store unreachable")).ReturnsAsync(true);
        var gate = Gate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.IsActiveAsync(FailClosed, CancellationToken.None));

        Assert.True(await gate.IsActiveAsync(FailClosed, CancellationToken.None));
        killSwitch.Verify(k => k.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Two processes' gates over one shared switch (in-process stand-in for the Cosmos document): a switch thrown after
    /// both have read stops neither inside the cache window, and stops both once it has passed.
    /// </summary>
    [Fact]
    public async Task IsActiveAsync_TwoGatesOverOneSwitch_BothStopWithinOneCacheWindow()
    {
        var shared = new KillSwitch();
        var gates = new[] { new KillSwitchGate(shared, Cache, clock, logger), new KillSwitchGate(shared, Cache, clock, logger) };
        foreach (var gate in gates)
            Assert.False(await gate.IsActiveAsync(FailOpen, CancellationToken.None));

        await shared.ActivateAsync("drill", CancellationToken.None);
        clock.Advance(Cache - TimeSpan.FromMilliseconds(1));
        foreach (var gate in gates)
            Assert.False(await gate.IsActiveAsync(FailOpen, CancellationToken.None));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        foreach (var gate in gates)
            Assert.True(await gate.IsActiveAsync(FailOpen, CancellationToken.None));
    }

    [Fact]
    public async Task IsActiveAsync_Cancelled_PropagatesEvenUnderFailOpen()
    {
        killSwitch.Setup(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Gate().IsActiveAsync(FailOpen, CancellationToken.None));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void CacheDuration_IsTheValueGiven()
    {
        Assert.Equal(Cache, new KillSwitchGate(killSwitch.Object, Cache, clock, NullLogger<KillSwitchGate>.Instance).CacheDuration);
    }

    private KillSwitchGate Gate() => new(killSwitch.Object, Cache, clock, logger);

    private void Reads(bool active) => killSwitch.Setup(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
}
