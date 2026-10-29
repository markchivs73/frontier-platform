using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Contracts;

/// <summary>DI registration for the contracts library. Only the composition root calls it.</summary>
public static class ContractsServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="ContractsOptions"/> from the <c>Contracts</c> section, and
    /// registers <see cref="IContractValidator"/> and <see cref="IContractRegistrationCheck"/> over
    /// the JSON Schema engine.
    /// </summary>
    public static IServiceCollection AddFrontierContracts(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ContractsOptions>()
            .Bind(configuration.GetSection(ContractsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services
            .AddSingleton<ISchemaEngine, CorvusSchemaEngine>()
            .AddSingleton<IContractValidator, ContractValidator>()
            .AddSingleton<IContractRegistrationCheck, ContractRegistrationCheck>();
    }
}
