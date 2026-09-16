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
    public async Task HandleChangesAsync_SkipsTheChainHeadAndExportsRecordsEitherSideOfIt()
    {
        // ADR-PA31: the head shares the container but is mutable bookkeeping, so it must never reach
        // an immutable archive — and it carries no `record` to export. The three doc_type cases are
        // covered together: absent (written before ADR-PA31), explicit "record", and the head.
        var exporter = new FakeAuditRecordExporter();
        var handler = new ArchivalAuditChangeFeedHandler(exporter, "archive");

        var changes = new JsonObject[]
        {
            new() { ["id"] = "exec-1::audit", ["engagement_id"] = "eng-1" },
            new() { ["id"] = "chain-head:eng-1", ["engagement_id"] = "eng-1", ["doc_type"] = "chain_head", ["sequence"] = 2 },
            new() { ["id"] = "exec-2::audit", ["engagement_id"] = "eng-1", ["doc_type"] = "record" },
        };

        await handler.HandleChangesAsync(changes, CancellationToken.None);

        Assert.Equal(["exec-1::audit", "exec-2::audit"], exporter.ExportedBlobs.Select(blob => blob.BlobName));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("record", true)]
    [InlineData("chain_head", false)]
    public void IsRecordDocument_TreatsAnAbsentDocTypeAsARecordAndExcludesTheHead(string? docType, bool expected)
    {
        var document = new JsonObject { ["id"] = "exec-1::audit" };
        if (docType is not null)
        {
            document["doc_type"] = docType;
        }

        Assert.Equal(expected, ArchivalAuditChangeFeedHandler.IsRecordDocument(document));
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
