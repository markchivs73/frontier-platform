using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The Cosmos database holding the guardrails' containers, bound from the <c>Cosmos</c> configuration section by
/// <see cref="GuardrailsServiceCollectionExtensions.AddFrontierGuardrails(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, TimeSpan)"/>,
/// as the other platform libraries bind theirs. The guardrails resolve their <c>CosmosClient</c> from the container,
/// so only the database name is their own. The default matches the local emulator seeded by the consumer's dev setup.
/// </summary>
public sealed class CosmosOptions
{
    /// <summary>The database holding <c>guardrail-ledger</c> and <c>platform-config</c>.</summary>
    [Required]
    public string Database { get; init; } = "frontier-workflow";
}
