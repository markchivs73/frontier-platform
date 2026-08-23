using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// Boot check (doc 12 §6, doc 05 §9): confirms the <c>audit-telemetry-staging</c> and
/// <c>audit-records</c> containers exist with their expected partition keys — the shapes
/// <see cref="CosmosAuditTelemetryStaging"/> and <see cref="CosmosAuditRecordStore"/> are
/// built against (canonical snake_case wire names, doc 01). The <c>execution-snapshots</c>
/// row moved with the reader''s deletion (S11.6, ADR-PA2): ArtifactState owns that
/// container and its topology check; the audit consolidator reads snapshots through
/// Orchestration''s <c>IExecutionSnapshotReader</c> port, adapted in Host.
/// </summary>
internal sealed class CosmosTopologyCheck(CosmosClient client, IOptions<CosmosOptions> options) : IStartupCheck
{
    /// <summary>The containers this library owns or reads, and the partition key path each must have (doc 05 §6, §9, doc 02 §3).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ExpectedContainers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CosmosAuditTelemetryStaging.ContainerName] = "/execution_id",
        [CosmosAuditRecordStore.ContainerName] = "/engagement_id",
    };

    /// <inheritdoc />
    public string Name => "CosmosTopology:Audit";

    /// <inheritdoc />
    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §9); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
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
    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §9); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
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
                return StartupCheckResult.Fail($"Cosmos container '{containerName}' not found (doc 05 §9, doc 12 §6).");
            }

            if (!string.Equals(actualPartitionKeyPath, expectedPartitionKeyPath, StringComparison.Ordinal))
            {
                return StartupCheckResult.Fail(
                    $"Cosmos container '{containerName}' has partition key '{actualPartitionKeyPath}', expected '{expectedPartitionKeyPath}' (doc 05 §9, doc 12 §6).");
            }
        }

        return StartupCheckResult.Pass();
    }
}
