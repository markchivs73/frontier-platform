using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

public sealed class AuditServiceCollectionExtensionsTests
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void AddFrontierAudit_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddFrontierAudit(null!));
    }

    [Fact]
    public async Task AddFrontierAudit_RegistersDevKeyProvider()
    {
        var provider = BuildProvider();

        var keyProvider = provider.GetRequiredService<IKeyProvider>();
        var key = await keyProvider.GetCurrentKeyAsync(CancellationToken.None);

        Assert.Equal("dev-key/v1", key.KeyId);
        Assert.False(key.KeyMaterial.IsEmpty);
    }

    [Fact]
    public void AddFrontierAudit_RegistersExpectedServices()
    {
        var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<CosmosClient>());
        Assert.IsType<CosmosAuditTelemetryStaging>(provider.GetRequiredService<IAuditTelemetryStaging>());
        Assert.IsType<CosmosAuditRecordStore>(provider.GetRequiredService<IAuditRecordStore>());
        Assert.IsType<AuditSigner>(provider.GetRequiredService<IAuditSigner>());
        Assert.IsType<AuditQueryService>(provider.GetRequiredService<IAuditQueryService>());

        var startupChecks = provider.GetServices<IStartupCheck>().ToArray();
        Assert.Contains(startupChecks, check => check is SigningKeyCheck);
        Assert.Contains(startupChecks, check => check is CosmosTopologyCheck);
    }

    [Fact]
    public void AddFrontierAudit_MissingCosmosKey_OptionsResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
        });

        services.AddFrontierAudit(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CosmosOptions>>().Value);
    }

    [Fact]
    public void AddFrontierAudit_MissingStorageConnectionString_BlobClientResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierAudit(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierAudit(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
