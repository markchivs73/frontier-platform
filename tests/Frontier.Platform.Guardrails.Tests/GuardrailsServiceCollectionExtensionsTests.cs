using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
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
}
