using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// The governance counterpart of <see cref="ArchivalAuditExportHostedService"/> (ADR-PA30): a change-feed
/// processor on <c>governance-audit-records</c>, under its own lease prefix, copying each signed record's
/// canonical bytes to the <c>governance-audit-records-archive</c> Blob container. Heads and record-id
/// markers are not archived; see <see cref="GovernanceAuditArchivalHandler"/>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos change-feed SDK adapter (ADR-PA30); mirrors ArchivalAuditExportHostedService, exercised against the emulator and Azurite, not the unit-coverage gate.")]
internal sealed class ArchivalGovernanceAuditExportHostedService(
    CosmosClient client,
    IOptions<CosmosOptions> options,
    IAuditRecordExporter exporter) : IHostedService
{
    private ChangeFeedProcessor? processor;

    /// <summary>The Blob container for governance archives.</summary>
    internal const string BlobContainerName = "governance-audit-records-archive";

    /// <summary>The processor name, which is the lease prefix in <c>archival-leases</c>.</summary>
    internal const string ProcessorName = "archival-governance-audit-records";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = StartProcessorWithRetryAsync(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (processor is not null)
        {
            await processor.StopAsync();
        }
    }

    private async Task StartProcessorWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var database = client.GetDatabase(options.Value.Database);
                var handler = new GovernanceAuditArchivalHandler(new ArchivalAuditChangeFeedHandler(exporter, BlobContainerName));
                processor = database.GetContainer(CosmosGovernanceAuditStore.ContainerName)
                    .GetChangeFeedProcessorBuilder<JsonObject>(ProcessorName, handler.HandleChangesAsync)
                    .WithInstanceName(Environment.MachineName)
                    .WithLeaseContainer(database.GetContainer("archival-leases"))
                    .WithPollInterval(TimeSpan.FromMilliseconds(500))
                    .Build();
                await processor.StartAsync();
                return;
            }
#pragma warning disable CA1031 // Retry loop must catch any transient startup failure, as ArchivalAuditExportHostedService does
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Filters a governance change-feed batch to signed records before archiving (ADR-PA30).</summary>
internal sealed class GovernanceAuditArchivalHandler(ArchivalAuditChangeFeedHandler inner)
{
    /// <summary>Archives the signed records in <paramref name="changes"/>; heads and markers are skipped.</summary>
    internal Task HandleChangesAsync(IReadOnlyCollection<JsonObject> changes, CancellationToken cancellationToken) =>
        inner.HandleChangesAsync([.. changes.Where(GovernanceAuditDocumentId.IsRecordDocument)], cancellationToken);
}
