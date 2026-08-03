using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// Hosted service (doc 05 §8, doc 12 §2 "library decides how it wires itself") running
/// a Cosmos change-feed processor on <c>audit-records</c> → immutable Blob container
/// for compliance archives, SIEM export, and tampering-detection comparison copies.
/// Registered by <see cref="AuditServiceCollectionExtensions.AddFrontierAudit"/> for
/// both worker and API heads; started/stopped with the host.
///
/// <para>
/// This implements doc 05 §8's "change feed → Blob (immutable) immediately" half for
/// audit records. The complementary "quarterly SIEM export" half and sector-specific
/// retention policy configuration (ADR-A1) are deployment concerns (S10.1).
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos change-feed SDK adapter (doc 05 §8); exercised by the Audit archival integration test against the Cosmos emulator + Azurite (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class ArchivalAuditExportHostedService(
    CosmosClient client,
    IOptions<CosmosOptions> options,
    IAuditRecordExporter exporter) : IHostedService
{
    private ChangeFeedProcessor? processor;

    /// <summary>Blob container name for immutable audit record archives (doc 05 §8).</summary>
    internal const string AuditBlobContainerName = "audit-records-archive";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start processor in background so IHost.StartAsync doesn't block or crash if
        // the Cosmos endpoint isn't reachable at startup (e.g. emulator still initialising).
        _ = StartProcessorWithRetryAsync(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task StartProcessorWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var database = client.GetDatabase(options.Value.Database);
                var leaseContainer = database.GetContainer("archival-leases");
                processor = await BuildProcessorAsync(database, leaseContainer, cancellationToken);
                return;
            }
#pragma warning disable CA1031 // Retry loop must catch any transient startup failure (SSL, HTTP, Cosmos) without knowing all concrete types the SDK may throw
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (processor is not null)
        {
            await processor.StopAsync();
        }
    }

    /// <summary>Builds and starts a change-feed processor copying audit-records into the archive Blob container.</summary>
    internal async Task<ChangeFeedProcessor> BuildProcessorAsync(Database database, Container leaseContainer, CancellationToken cancellationToken)
    {
        var handler = new ArchivalAuditChangeFeedHandler(exporter, AuditBlobContainerName);
        var processor = database.GetContainer(CosmosAuditRecordStore.ContainerName)
            .GetChangeFeedProcessorBuilder<JsonObject>($"archival-{CosmosAuditRecordStore.ContainerName}", handler.HandleChangesAsync)
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(leaseContainer)
            .WithPollInterval(TimeSpan.FromMilliseconds(500))
            .Build();

        await processor.StartAsync();
        return processor;
    }
}
