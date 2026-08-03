using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl.Tests.Integration;

/// <summary>
/// S6.11a integration tests for <see cref="CosmosApprovalStore.GetDecidedAsync"/> against
/// the local Cosmos emulator (doc 12 §8, cosmos-conventions: "integration tests against
/// the emulator, not SDK mocks"). Creates and tears down its own database/container so it
/// doesn't depend on <c>tools/dev-setup/cosmos-init.py</c> having been run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosApprovalStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-hitl-approval-tests";

    private CosmosClient client = null!;
    private CosmosApprovalStore store = null!;

    /// <summary>Creates a canonical-profile-wired <see cref="CosmosClient"/> against the emulator and provisions the <c>approvals</c> container.</summary>
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
        await database.Database.CreateContainerIfNotExistsAsync(CosmosApprovalStore.ContainerName, "/engagement_id");

        var cosmosOptions = Options.Create(new CosmosOptions { Database = DatabaseId });
        var decidedQuery = new DecidedApprovalQuery(client, cosmosOptions);
        store = new CosmosApprovalStore(client, cosmosOptions, decidedQuery);
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/> (doc 06 §9 store is the system under test, not the disposal target).</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task GetDecidedAsync_ReturnsOnlyDecidedRequests()
    {
        var pending = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-pending" };
        var decided = HitlFixtures.PendingRequest() with
        {
            Id = $"req-{Guid.NewGuid():N}",
            EngagementId = "eng-decided",
            Status = ApprovalRequestStatus.Decided,
            Decision = new HitlDecision
            {
                GateId = "gate-business-1",
                RequestId = "req-decided",
                ApproverId = "approver-1",
                Kind = DecisionKind.Approve,
                DecidedAtUtc = new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc),
            },
        };
        var escalated = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-escalated", Status = ApprovalRequestStatus.Escalated };
        var expired = HitlFixtures.PendingRequest() with { Id = $"req-{Guid.NewGuid():N}", EngagementId = "eng-expired", Status = ApprovalRequestStatus.Expired };

        await store.UpsertAsync(pending, CancellationToken.None);
        await store.UpsertAsync(decided, CancellationToken.None);
        await store.UpsertAsync(escalated, CancellationToken.None);
        await store.UpsertAsync(expired, CancellationToken.None);

        var results = await store.GetDecidedAsync(CancellationToken.None);

        Assert.Contains(results, r => r.Id == decided.Id);
        Assert.DoesNotContain(results, r => r.Id == pending.Id);
        Assert.DoesNotContain(results, r => r.Id == escalated.Id);
        Assert.DoesNotContain(results, r => r.Id == expired.Id);
    }
}
