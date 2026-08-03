namespace Frontier.Platform.Audit;

/// <summary>
/// Writes a single signed audit record's canonical bytes to immutable Blob storage
/// (doc 05 §8): for compliance, SIEM export, and tampering-detection comparison copies.
/// Consumer-owned so change-feed handler can be unit-tested without <c>BlobServiceClient</c>
/// (engineering-standards: no SDK types on interfaces).
/// </summary>
internal interface IAuditRecordExporter
{
    /// <summary>
    /// Uploads <paramref name="recordBytes"/> to <paramref name="blobName"/> within
    /// <paramref name="containerName"/>, overwriting any existing blob — re-processing a
    /// change-feed event for the same execution id is convergent (cosmos-conventions).
    /// </summary>
    Task ExportAsync(string containerName, string blobName, ReadOnlyMemory<byte> recordBytes, CancellationToken cancellationToken);
}
