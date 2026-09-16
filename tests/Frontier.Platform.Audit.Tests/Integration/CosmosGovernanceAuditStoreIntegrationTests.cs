using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// S13.103 emulator tests for <see cref="CosmosGovernanceAuditStore"/> behind <see cref="GovernanceAuditService"/>
/// (ADR-PA30): the transactional batch and If-Match head under real concurrency. Provisions its own
/// database, so it does not depend on <c>cosmos-init.py</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosGovernanceAuditStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-governance-audit-store-tests";

    private CosmosClient client = null!;
    private GovernanceAuditService service = null!;

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
        await database.Database.CreateContainerIfNotExistsAsync(new ContainerProperties(CosmosGovernanceAuditStore.ContainerName, CosmosGovernanceAuditStore.PartitionKeyPath) { DefaultTimeToLive = -1 });

        var store = new CosmosGovernanceAuditStore(client, Options.Create(new CosmosOptions { Database = DatabaseId }));
        var devKeys = new DevKeyProvider();
        service = new GovernanceAuditService(store, new SharedSigningKeyRing(devKeys, new HmacAuditSigningService(devKeys)), Options.Create(new GovernanceAuditOptions { AppendMaxAttempts = 50, AppendBaseDelayMs = 5, AppendMaxDelayMs = 200 }));
    }

    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task AppendAsync_ParallelAppendsOnOneScope_GiveAGaplessValidChain()
    {
        const int count = 20;
        var scope = UniqueScope();

        await Task.WhenAll(Enumerable.Range(1, count).Select(index => service.AppendAsync(Entry($"role-{index}") with { Scope = scope }, CancellationToken.None)));

        var page = await service.QueryAsync(new GovernanceAuditQuery { Scope = scope, PageSize = GovernanceAuditQuery.MaxPageSize }, CancellationToken.None);
        var verification = await service.VerifyAsync(scope, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, count).Select(index => (long)index), page.Records.Select(record => record.Sequence));
        Assert.Equal(count, page.Records.Select(record => record.PreviousRecordHash).Distinct(StringComparer.Ordinal).Count());
        Assert.True(verification.Valid);
        Assert.Equal(count, verification.HeadSequence);
    }

    [Fact]
    public async Task AppendAsync_IdenticalEntryAppendedConcurrently_StoresOneRecord()
    {
        var scope = UniqueScope();
        var entry = Entry() with { Scope = scope };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.AppendAsync(entry, CancellationToken.None)));

        Assert.Single(results.Select(record => record.RecordId).Distinct(StringComparer.Ordinal));
        Assert.Single((await service.QueryAsync(new GovernanceAuditQuery { Scope = scope }, CancellationToken.None)).Records);
        Assert.NotNull(await service.GetAsync(results[0].RecordId, CancellationToken.None));
    }

    private static string UniqueScope() => $"it{Guid.NewGuid():N}";
}
