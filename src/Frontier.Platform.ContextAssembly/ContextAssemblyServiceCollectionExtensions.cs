using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// DI registration for Frontier.Platform.ContextAssembly.
/// Only Host calls this (library-boundaries: composition root only).
/// </summary>
public static class ContextAssemblyServiceCollectionExtensions
{
    /// <summary>
    /// Register ContextAssembly services (context assembler, caching strategies, stores, debugger).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional: configure ContextAssemblyOptions (baseline catalogue ID, tier max tokens, etc.).</param>
    /// <returns>The service collection (for chaining).</returns>
    public static IServiceCollection AddFrontierContextAssembly(
        this IServiceCollection services,
        Action<ContextAssemblyOptions>? configure = null)
    {
        // Register options (with validation if configured)
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Register option validation
        services.AddOptions<ContextAssemblyOptions>()
            .ValidateOnStart();

        // Register stores. Phase1* implementations are the compiled-in PoC catalogue
        // (S4.2); Cosmos-backed implementations (config-store conventions) replace these
        // once a second baseline catalogue or cross-process dynamic context is needed.
        services.AddSingleton<IBaselineCatalogueStore, Phase1BaselineCatalogueStore>();
        services.AddSingleton<IEngagementContextStore, Phase1EngagementContextStore>();

        // Register caching strategy registry with fallback
        services.AddSingleton<CachingStrategyRegistry>(sp =>
        {
            var registry = new CachingStrategyRegistry(NoCachingStrategy.Instance);

            // S4.2: the PoC Gate 3 agents resolve to "anthropic" via Model-Role Config
            // (Phase1RoleCatalogue), so CachingMetadata.ProviderId is always "anthropic"
            // initially. S6.2c: register OpenAI as provider default for fallback.
            registry.Register("anthropic", "claude-*", versionPattern: null, new AnthropicCachingStrategy());
            registry.Register("openai", modelPattern: "*", versionPattern: null, new OpenAiCachingStrategy());

            return registry;
        });

        // Register as interface (for DI of orchestration activities)
        services.AddSingleton<ICachingStrategyRegistry>(sp => sp.GetRequiredService<CachingStrategyRegistry>());

        // Register context assembler (S3.3 ADR-CR1)
        services.AddTransient<IContextAssembler, ContextAssemblerSimple>();

        // Register dynamic context refresher (S6.2b ADR-CR1 primitive)
        services.AddTransient<IDynamicContextRefresher, DynamicContextRefresher>();

        // Register context validator (S6.2c unknown component validation)
        services.AddSingleton<IContextValidator, ContextValidator>();

        // Register debugger (S3.5 debugging, S6.2c structured comparison)
        services.AddSingleton<IContextDebugger, ContextDebugger>();

        return services;
    }
}
