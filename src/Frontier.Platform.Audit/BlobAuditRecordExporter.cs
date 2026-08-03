using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditRecordExporter"/> over <see cref="BlobServiceClient"/> (doc 05 §8):
/// immutable Blob container for compliance archives, SIEM export, and tampering-detection
/// comparison copies. Creates the target Blob container on first use (unlike Cosmos,
/// Blob containers have no pre-provisioning step).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Blob SDK adapter (doc 05 §8); exercised by the Audit archival integration test against Azurite (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class BlobAuditRecordExporter(BlobServiceClient client) : IAuditRecordExporter
{
    /// <inheritdoc />
    public async Task ExportAsync(string containerName, string blobName, ReadOnlyMemory<byte> recordBytes, CancellationToken cancellationToken)
    {
        var container = client.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        using var stream = new MemoryStream(recordBytes.ToArray());
        await container.GetBlobClient(blobName).UploadAsync(stream, overwrite: true, cancellationToken);
    }
}
