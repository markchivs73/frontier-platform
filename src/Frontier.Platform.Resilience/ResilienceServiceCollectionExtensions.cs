using Frontier.Platform.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Resilience;

/// <summary>
/// DI registration for the Resilience library (engineering-standards: each library
/// wires its own internals; only Host calls these extensions).
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFailureClassifier"/>, <see cref="IResiliencePolicyProvider"/>,
    /// <see cref="ICircuitStateProvider"/>, <see cref="IRetryBudget"/>, and the
    /// <see cref="TimeoutHierarchyCheck"/> boot invariant (doc 12 §6). All are
    /// stateless or in-process-stateful singletons over the compiled-in
    /// <see cref="Phase1ResilienceProfileCatalogue"/> (doc 10 §9 cold-start fallback) —
    /// no configuration to bind for Phase 1.
    /// </summary>
    public static IServiceCollection AddFrontierResilience(this IServiceCollection services) =>
        services
            .AddSingleton<IFailureClassifier, FailureClassifier>()
            .AddSingleton<IResiliencePolicyProvider, ResiliencePolicyProvider>()
            .AddSingleton<ICircuitStateProvider, CircuitStateProvider>()
            .AddSingleton<IRetryBudget, RetryBudget>()
            .AddSingleton<IStartupCheck, TimeoutHierarchyCheck>();
}
