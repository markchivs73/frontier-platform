using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ContextAssembly;

/// <summary><see cref="IEngagementContextDocuments"/> over the <c>engagement-context</c> container (partition key <c>/engagementId</c>).</summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK glue (doc 04 §6): one SDK call per member and a 404/409 translation, its decisions live in CosmosEngagementContextStore; exercised by the ContextAssembly integration tests against the Cosmos emulator (CI integration job), not the unit-coverage gate.")]
internal sealed class CosmosEngagementContextDocuments(Container container) : IEngagementContextDocuments
{
    private readonly Container container = container ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    public Task<EngagementContextPointer?> ReadPointerAsync(EngagementId engagementId, CancellationToken ct) =>
        ReadOrNullAsync<EngagementContextPointer>(CosmosEngagementContextStore.PointerId(engagementId), engagementId, ct);

    /// <inheritdoc />
    public Task<EngagementContextEpoch?> ReadEpochAsync(EngagementId engagementId, string epochId, CancellationToken ct) =>
        ReadOrNullAsync<EngagementContextEpoch>(epochId, engagementId, ct);

    /// <inheritdoc />
    public async Task<bool> TryCreateEpochAsync(EngagementContextEpoch epoch, CancellationToken ct)
    {
        try
        {
            await container.CreateItemAsync(epoch, new PartitionKey(epoch.EngagementId), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task UpsertPointerAsync(EngagementContextPointer pointer, CancellationToken ct) =>
        container.UpsertItemAsync(pointer, new PartitionKey(pointer.EngagementId), cancellationToken: ct);

    /// <summary>Point-reads <paramref name="id"/>; <see langword="null"/> on a 404.</summary>
    private async Task<T?> ReadOrNullAsync<T>(string id, EngagementId engagementId, CancellationToken ct)
        where T : class
    {
        try
        {
            return (await container.ReadItemAsync<T>(id, new PartitionKey(engagementId), cancellationToken: ct)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
