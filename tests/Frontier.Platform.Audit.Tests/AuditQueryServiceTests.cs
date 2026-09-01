using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S5.7 tests for <see cref="AuditQueryService"/>'s record-store delegation (doc 05 §10).
/// <see cref="AuditQueryService.QueryAsync"/> is Cosmos-backed and covered by the emulator
/// integration tests, not here.
/// </summary>
public sealed class AuditQueryServiceTests : IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private readonly CosmosClient client = new("https://localhost:8081", EmulatorKey);

    /// <summary>Releases the <see cref="CosmosClient"/> shared by each test's <see cref="AuditQueryService"/>.</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task GetAsync_DelegatesToRecordStore()
    {
        var store = new FakeAuditRecordStore();
        var record = await Sign(store, "eng-1", "wf-1");
        var service = CreateService(store);

        var result = await service.GetAsync(record.ExecutionId, record.EngagementId, CancellationToken.None);

        Assert.Equal(record, result);
    }

    [Fact]
    public async Task GetAsync_NoRecord_ReturnsNull()
    {
        var service = CreateService(new FakeAuditRecordStore());

        var result = await service.GetAsync("eng-1::wf-1", "eng-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetChainAsync_DelegatesToRecordStore()
    {
        var store = new FakeAuditRecordStore();
        var first = await Sign(store, "eng-1", "wf-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = await Sign(store, "eng-1", "wf-2", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);

        var chain = await service.GetChainAsync("eng-1", CancellationToken.None);

        Assert.Equal([first, second], chain);
    }

    /// <summary>
    /// Builds an <see cref="AuditQueryService"/> over <paramref name="store"/>. The Cosmos
    /// client/options are unused by <see cref="AuditQueryService.GetAsync"/>/
    /// <see cref="AuditQueryService.GetChainAsync"/> but required by the constructor.
    /// </summary>
    private AuditQueryService CreateService(FakeAuditRecordStore store) =>
        new(store, client, Options.Create(new CosmosOptions()));

    /// <summary>Builds and persists a well-formed, correctly-signed record for <paramref name="engagementId"/>/<paramref name="workflowId"/> via <paramref name="store"/>.</summary>
    private static async Task<Frontier.Platform.Audit.SignedAuditRecord> Sign(FakeAuditRecordStore store, string engagementId, string workflowId, DateTime? closedAtUtc = null)
    {
        var keyProvider = new DevKeyProvider();
        var record = AuditRecordHasherTests.Sample() with
        {
            ExecutionId = $"{engagementId}::{workflowId}",
            EngagementId = engagementId,
            WorkflowId = workflowId,
            ClosedAtUtc = closedAtUtc ?? new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);
        var previousRecordHash = chain.Count > 0
            ? chain[^1].RecordHash
            : AuditRecordHasher.ComputeGenesisHash(engagementId);

        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var key = await keyProvider.GetCurrentKeyAsync(CancellationToken.None);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);
        var signed = AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);

        await store.CreateAsync(signed, CancellationToken.None);
        return signed;
    }
}
