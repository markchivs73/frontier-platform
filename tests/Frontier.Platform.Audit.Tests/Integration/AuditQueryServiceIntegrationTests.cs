using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// S5.7 integration tests for <see cref="AuditQueryService.QueryAsync"/> against the local
/// Cosmos emulator (doc 05 §7, §10, cosmos-conventions: "integration tests against the
/// emulator, not SDK mocks"). Creates and tears down its own database/container so it
/// doesn't depend on <c>tools/dev-setup/cosmos-init.py</c> having been run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuditQueryServiceIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-audit-query-tests";

    private readonly DevKeyProvider keyProvider = new();

    private CosmosClient client = null!;
    private CosmosAuditRecordStore recordStore = null!;
    private AuditQueryService service = null!;

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

        var options = Options.Create(new CosmosOptions { Database = DatabaseId });
        recordStore = new CosmosAuditRecordStore(client, options);
        service = new AuditQueryService(recordStore, client, options);
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/> (the service is the system under test, not the disposal target).</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task QueryAsync_EmptyQuery_ReturnsEveryRecordAsSummaries()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await SignAndStore(engagementId, "wf-1");

        var summaries = await service.QueryAsync(new AuditQuery(), CancellationToken.None);

        Assert.Contains(summaries, summary => summary.ExecutionId == record.ExecutionId);
    }

    [Fact]
    public async Task QueryAsync_FilterByEngagementId_ReturnsOnlyThatEngagementsRecords()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var otherEngagementId = $"eng-{Guid.NewGuid():N}";
        var record = await SignAndStore(engagementId, "wf-1");
        await SignAndStore(otherEngagementId, "wf-1");

        var summaries = await service.QueryAsync(new AuditQuery { EngagementId = engagementId }, CancellationToken.None);

        Assert.Equal([record.ExecutionId], summaries.Select(summary => summary.ExecutionId));
    }

    [Fact]
    public async Task QueryAsync_FilterByModelId_ReturnsOnlyRecordsWithThatResolvedModel()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await SignAndStore(engagementId, "wf-1");
        var resolvedModelId = record.AgentInvocations[0].ResolvedModel.ModelId;

        var matching = await service.QueryAsync(new AuditQuery { ModelId = resolvedModelId }, CancellationToken.None);
        var nonMatching = await service.QueryAsync(new AuditQuery { ModelId = "no-such-model" }, CancellationToken.None);

        Assert.Contains(matching, summary => summary.ExecutionId == record.ExecutionId);
        Assert.DoesNotContain(nonMatching, summary => summary.ExecutionId == record.ExecutionId);
    }

    [Fact]
    public async Task QueryAsync_FilterByDefinitionHash_ReturnsOnlyMatchingRecords()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var record = await SignAndStore(engagementId, "wf-1");

        var matching = await service.QueryAsync(new AuditQuery { DefinitionHash = record.DefinitionHash }, CancellationToken.None);
        var nonMatching = await service.QueryAsync(new AuditQuery { DefinitionHash = "0000000000000000000000000000000000000000000000000000000000000" }, CancellationToken.None);

        Assert.Contains(matching, summary => summary.ExecutionId == record.ExecutionId);
        Assert.DoesNotContain(nonMatching, summary => summary.ExecutionId == record.ExecutionId);
    }

    [Fact]
    public async Task QueryAsync_ValidatorIdFilter_ReturnsEmptyUntilStage6()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        await SignAndStore(engagementId, "wf-1");

        var summaries = await service.QueryAsync(new AuditQuery { ValidatorId = "any-validator" }, CancellationToken.None);

        Assert.Empty(summaries);
    }

    /// <summary>Builds, signs, and persists a well-formed record for <paramref name="engagementId"/>/<paramref name="workflowId"/>.</summary>
    private async Task<SignedAuditRecord> SignAndStore(string engagementId, string workflowId)
    {
        var record = AuditRecordHasherTests.Sample() with
        {
            ExecutionId = $"{engagementId}::{workflowId}",
            EngagementId = engagementId,
            WorkflowId = workflowId,
        };

        var previousRecordHash = AuditRecordHasher.ComputeGenesisHash(engagementId);
        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var key = await keyProvider.GetCurrentKeyAsync(CancellationToken.None);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);
        var signed = AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);

        await recordStore.CreateAsync(signed, CancellationToken.None);
        return signed;
    }
}
