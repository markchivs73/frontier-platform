using System.Text;
using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Change-feed handler for <c>audit-records</c> (doc 05 §8): strips the Cosmos-injected
/// system metadata fields from each new/modified audit record and exports the remaining
/// canonical bytes to immutable Blob storage (for compliance archives, SIEM export,
/// tampering-detection comparison). Overwriting on reprocess is convergent
/// (cosmos-conventions) — the record's deterministic id ensures idempotency.
/// </summary>
internal sealed class ArchivalAuditChangeFeedHandler(IAuditRecordExporter exporter, string blobContainerName)
{
    /// <summary>Cosmos system metadata fields (doc 01: never part of a contract's canonical bytes).</summary>
    internal static readonly string[] SystemMetadataProperties = ["_rid", "_self", "_etag", "_attachments", "_ts", "_lsn"];

    /// <summary>Exports every changed audit record in <paramref name="changes"/> (the change-feed processor's per-batch callback).</summary>
    internal async Task HandleChangesAsync(IReadOnlyCollection<JsonObject> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            await ExportAsync(change, cancellationToken);
        }
    }

    /// <summary>Strips Cosmos system metadata from <paramref name="recordDocument"/> and exports the remaining canonical bytes under the record's execution id.</summary>
    internal async Task ExportAsync(JsonObject recordDocument, CancellationToken cancellationToken)
    {
        var id = recordDocument["id"]!.GetValue<string>();

        foreach (var property in SystemMetadataProperties)
        {
            recordDocument.Remove(property);
        }

        var bytes = Encoding.UTF8.GetBytes(recordDocument.ToJsonString(CanonicalProfile.Options));
        await exporter.ExportAsync(blobContainerName, id, bytes, cancellationToken);
    }
}
