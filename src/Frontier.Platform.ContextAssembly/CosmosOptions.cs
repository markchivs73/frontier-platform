using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Cosmos DB connection settings (doc 02 §3, doc 12 §4), bound from the <c>Cosmos</c>
/// configuration section and validated at boot by
/// <see cref="ContextAssemblyServiceCollectionExtensions.AddFrontierCosmosEngagementContext"/>.
/// Mirrors the sibling libraries' options exactly — each Cosmos-using library binds its own so
/// none depends on another's registration order.
/// </summary>
public sealed class CosmosOptions
{
    /// <summary>The Cosmos account/emulator gateway endpoint.</summary>
    [Required, Url]
    public string Endpoint { get; init; } = "https://localhost:8081";

    /// <summary>The database name (doc 02 §3).</summary>
    [Required]
    public string Database { get; init; } = "frontier-workflow";

    /// <summary>The Cosmos account/emulator master key.</summary>
    [Required]
    public string Key { get; init; } = "";
}
