using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// S5.5 integration tests for <see cref="CosmosAuditRecordStore"/> against the local Cosmos
/// emulator (doc 02 §3, doc 05 §6, cosmos-conventions: "integration tests against the
/// emulator, not SDK mocks"). Creates and tears down its own database/container so it
/// doesn't depend on <c>tools/dev-setup/cosmos-init.py</c> having been run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosAuditRecordStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-audit-record-store-tests";

    private readonly DevKeyProvider keyProvider = new();

    private CosmosClient client = null!;
    private CosmosAuditRecordStore store = null!;

    /// <summary>Creates a canonical-profile-wired <see cref="CosmosClient"/> against the emulator and provisions the <c>audit-records</c> container.</summary>
    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, EmulatorKey, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                CheckCertificateRevocationList = true,
            }),
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var containerProperties = new ContainerProperties(CosmosAuditRecordStore.ContainerName, "/engagement_id");
        await database.Database.CreateContainerIfNotExistsAsync(containerProperties);
        store = new CosmosAuditRecordStore(client, Options.Create(new CosmosOptions { Database = DatabaseId }));
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/> (the store is the system under test, not the disposal target).</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task GetChainAsync_NoRecordsForEngagement_ReturnsEmpty()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";

        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);

        Assert.Empty(chain);
    }

    [Fact]
    public async Task CreateAsync_ThenGetChainAsync_ReturnsTheCreatedRecord()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await Sign(engagementId, "wf-1");

        await store.CreateAsync(record, CancellationToken.None);
        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);

        var single = Assert.Single(chain);
        CanonicalAssert.Equal(record, single);
    }

    [Fact]
    public async Task GetChainAsync_MultipleRecords_ReturnsOrderedByClosedAtUtcAscending()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var first = await Sign(engagementId, "wf-1", closedAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = await Sign(engagementId, "wf-2", closedAtUtc: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await store.CreateAsync(second, CancellationToken.None);
        await store.CreateAsync(first, CancellationToken.None);

        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);

        Assert.Equal(2, chain.Count);
        CanonicalAssert.Equal(first, chain[0]);
        CanonicalAssert.Equal(second, chain[1]);
    }

    [Fact]
    public async Task GetAsync_NoRecordForExecutionId_ReturnsNull()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var result = await store.GetAsync($"{engagementId}::wf-1", engagementId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_RecordExists_ReturnsIt()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await Sign(engagementId, "wf-1");
        await store.CreateAsync(record, CancellationToken.None);

        var result = await store.GetAsync(record.ExecutionId, record.EngagementId, CancellationToken.None);

        CanonicalAssert.Equal(record, result);
    }

    [Fact]
    public async Task CreateAsync_DuplicateExecutionId_ThrowsCosmosConflict()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await Sign(engagementId, "wf-1");
        await store.CreateAsync(record, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<CosmosException>(() => store.CreateAsync(record, CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task SignAsync_ParallelClosesOnOneEngagement_GiveALinearChainAgainstCosmos()
    {
        // S13.106 / ADR-PA31, against the real transactional batch and real If-Match semantics:
        // the emulator, not a fake, is what proves the 412 actually arrives.
        const int count = 8;
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var signer = BuildSigner();

        await Task.WhenAll(Enumerable.Range(1, count).Select(index => signer.SignAsync(
            AuditRecordHasherTests.Sample() with
            {
                ExecutionId = $"{engagementId}::wf-{index}",
                EngagementId = engagementId,
                WorkflowId = $"wf-{index}",
                ClosedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
            },
            CancellationToken.None)));

        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);
        var verification = await signer.VerifyAsync(chain[0].ExecutionId, engagementId, CancellationToken.None);

        Assert.Equal(count, chain.Count);
        Assert.Equal(count, chain.Select(record => record.PreviousRecordHash).Distinct(StringComparer.Ordinal).Count());
        Assert.True(verification.ChainValid);
        Assert.Null(verification.BrokenLinkAt);
        Assert.Null(verification.Forks);
        Assert.Null(verification.UnreachableRecords);
    }

    [Fact]
    public async Task GetChainAsync_ExcludesTheHeadDocument()
    {
        // The head shares the container and partition; it must never surface as a record.
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var signer = BuildSigner();
        await signer.SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = $"{engagementId}::wf-1", EngagementId = engagementId, WorkflowId = "wf-1" },
            CancellationToken.None);

        var chain = await store.GetChainAsync(engagementId, CancellationToken.None);

        Assert.Single(chain);
        Assert.NotNull(await ((IAuditChainHeadStore)store).ReadHeadAsync(engagementId, CancellationToken.None));
    }

    [Fact]
    public async Task SignAsync_ChainPredatingTheGuard_CreatesTheHeadFromTheStoredTail()
    {
        // The migration path against real storage: records written the old way (CreateAsync, no
        // head), then one append under the new code.
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var legacy = await Sign(engagementId, "wf-legacy");
        await store.CreateAsync(legacy, CancellationToken.None);

        var signed = await BuildSigner().SignAsync(
            AuditRecordHasherTests.Sample() with
            {
                ExecutionId = $"{engagementId}::wf-2",
                EngagementId = engagementId,
                WorkflowId = "wf-2",
                ClosedAtUtc = legacy.ClosedAtUtc.AddMinutes(1),
            },
            CancellationToken.None);

        var head = await ((IAuditChainHeadStore)store).ReadHeadAsync(engagementId, CancellationToken.None);
        Assert.Equal(legacy.RecordHash, signed.PreviousRecordHash);
        Assert.Equal(2, head!.Head.Sequence);
        Assert.Equal(1, head.Head.UnguardedRecordCount);
    }

    /// <summary>An <see cref="AuditSigner"/> over the emulator-backed store (ADR-PA31).</summary>
    private AuditSigner BuildSigner() =>
        new(store, store, keyProvider, new HmacAuditSigningService(keyProvider), Options.Create(new ExecutionAuditOptions { AppendMaxAttempts = 50, AppendBaseDelayMs = 5, AppendMaxDelayMs = 200 }));

    /// <summary>Builds a well-formed, correctly-signed <see cref="SignedAuditRecord"/> for <paramref name="engagementId"/>/<paramref name="workflowId"/>.</summary>
    private async Task<Frontier.Platform.Audit.SignedAuditRecord> Sign(string engagementId, string workflowId, DateTime? closedAtUtc = null)
    {
        var record = AuditRecordHasherTests.Sample() with
        {
            ExecutionId = $"{engagementId}::{workflowId}",
            EngagementId = engagementId,
            WorkflowId = workflowId,
            ClosedAtUtc = closedAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var previousRecordHash = AuditRecordHasher.ComputeGenesisHash(engagementId);
        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var key = await keyProvider.GetCurrentKeyAsync(CancellationToken.None);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);

        return AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);
    }
}
