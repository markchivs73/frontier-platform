using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// <see cref="IKillSwitch"/> over the consumer's <c>platform-config</c> container (doc 07 §5a, S13.187): one
/// fleet-scope <see cref="KillSwitchDocument"/>, so a switch thrown on the API head is the state every worker
/// reads. Reads are uncached; admission caches them through <see cref="KillSwitchGate"/>. Activate and resume
/// upsert the whole document, so the last governed write wins.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter; its decisions live in KillSwitchDocument and CosmosItemNotFound, and it is covered by CosmosKillSwitchIntegrationTests against the emulator, which the unit coverage gate does not run.")]
internal sealed class CosmosKillSwitch : IKillSwitch
{
    private static readonly PartitionKey Partition = new(KillSwitchDocument.ConfigTypeValue);

    private readonly Container container;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the switch over <paramref name="platformConfigContainer"/> (partition key <c>/config_type</c>), stamping changes with <paramref name="timeProvider"/>.</summary>
    public CosmosKillSwitch(Container platformConfigContainer, TimeProvider timeProvider)
    {
        container = platformConfigContainer ?? throw new ArgumentNullException(nameof(platformConfigContainer));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    /// <remarks>Only a missing item reads as not thrown; any other failure, a missing container included, is thrown.</remarks>
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<KillSwitchDocument>(KillSwitchDocument.FleetId, Partition, cancellationToken: cancellationToken).ConfigureAwait(false);
            return KillSwitchDocument.IsActive(response.Resource);
        }
        catch (CosmosException ex) when (CosmosItemNotFound.IsMissingItem(ex.StatusCode, ex.SubStatusCode))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task ActivateAsync(string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return container.UpsertItemAsync(KillSwitchDocument.Activated(reason, timeProvider.GetUtcNow().UtcDateTime), Partition, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken) =>
        container.UpsertItemAsync(KillSwitchDocument.Resumed(timeProvider.GetUtcNow().UtcDateTime), Partition, cancellationToken: cancellationToken);
}
