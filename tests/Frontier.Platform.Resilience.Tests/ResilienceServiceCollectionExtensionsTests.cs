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

    /// <summary>S13.217: the circuit-state provider callers read is the one the policy provider's breakers report to.</summary>
    [Fact]
    public void AddFrontierResilience_CircuitStateProviderIsTheTrackingInstance()
    {
        var provider = new ServiceCollection().AddFrontierResilience().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<CircuitStateProvider>(), provider.GetRequiredService<ICircuitStateProvider>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddFrontierResilience_KeepsAnExistingTimeProvider()
    {
        var clock = new ManualTimeProvider();

        var provider = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddFrontierResilience().BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddFrontierResilience_RegistersTimeoutHierarchyCheckAsStartupCheck()
    {
        var provider = new ServiceCollection().AddFrontierResilience().BuildServiceProvider();

        var check = provider.GetRequiredService<IStartupCheck>();

        Assert.IsType<TimeoutHierarchyCheck>(check);
    }
}
