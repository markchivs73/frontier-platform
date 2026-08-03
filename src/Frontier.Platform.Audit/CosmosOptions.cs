using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.Audit;

/// <summary>
/// Cosmos DB connection settings (doc 02 §3, doc 12 §4), bound from the <c>Cosmos</c>
/// configuration section and validated at boot by
/// <see cref="AuditServiceCollectionExtensions.AddFrontierAudit"/>. The
/// <see cref="Endpoint"/> and <see cref="Database"/> defaults match the local Cosmos
/// emulator seeded by <c>tools/dev-setup/cosmos-init.py</c> (S9.19); <see cref="Key"/>
/// has no default and must be supplied via user-secrets locally or a Key Vault
/// reference when deployed.
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
