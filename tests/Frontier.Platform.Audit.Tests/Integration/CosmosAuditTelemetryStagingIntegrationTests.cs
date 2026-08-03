using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Frontier.TestSupport;

namespace Frontier.Platform.Audit.Tests.Integration;

/// <summary>
/// S5.2 integration tests for <see cref="CosmosAuditTelemetryStaging"/> against the local
/// Cosmos emulator (doc 05 §9, cosmos-conventions: "integration tests against the emulator,
/// not SDK mocks"). Creates and tears down its own database/container so it doesn't depend
/// on <c>tools/dev-setup/cosmos-init.py</c> having been run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosAuditTelemetryStagingIntegrationTests : IAsyncLifetime, IDisposable
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "frontier-audit-telemetry-tests";

    private CosmosClient client = null!;
    private Container container = null!;
    private CosmosAuditTelemetryStaging staging = null!;

    /// <summary>Creates a canonical-profile-wired <see cref="CosmosClient"/> against the emulator and provisions the <c>audit-telemetry-staging</c> container with its doc 05 §9 TTL.</summary>
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
        var containerProperties = new ContainerProperties(CosmosAuditTelemetryStaging.ContainerName, "/execution_id")
        {
            DefaultTimeToLive = 2592000,
        };
        var containerResponse = await database.Database.CreateContainerIfNotExistsAsync(containerProperties);
        container = containerResponse.Container;
        staging = new CosmosAuditTelemetryStaging(client, Options.Create(new CosmosOptions { Database = DatabaseId }));
    }

    /// <summary>Drops the test database so each run starts clean.</summary>
    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    /// <summary>Releases the <see cref="CosmosClient"/> (the staging store is the system under test, not the disposal target).</summary>
    public void Dispose() => client.Dispose();

    [Fact]
    public async Task RecordInvocationAsync_ThenGetForExecution_RoundTripsTheRecord()
    {
        var executionId = $"eng-{Guid.NewGuid():N}::wf-chain";
        var record = TelemetrySamples.Record() with { ExecutionId = executionId };

        await staging.RecordInvocationAsync(record, CancellationToken.None);
        var records = await staging.GetForExecutionAsync(executionId, CancellationToken.None);

        var single = Assert.Single(records);
        CanonicalAssert.Equal(record, single);
    }

    [Fact]
    public async Task RecordInvocationAsync_Retried_IsConvergent()
    {
        var executionId = $"eng-{Guid.NewGuid():N}::wf-chain";
        var record = TelemetrySamples.Record() with { ExecutionId = executionId };

        await staging.RecordInvocationAsync(record, CancellationToken.None);
        await staging.RecordInvocationAsync(record, CancellationToken.None);
        var records = await staging.GetForExecutionAsync(executionId, CancellationToken.None);

        var single = Assert.Single(records);
        CanonicalAssert.Equal(record, single);
    }

    [Fact]
    public async Task RecordInvocationAsync_WrittenUnderContainerWithThirtyDayDefaultTtl()
    {
        var executionId = $"eng-{Guid.NewGuid():N}::wf-chain";
        var record = TelemetrySamples.Record() with { ExecutionId = executionId };

        await staging.RecordInvocationAsync(record, CancellationToken.None);

        var documentId = AuditTelemetryDocumentId.ForInvocation(record.ExecutionId, record.CorrelationId);
        var item = await container.ReadItemAsync<AuditTelemetryStagingDocument>(documentId, new PartitionKey(executionId));
        var containerProperties = await container.ReadContainerAsync();

        Assert.Equal(documentId, item.Resource.Id);
        Assert.Equal(2592000, containerProperties.Resource.DefaultTimeToLive);
    }

    [Fact]
    public async Task GetForExecutionAsync_NoStagedRecords_ReturnsEmpty()
    {
        var executionId = $"eng-{Guid.NewGuid():N}::wf-chain";

        var records = await staging.GetForExecutionAsync(executionId, CancellationToken.None);

        Assert.Empty(records);
    }
}
