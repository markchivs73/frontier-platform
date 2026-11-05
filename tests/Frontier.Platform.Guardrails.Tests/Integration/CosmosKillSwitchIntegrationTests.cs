using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Guardrails.Tests.Integration;

/// <summary>
/// S13.187's binding test at the platform layer, against the local Cosmos emulator: a switch thrown through one
/// process's <see cref="IKillSwitch"/> (the API head) stops the next admission in another process (a worker),
/// because both read one <c>kill-switch</c> document in <c>platform-config</c> (doc 07 §5a). Creates and drops its
/// own database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosKillSwitchIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-kill-switch-tests";

    private CosmosClient client = null!;
    private Container platformConfig = null!;
    private Container ledger = null!;

    /// <summary>Creates a canonical-profile-wired client and provisions <c>platform-config</c> (PK <c>/config_type</c>) and <c>guardrail-ledger</c>.</summary>
    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, Frontier.TestSupport.EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = (await client.CreateDatabaseIfNotExistsAsync(DatabaseId)).Database;
        platformConfig = (await database.CreateContainerIfNotExistsAsync(new ContainerProperties("platform-config", "/config_type"))).Container;
        ledger = (await database.CreateContainerIfNotExistsAsync(new ContainerProperties("guardrail-ledger", "/engagement_id"))).Container;
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/>.</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task ASwitchThrownOnTheApiHead_StopsTheNextAdmissionOnAWorker_AndAResumeLiftsIt()
    {
        var apiHead = Process();
        var worker = Process();
        var admission = worker.GetRequiredService<IAdmissionController>();

        Assert.Equal(AdmissionResult.Proceed, (await admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None)).Result);

        await apiHead.GetRequiredService<IKillSwitch>().ActivateAsync("drill", CancellationToken.None);
        await Assert.ThrowsAsync<GuardrailsSuspendedException>(() => admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None));

        await apiHead.GetRequiredService<IKillSwitch>().DeactivateAsync(CancellationToken.None);
        Assert.Equal(AdmissionResult.Proceed, (await admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task ComposedFromConfiguration_ASwitchThrownOnTheApiHead_StopsTheNextAdmissionOnAWorker()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cosmos:Database"] = DatabaseId })
            .Build();
        using var apiHead = ProcessFrom(configuration);
        using var worker = ProcessFrom(configuration);
        var admission = worker.GetRequiredService<IAdmissionController>();
        Assert.Equal(AdmissionResult.Proceed, (await admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None)).Result);

        await apiHead.GetRequiredService<IKillSwitch>().ActivateAsync("drill", CancellationToken.None);

        await Assert.ThrowsAsync<GuardrailsSuspendedException>(() => admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None));
        var stored = await platformConfig.ReadItemAsync<KillSwitchDocument>(KillSwitchDocument.FleetId, new PartitionKey(KillSwitchDocument.ConfigTypeValue));
        Assert.True(stored.Resource.Active);

        await apiHead.GetRequiredService<IKillSwitch>().DeactivateAsync(CancellationToken.None);
        Assert.Equal(AdmissionResult.Proceed, (await admission.AdmitAsync(AdmissionControllerTests.Estimate(1_000, 500), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task ComposedFromConfiguration_TheLedgerWritesToTheGuardrailLedgerContainer()
    {
        var engagementId = $"eng-{Guid.NewGuid():N}";
        using var worker = ProcessFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cosmos:Database"] = DatabaseId })
            .Build());

        await worker.GetRequiredService<IBudgetLedger>().RecordUsageAsync(BudgetLedgerTests.Usage("corr-1", "exec-1", engagementId, 100, 50, 0.001m), CancellationToken.None);

        var stored = await ledger.ReadItemAsync<BudgetLedgerDocument>(BudgetLedgerAccumulation.DocumentId(engagementId), new PartitionKey(engagementId));
        Assert.Equal(engagementId, stored.Resource.EngagementId);
    }

    [Fact]
    public async Task ActivateAsync_StoresTheDocumentUnderTheKillSwitchPartition()
    {
        await Process().GetRequiredService<IKillSwitch>().ActivateAsync("drill", CancellationToken.None);

        var stored = await platformConfig.ReadItemAsync<KillSwitchDocument>(KillSwitchDocument.FleetId, new PartitionKey(KillSwitchDocument.ConfigTypeValue));

        Assert.True(stored.Resource.Active);
        Assert.Equal("drill", stored.Resource.Reason);
    }

    [Fact]
    public async Task IsActiveAsync_WhenTheContainerIsMissing_Throws_RatherThanReportingNotThrown()
    {
        var missing = client.GetDatabase(DatabaseId).GetContainer("no-such-container");
        var killSwitch = new CosmosKillSwitch(missing, TimeProvider.System);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() => killSwitch.IsActiveAsync(CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, thrown.StatusCode);
        Assert.NotEqual(0, thrown.SubStatusCode);
    }

    /// <summary>One process's composition over the shared containers; a zero cache so the test needs no wait.</summary>
    private ServiceProvider Process() =>
        new ServiceCollection().AddFrontierGuardrails(ledger, platformConfig, TimeSpan.Zero).BuildServiceProvider();

    /// <summary>One process composed as a consumer head composes it: its registered client, and the database from configuration.</summary>
    private ServiceProvider ProcessFrom(IConfiguration configuration) =>
        new ServiceCollection().AddSingleton(client).AddFrontierGuardrails(configuration, TimeSpan.Zero).BuildServiceProvider();
}
