using Frontier.Platform.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4/S4.7b DI-wiring test for <see cref="ResilienceServiceCollectionExtensions"/>.</summary>
public sealed class ResilienceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFrontierResilience_RegistersExpectedServices()
    {
        var services = new ServiceCollection().AddFrontierResilience();
        var provider = services.BuildServiceProvider();

        Assert.IsType<FailureClassifier>(provider.GetRequiredService<IFailureClassifier>());
        Assert.IsType<ResiliencePolicyProvider>(provider.GetRequiredService<IResiliencePolicyProvider>());
        Assert.IsType<CircuitStateProvider>(provider.GetRequiredService<ICircuitStateProvider>());
        Assert.IsType<RetryBudget>(provider.GetRequiredService<IRetryBudget>());
    }

    [Fact]
    public void AddFrontierResilience_RegistersTimeoutHierarchyCheckAsStartupCheck()
    {
        var provider = new ServiceCollection().AddFrontierResilience().BuildServiceProvider();

        var check = provider.GetRequiredService<IStartupCheck>();

        Assert.IsType<TimeoutHierarchyCheck>(check);
    }
}
