using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The Cosmos containers the compiler's stores use, in the configured database (<see cref="CosmosOptions"/>).
/// One place names them, so the DI registration cannot drift back to a fixed database name.
/// </summary>
internal static class CompilerContainers
{
    /// <summary>The <c>workflow-definitions</c> container the definition store reads and writes.</summary>
    internal static Container Definitions(CosmosClient client, IOptions<CosmosOptions> options) =>
        In(client, options, "workflow-definitions");

    /// <summary>The <c>execution-snapshots</c> container the retirement monitor reads.</summary>
    internal static Container Snapshots(CosmosClient client, IOptions<CosmosOptions> options) =>
        In(client, options, "execution-snapshots");

    private static Container In(CosmosClient client, IOptions<CosmosOptions> options, string container)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        return client.GetDatabase(options.Value.Database).GetContainer(container);
    }
}
