using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit.Tests;

/// <summary>Tests for <see cref="ArchivalAuditChangeFeedHandler"/> (S6.3).</summary>
public sealed class ArchivalAuditChangeFeedHandlerTests
{
    [Fact]
    public async Task ExportAsync_StripsCosmosMetadata_AndExportsCanonicalBytes()
    {
        var exporter = new FakeAuditRecordExporter();
        var handler = new ArchivalAuditChangeFeedHandler(exporter, "test-container");

        var document = new JsonObject
        {
            ["id"] = "ENGAGEMENT-123::audit",
            ["engagement_id"] = "ENGAGEMENT-123",
            ["execution_id"] = "ENGAGEMENT-123::workflow",
            ["record"] = new JsonObject
            {
                ["schema_version"] = "1.0",
                ["execution_id"] = "ENGAGEMENT-123::workflow",
            },
            ["_rid"] = "system-rid",
            ["_self"] = "system-self",
            ["_etag"] = "system-etag",
            ["_attachments"] = "attachments",
            ["_ts"] = 1234567890,
            ["_lsn"] = 100,
        };

        await handler.ExportAsync(document, CancellationToken.None);

        Assert.Single(exporter.ExportedBlobs);
        var (containerName, blobName, bytes) = exporter.ExportedBlobs[0];

        Assert.Equal("test-container", containerName);
        Assert.Equal("ENGAGEMENT-123::audit", blobName);

        // Verify Cosmos metadata was stripped
        var exportedJson = JsonNode.Parse(bytes.ToArray());
        Assert.Null(exportedJson!["_rid"]);
        Assert.Null(exportedJson["_self"]);
        Assert.Null(exportedJson["_etag"]);
        Assert.Null(exportedJson["_ts"]);

        // Verify business properties remain
        Assert.Equal("ENGAGEMENT-123::audit", exportedJson["id"]!.GetValue<string>());
        Assert.NotNull(exportedJson["record"]);
    }

    [Fact]
    public async Task HandleChangesAsync_ExportsMultipleDocuments()
    {
        var exporter = new FakeAuditRecordExporter();
        var handler = new ArchivalAuditChangeFeedHandler(exporter, "archive");

        var changes = new JsonObject[]
        {
            new()
            {
                ["id"] = "exec-1::audit",
                ["engagement_id"] = "eng-1",
                ["_rid"] = "rid1",
                ["_ts"] = 100,
            },
            new()
            {
                ["id"] = "exec-2::audit",
                ["engagement_id"] = "eng-2",
                ["_rid"] = "rid2",
                ["_ts"] = 200,
            },
        };

        await handler.HandleChangesAsync(changes, CancellationToken.None);

        Assert.Equal(2, exporter.ExportedBlobs.Count);
        Assert.Equal("exec-1::audit", exporter.ExportedBlobs[0].BlobName);
        Assert.Equal("exec-2::audit", exporter.ExportedBlobs[1].BlobName);
    }

    [Fact]
    public async Task HandleChangesAsync_WithEmptyChanges_ProducesNoExports()
    {
        var exporter = new FakeAuditRecordExporter();
        var handler = new ArchivalAuditChangeFeedHandler(exporter, "archive");

        await handler.HandleChangesAsync([], CancellationToken.None);

        Assert.Empty(exporter.ExportedBlobs);
    }

    /// <summary>Fake exporter for testing (no external dependencies).</summary>
    private sealed class FakeAuditRecordExporter : IAuditRecordExporter
    {
        internal List<(string ContainerName, string BlobName, ReadOnlyMemory<byte> Bytes)> ExportedBlobs { get; } = [];

        public Task ExportAsync(string containerName, string blobName, ReadOnlyMemory<byte> recordBytes, CancellationToken cancellationToken)
        {
            ExportedBlobs.Add((containerName, blobName, recordBytes));
            return Task.CompletedTask;
        }
    }
}
