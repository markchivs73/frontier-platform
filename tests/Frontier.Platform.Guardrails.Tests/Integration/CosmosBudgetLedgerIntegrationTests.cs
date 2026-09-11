using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails.Tests.Integration;

/// <summary>
/// <see cref="CosmosBudgetLedger"/> against the local Cosmos emulator (doc 07 §6, cosmos-conventions: "integration
/// tests against the emulator, not SDK mocks"). Creates and tears down its own database/container.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosBudgetLedgerIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-guardrail-ledger-tests";

    private CosmosClient client = null!;
    private CosmosBudgetLedger ledger = null!;

    /// <summary>Creates a canonical-profile-wired <see cref="CosmosClient"/> and provisions the <c>guardrail-ledger</c> container.</summary>
    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, Frontier.TestSupport.EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var container = await database.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("guardrail-ledger", "/partitionKey"));
        ledger = new CosmosBudgetLedger(container.Container);
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/>.</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task RecordUsageAsync_TwoExecutionsAcrossThreeCalls_KeepsEveryScopeCurrent()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        var exec1 = $"exec-{Guid.NewGuid():N}";
        var exec2 = $"exec-{Guid.NewGuid():N}";

        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-1", exec1, engagementId, 100, 50, 0.0012m), CancellationToken.None);
        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-2", exec1, engagementId, 200, 25, 0.0030m), CancellationToken.None);
        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-3", exec2, engagementId, 10, 5, 0.0003m), CancellationToken.None);

        AssertSnapshot(await Snapshot(BudgetScopeKind.Execution, exec1), 375, 0.0042m, 2);
        AssertSnapshot(await Snapshot(BudgetScopeKind.Execution, exec2), 15, 0.0003m, 1);
        AssertSnapshot(await Snapshot(BudgetScopeKind.Engagement, engagementId), 390, 0.0045m, 3);
    }

    private Task<BudgetSnapshot> Snapshot(BudgetScopeKind kind, string id) =>
        ledger.GetSnapshotAsync(new BudgetScopeRef(kind, id), CancellationToken.None);

    private static void AssertSnapshot(BudgetSnapshot snapshot, long tokens, decimal cost, int invocations) =>
        Assert.Equal((tokens, cost, "USD", invocations), (snapshot.TokensUsed, snapshot.Cost, snapshot.Currency, snapshot.InvocationCount));
}
