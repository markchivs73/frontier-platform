using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Cosmos-backed <see cref="IEngagementContextStore"/> (S6.2a, doc 04 §6): reads/writes
/// dynamic context via the <c>engagement-context</c> container with epoch-based versioning.
/// Each upsert increments the epoch and updates the mutable `:current` pointer.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 04 §6); exercised by the ContextAssembly integration tests against the Cosmos emulator (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class CosmosEngagementContextStore : IEngagementContextStore
{
    private readonly Container container;

    /// <summary>The Cosmos container name (<c>engagement-context</c>).</summary>
    internal const string ContainerName = "engagement-context";

    /// <summary>Suffix for the current pointer document id: <c>{engagementId}:ctx:current</c>.</summary>
    private const string CurrentPointerSuffix = ":ctx:current";

    /// <summary>Prefix for epoch documents: <c>{engagementId}:ctx:e{epoch:D6}</c>.</summary>
    private const string EpochPrefix = ":ctx:e";

    /// <summary>Initializes the store with a reference to the engagement-context container.</summary>
    public CosmosEngagementContextStore(Container container)
    {
        this.container = container ?? throw new ArgumentNullException(nameof(container));
    }

    /// <inheritdoc />
    public async Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);

        try
        {
            // Read the current pointer to get the active epoch
            var pointerId = $"{engagementId}{CurrentPointerSuffix}";
            var pointerResponse = await container.ReadItemAsync<EngagementContextPointer>(
                pointerId,
                new PartitionKey(engagementId),
                cancellationToken: ct);

            if (pointerResponse.Resource is null)
                return null;

            // Read the epoch document
            var epochId = $"{engagementId}{EpochPrefix}{pointerResponse.Resource.Epoch:D6}";
            var epochResponse = await container.ReadItemAsync<EngagementContextEpoch>(
                epochId,
                new PartitionKey(engagementId),
                cancellationToken: ct);

            return epochResponse.Resource?.Content;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentNullException.ThrowIfNull(dynamicContent);

        // Compute hash of the new content
        var contentHash = CanonicalProfile.Hash(dynamicContent);

        try
        {
            // Read the current pointer to get the next epoch number
            var pointerId = $"{engagementId}{CurrentPointerSuffix}";
            var nextEpoch = 0;

            try
            {
                var pointerResponse = await container.ReadItemAsync<EngagementContextPointer>(
                    pointerId,
                    new PartitionKey(engagementId),
                    cancellationToken: ct);

                if (pointerResponse.Resource is not null)
                    nextEpoch = pointerResponse.Resource.Epoch + 1;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // First write; epoch starts at 0
                nextEpoch = 0;
            }

            // Write the new epoch document (append-only)
            var epochId = $"{engagementId}{EpochPrefix}{nextEpoch:D6}";
            var epochDoc = new EngagementContextEpoch(
                epochId,
                engagementId,
                nextEpoch,
                contentHash,
                dynamicContent,
                DateTime.UtcNow);

            await container.CreateItemAsync(epochDoc, new PartitionKey(engagementId), cancellationToken: ct);

            // Update (or create) the current pointer
            var pointerDoc = new EngagementContextPointer(
                pointerId,
                engagementId,
                nextEpoch,
                contentHash,
                DateTime.UtcNow);

            await container.UpsertItemAsync(pointerDoc, new PartitionKey(engagementId), cancellationToken: ct);
            return nextEpoch;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Concurrent write: epoch already exists (same epoch number)
            // This is fine (idempotent); re-read pointer and return its epoch
            var pointerId = $"{engagementId}{CurrentPointerSuffix}";
            var pointerResponse = await container.ReadItemAsync<EngagementContextPointer>(
                pointerId,
                new PartitionKey(engagementId),
                cancellationToken: ct);
            return pointerResponse.Resource?.Epoch ?? 0;
        }
    }
}
