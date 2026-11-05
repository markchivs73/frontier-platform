using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S4.5/S6.5 DI-wiring test for <see cref="GuardrailsServiceCollectionExtensions"/>.</summary>
public sealed class GuardrailsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFrontierGuardrails_RegistersExpectedServices()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        Assert.IsType<AdmissionController>(provider.GetRequiredService<IAdmissionController>());
        Assert.IsType<BudgetLedger>(provider.GetRequiredService<IBudgetLedger>());
        Assert.IsType<BudgetHierarchy>(provider.GetRequiredService<IBudgetHierarchy>());
        Assert.IsType<KillSwitch>(provider.GetRequiredService<IKillSwitch>());
    }

    [Fact]
    public void AddFrontierGuardrails_BudgetHierarchy_IsSingleton()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IBudgetHierarchy>();
        var second = provider.GetRequiredService<IBudgetHierarchy>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierGuardrails_KillSwitch_IsSingleton()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IKillSwitch>();
        var second = provider.GetRequiredService<IKillSwitch>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierGuardrails_BudgetLedger_IsSingleton()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IBudgetLedger>();
        var second = provider.GetRequiredService<IBudgetLedger>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierGuardrails_AdmissionController_IsSingleton()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAdmissionController>();
        var second = provider.GetRequiredService<IAdmissionController>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierGuardrails_AllServicesResolvable()
    {
        var services = new ServiceCollection().AddFrontierGuardrails();
        var provider = services.BuildServiceProvider();

        var admissionController = provider.GetRequiredService<IAdmissionController>();
        var budgetLedger = provider.GetRequiredService<IBudgetLedger>();
        var budgetHierarchy = provider.GetRequiredService<IBudgetHierarchy>();
        var killSwitch = provider.GetRequiredService<IKillSwitch>();

        Assert.NotNull(admissionController);
        Assert.NotNull(budgetLedger);
        Assert.NotNull(budgetHierarchy);
        Assert.NotNull(killSwitch);
    }

    [Fact]
    public void AddFrontierGuardrails_WithCosmosContainer_RegistersExpectedServices()
    {
        var mockContainer = new Mock<Container>();
        var services = new ServiceCollection().AddFrontierGuardrails(mockContainer.Object);
        var provider = services.BuildServiceProvider();

        Assert.IsType<AdmissionController>(provider.GetRequiredService<IAdmissionController>());
        Assert.IsType<CosmosBudgetLedger>(provider.GetRequiredService<IBudgetLedger>());
        Assert.IsType<BudgetHierarchy>(provider.GetRequiredService<IBudgetHierarchy>());
        Assert.IsType<KillSwitch>(provider.GetRequiredService<IKillSwitch>());
    }

    [Fact]
    public void AddFrontierGuardrails_WithCosmosContainer_UsesCosmosBudgetLedger()
    {
        var mockContainer = new Mock<Container>();
        var services = new ServiceCollection().AddFrontierGuardrails(mockContainer.Object);
        var provider = services.BuildServiceProvider();

        var ledger = provider.GetRequiredService<IBudgetLedger>();

        Assert.IsType<CosmosBudgetLedger>(ledger);
    }

    [Fact]
    public void AddFrontierGuardrails_InProcessVariants_ReadTheKillSwitchUncached()
    {
        var inMemory = new ServiceCollection().AddFrontierGuardrails().BuildServiceProvider();
        var withLedger = new ServiceCollection().AddFrontierGuardrails(new Mock<Container>().Object).BuildServiceProvider();

        Assert.Equal(TimeSpan.Zero, inMemory.GetRequiredService<KillSwitchGate>().CacheDuration);
        Assert.Equal(TimeSpan.Zero, withLedger.GetRequiredService<KillSwitchGate>().CacheDuration);
    }

    [Fact]
    public void AddFrontierGuardrails_WithPlatformConfig_SharesTheKillSwitchThroughCosmosAndCachesAdmissionReads()
    {
        var cache = TimeSpan.FromSeconds(7);
        var provider = new ServiceCollection()
            .AddFrontierGuardrails(new Mock<Container>().Object, new Mock<Container>().Object, cache)
            .BuildServiceProvider();

        Assert.IsType<CosmosKillSwitch>(provider.GetRequiredService<IKillSwitch>());
        Assert.IsType<CosmosBudgetLedger>(provider.GetRequiredService<IBudgetLedger>());
        Assert.IsType<AdmissionController>(provider.GetRequiredService<IAdmissionController>());
        Assert.IsType<BudgetHierarchy>(provider.GetRequiredService<IBudgetHierarchy>());
        Assert.Equal(cache, provider.GetRequiredService<KillSwitchGate>().CacheDuration);
    }

    [Fact]
    public void AddFrontierGuardrails_WithPlatformConfig_RejectsANullContainerAndANegativeCache()
    {
        var ledger = new Mock<Container>().Object;

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierGuardrails(ledger, null!, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddFrontierGuardrails(ledger, ledger, TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void DefaultKillSwitchCacheDuration_IsDocSevensTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), GuardrailsServiceCollectionExtensions.DefaultKillSwitchCacheDuration);
    }

    [Fact]
    public async Task AddFrontierGuardrails_ARegisteredLogger_ReceivesTheFailOpenBypassWarning()
    {
        var logger = new CapturingLogger<KillSwitchGate>();
        var unreadable = new Mock<IKillSwitch>();
        unreadable.Setup(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store unreachable"));
        var provider = new ServiceCollection()
            .AddFrontierGuardrails()
            .AddSingleton(unreadable.Object)
            .AddSingleton<ILogger<KillSwitchGate>>(logger)
            .BuildServiceProvider();

        var decision = await provider.GetRequiredService<IAdmissionController>().AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task AddFrontierGuardrails_NoLoggerRegistered_StillAdmitsUnderFailOpen()
    {
        var unreadable = new Mock<IKillSwitch>();
        unreadable.Setup(k => k.IsActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store unreachable"));
        var provider = new ServiceCollection().AddFrontierGuardrails().AddSingleton(unreadable.Object).BuildServiceProvider();

        var decision = await provider.GetRequiredService<IAdmissionController>().AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
    }

    [Fact]
    public async Task AddFrontierGuardrails_ASwitchThrownThroughTheRegisteredKillSwitch_StopsTheNextAdmission()
    {
        var provider = new ServiceCollection().AddFrontierGuardrails().BuildServiceProvider();

        await provider.GetRequiredService<IKillSwitch>().ActivateAsync("drill", CancellationToken.None);

        await Assert.ThrowsAsync<GuardrailsSuspendedException>(() =>
            provider.GetRequiredService<IAdmissionController>().AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None));
    }
}
