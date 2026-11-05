using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The Cosmos containers the guardrails use, in the configured database (<see cref="CosmosOptions"/>). One place
/// names them, so the DI registration cannot drift to a fixed database name.
/// </summary>
internal static class GuardrailsContainers
{
    /// <summary>The <c>guardrail-ledger</c> container the budget ledger reads and writes (PK <c>/engagement_id</c>).</summary>
    internal static Container Ledger(CosmosClient client, IOptions<CosmosOptions> options) =>
        In(client, options, "guardrail-ledger");

    /// <summary>The <c>platform-config</c> container holding the fleet kill switch (PK <c>/config_type</c>).</summary>
    internal static Container PlatformConfig(CosmosClient client, IOptions<CosmosOptions> options) =>
        In(client, options, "platform-config");

    private static Container In(CosmosClient client, IOptions<CosmosOptions> options, string container)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        return client.GetDatabase(options.Value.Database).GetContainer(container);
    }
}
