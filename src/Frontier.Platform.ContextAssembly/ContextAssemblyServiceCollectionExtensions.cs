using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <summary>
    /// Replaces the compiled-in Phase-1 engagement-context store with the durable Cosmos-backed
    /// one (doc 04, doc 18; epoch-versioned, <c>engagement-context</c> container).
    /// <para>
    /// <b>Why this is opt-in rather than the default:</b> <see cref="AddFrontierContextAssembly"/>
    /// deliberately needs no Cosmos configuration, so a consumer can compose context assembly in a
    /// test or a tool without a database. A solution that runs real engagements calls this as well.
    /// Until it existed the Cosmos store was <c>internal</c> and registered nowhere — the durable
    /// half of the design shipped unreachable, and every engagement outside the compiled-in
    /// catalogue resolved to no dynamic context at all (frontier-workflow S13.50).
    /// </para>
    /// <para>
    /// The <see cref="CosmosClient"/> is registered with <c>TryAdd</c>, so a consumer that already
    /// shares one keeps it; the container is resolved from <see cref="CosmosOptions.Database"/>.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration carrying the <c>Cosmos</c> section.</param>
    public static IServiceCollection AddFrontierCosmosEngagementContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection("Cosmos"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(CreateCosmosClient);

        // Replace, not append: leaving the Phase-1 store registered would make the resolved
        // implementation depend on call order, which is exactly the kind of thing that is true
        // in one head and false in the other.
        services.Replace(ServiceDescriptor.Singleton<IEngagementContextStore>(provider =>
        {
            var client = provider.GetRequiredService<CosmosClient>();
            var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
            return new CosmosEngagementContextStore(
                client.GetContainer(options.Database, CosmosEngagementContextStore.ContainerName));
        }));

        return services;
    }

    /// <summary>
    /// Builds the <see cref="CosmosClient"/> wired to the shared <see cref="CanonicalProfile"/>
    /// (canonical-serialization: no other <c>JsonSerializerOptions</c> instance may be constructed
    /// anywhere). <see cref="ConnectionMode.Gateway"/> matches every other Cosmos client here and
    /// is required by the Linux emulator, which exposes no Direct/TCP port range.
    /// </summary>
    internal static CosmosClient CreateCosmosClient(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
        var clientOptions = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = IsLocalEmulator(options.Endpoint)
                ? () => new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true, ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })
                : null,
        };

        return new CosmosClient(options.Endpoint, options.Key, clientOptions);
    }

    /// <summary>The emulator's self-signed certificate is not in the OS trust store; validation is bypassed for localhost only.</summary>
    internal static bool IsLocalEmulator(string endpoint) =>
        endpoint.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);

}
