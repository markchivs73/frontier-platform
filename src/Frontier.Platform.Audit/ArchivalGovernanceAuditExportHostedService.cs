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
[ExcludeFromCodeCoverage(Justification = "Cosmos change-feed SDK adapter (ADR-PA30); lifecycle logic lives in ChangeFeedProcessorLifecycle, exercised against the emulator and Azurite, not the unit-coverage gate.")]
internal sealed class ArchivalGovernanceAuditExportHostedService : IHostedService
{
    private readonly CosmosClient client;
    private readonly IOptions<CosmosOptions> options;
    private readonly IAuditRecordExporter exporter;
    private readonly ChangeFeedProcessorLifecycle lifecycle;

    /// <summary>The Blob container for governance archives.</summary>
    internal const string BlobContainerName = "governance-audit-records-archive";

    /// <summary>The processor name, which is the lease prefix in <c>archival-leases</c>.</summary>
    internal const string ProcessorName = "archival-governance-audit-records";

    /// <summary>Creates the service over the Cosmos client, options and archive exporter.</summary>
    public ArchivalGovernanceAuditExportHostedService(CosmosClient client, IOptions<CosmosOptions> options, IAuditRecordExporter exporter)
    {
        this.client = client;
        this.options = options;
        this.exporter = exporter;
        lifecycle = new ChangeFeedProcessorLifecycle(BuildProcessorAsync, TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = lifecycle.StartWithRetryAsync(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => lifecycle.StopAsync();

    private async Task<ChangeFeedProcessor> BuildProcessorAsync(CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(options.Value.Database);
        var handler = new GovernanceAuditArchivalHandler(new ArchivalAuditChangeFeedHandler(exporter, BlobContainerName));
        var processor = database.GetContainer(CosmosGovernanceAuditStore.ContainerName)
            .GetChangeFeedProcessorBuilder<JsonObject>(ProcessorName, handler.HandleChangesAsync)
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(database.GetContainer("archival-leases"))
            .WithPollInterval(TimeSpan.FromMilliseconds(500))
            .Build();
        await processor.StartAsync();
        return processor;
    }
}

/// <summary>Filters a governance change-feed batch to signed records before archiving (ADR-PA30).</summary>
internal sealed class GovernanceAuditArchivalHandler(ArchivalAuditChangeFeedHandler inner)
{
    /// <summary>Archives the signed records in <paramref name="changes"/>; heads and markers are skipped.</summary>
    internal Task HandleChangesAsync(IReadOnlyCollection<JsonObject> changes, CancellationToken cancellationToken) =>
        inner.HandleChangesAsync([.. changes.Where(GovernanceAuditDocumentId.IsRecordDocument)], cancellationToken);
}
