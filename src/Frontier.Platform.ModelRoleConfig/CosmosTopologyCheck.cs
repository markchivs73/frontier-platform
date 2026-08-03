using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Boot check (doc 12 §6, doc 08 §6): confirms the <c>model-role-config</c> container
/// exists with partition key <c>/role_id</c> — the shape <see cref="CosmosRoleRegistry"/>
/// is built against (canonical snake_case wire name for <see cref="RoleMappingDocument.RoleId"/>,
/// doc 01).
/// </summary>
internal sealed class CosmosTopologyCheck(CosmosClient client, IOptions<CosmosOptions> options) : IStartupCheck
{
    /// <summary>The containers this library owns, and the partition key path each must have (doc 08 §6).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ExpectedContainers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CosmosRoleRegistry.ContainerName] = "/role_id",
    };

    /// <inheritdoc />
    public string Name => "CosmosTopology:ModelRoleConfig";

    /// <inheritdoc />
    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 08 §6); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
    public async Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(options.Value.Database);
        var actual = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var containerName in ExpectedContainers.Keys)
        {
            actual[containerName] = await TryReadPartitionKeyPathAsync(database, containerName, cancellationToken);
        }

        return Evaluate(ExpectedContainers, actual);
    }

    /// <summary>Reads a container's partition key path, or <see langword="null"/> if the container does not exist.</summary>
    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 08 §6); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
    internal static async Task<string?> TryReadPartitionKeyPathAsync(Database database, string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await database.GetContainer(containerName).ReadContainerAsync(cancellationToken: cancellationToken);
            return response.Resource.PartitionKeyPath;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Compares <paramref name="actual"/> partition key paths (or <see langword="null"/>
    /// for a missing container) against <paramref name="expected"/>.
    /// </summary>
    internal static StartupCheckResult Evaluate(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string?> actual)
    {
        foreach (var (containerName, expectedPartitionKeyPath) in expected)
        {
            if (!actual.TryGetValue(containerName, out var actualPartitionKeyPath) || actualPartitionKeyPath is null)
            {
                return StartupCheckResult.Fail($"Cosmos container '{containerName}' not found (doc 08 §6, doc 12 §6).");
            }

            if (!string.Equals(actualPartitionKeyPath, expectedPartitionKeyPath, StringComparison.Ordinal))
            {
                return StartupCheckResult.Fail(
                    $"Cosmos container '{containerName}' has partition key '{actualPartitionKeyPath}', expected '{expectedPartitionKeyPath}' (doc 08 §6, doc 12 §6).");
            }
        }

        return StartupCheckResult.Pass();
    }
}
