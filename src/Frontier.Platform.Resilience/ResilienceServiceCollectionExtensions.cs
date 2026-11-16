using Frontier.Platform.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frontier.Platform.Resilience;

/// <summary>
/// DI registration for the Resilience library (engineering-standards: each library
/// wires its own internals; only Host calls these extensions).
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFailureClassifier"/>, <see cref="IResiliencePolicyProvider"/>,
    /// <see cref="IHttpResilienceHandlerFactory"/> (over the same provider, so HTTP pipelines are cached once per worker),
    /// <see cref="ICircuitStateProvider"/>, <see cref="IRetryBudget"/>, and the
    /// <see cref="TimeoutHierarchyCheck"/> boot invariant (doc 12 §6). <see cref="ICircuitStateProvider"/>
    /// is the same instance that tracks the policy provider's endpoint breakers. All are
    /// stateless or in-process-stateful singletons over the compiled-in
    /// <see cref="Phase1ResilienceProfileCatalogue"/> (doc 10 §9 cold-start fallback) —
    /// no configuration to bind for Phase 1.
    /// </summary>
    public static IServiceCollection AddFrontierResilience(this IServiceCollection services)
    {
        // TryAdd so a consumer that already registered a TimeProvider keeps theirs.
        services.TryAddSingleton(TimeProvider.System);

        return services
            .AddSingleton<IFailureClassifier, FailureClassifier>()
            .AddSingleton<ResiliencePolicyProvider>()
            .AddSingleton<IResiliencePolicyProvider>(sp => sp.GetRequiredService<ResiliencePolicyProvider>())
            .AddSingleton<IHttpResilienceHandlerFactory, HttpResilienceHandlerFactory>()
            .AddSingleton<CircuitStateProvider>()
            .AddSingleton<ICircuitStateProvider>(sp => sp.GetRequiredService<CircuitStateProvider>())
            .AddSingleton<IRetryBudget, RetryBudget>()
            .AddSingleton<IStartupCheck, TimeoutHierarchyCheck>();
    }
}
