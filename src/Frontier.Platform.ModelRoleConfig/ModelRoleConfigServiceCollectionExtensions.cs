using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// DI registration for the Model-Role Config library (engineering-standards: each
/// library wires its own internals; only Host calls these extensions).
/// </summary>
public static class ModelRoleConfigServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="CosmosOptions"/> (doc 12 §4 "options with teeth"),
    /// and registers the <see cref="CosmosClient"/>, <see cref="IRoleRegistry"/>,
    /// <see cref="IModelResolver"/>, <see cref="IMappingGovernanceService"/>, and the
    /// <see cref="CosmosTopologyCheck"/> and <see cref="RoleCatalogueCheck"/> boot
    /// invariants (doc 12 §6).
    /// </summary>
    public static IServiceCollection AddFrontierModelRoleConfig(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection("Cosmos"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddSingleton(CreateCosmosClient)
            .AddSingleton<CosmosRoleRegistry>()
            .AddSingleton<IRoleRegistry>(sp => sp.GetRequiredService<CosmosRoleRegistry>())
            .AddSingleton<IRoleMappingWriter>(sp => sp.GetRequiredService<CosmosRoleRegistry>())
            .AddSingleton<ICircuitBreakerQuery, AlwaysClosedCircuitBreakerQuery>()
            .AddSingleton<IModelResolver, ModelResolver>()
            .AddSingleton<IMappingGovernanceService, MappingGovernanceService>()
            .AddSingleton<IStartupCheck, CosmosTopologyCheck>()
            .AddSingleton<IStartupCheck, RoleCatalogueCheck>();

        return services;
    }

    /// <summary>
    /// Builds the <see cref="CosmosClient"/> wired to the shared <see cref="CanonicalProfile"/>
    /// (canonical-serialization: no other <c>JsonSerializerOptions</c> instance may be
    /// constructed anywhere). <see cref="ConnectionMode.Gateway"/> matches every other
    /// Cosmos client in this codebase and is required by the Linux Cosmos DB emulator,
    /// which does not expose the Direct/TCP port range.
    /// </summary>
    internal static CosmosClient CreateCosmosClient(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
        var clientOptions = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
            ConnectionMode = ConnectionMode.Gateway,
            // The Cosmos emulator uses a self-signed TLS cert that is not in the OS trust
            // store. Bypass validation when connecting to localhost (emulator only).
            HttpClientFactory = IsLocalEmulator(options.Endpoint)
                ? () => new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true, ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })
                : null
        };

        return new CosmosClient(options.Endpoint, options.Key, clientOptions);
    }

    private static bool IsLocalEmulator(string endpoint) =>
        endpoint.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
