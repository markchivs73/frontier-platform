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
        Assert.IsType<SharedSigningKeyRing>(provider.GetRequiredService<ISigningKeyRing>());
        Assert.IsType<CosmosGovernanceAuditStore>(provider.GetRequiredService<IGovernanceAuditStore>());
        Assert.IsType<GovernanceAuditService>(provider.GetRequiredService<IGovernanceAuditService>());
        Assert.Equal(8, provider.GetRequiredService<IOptions<GovernanceAuditOptions>>().Value.AppendMaxAttempts);
        Assert.Equal(8, provider.GetRequiredService<IOptions<ExecutionAuditOptions>>().Value.AppendMaxAttempts);

        // ADR-PA31: the record store and the chain-head port must be the SAME instance — two
        // instances would still work against Cosmos, but the registration would be lying about
        // which object owns the container.
        Assert.Same(provider.GetRequiredService<IAuditRecordStore>(), provider.GetRequiredService<IAuditChainHeadStore>());

        var startupChecks = provider.GetServices<IStartupCheck>().ToArray();
        Assert.Contains(startupChecks, check => check is SigningKeyCheck);
        Assert.Contains(startupChecks, check => check is CosmosTopologyCheck);
    }

    [Theory]
    [InlineData("https://frontier.documents.azure.com:443", false)]
    [InlineData("https://localhost:8081", true)]
    [InlineData("https://127.0.0.1:8081", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("https://localhost.evil.example", false)] // S13.349: lookalikes keep certificate validation on
    [InlineData("https://localhost:8081@evil.example", false)]
    [InlineData("https://127.0.0.1.nip.io:8081", false)]
    public void AddFrontierAudit_EndpointLocality_DeterminesCertBypassBranch(string endpoint, bool isLocal)
    {
        // S13.79 / S13.349: the client relaxes certificate validation (an HttpClientFactory) only for a parsed
        // loopback host; every other endpoint, lookalikes included, keeps the SDK's validating client.
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = endpoint,
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierAudit(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(isLocal, provider.GetRequiredService<CosmosClient>().ClientOptions.HttpClientFactory is not null);
    }

    /// <summary>S13.293: the deployment tenant binds from <c>Audit:Tenant</c>, and the signer receives it.</summary>
    [Fact]
    public void AddFrontierAudit_BindsTheAuditTenant()
    {
        var services = new ServiceCollection();
        services.AddFrontierAudit(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
            ["Audit:Tenant"] = "contoso",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.Equal("contoso", provider.GetRequiredService<IOptions<AuditTenantOptions>>().Value.Tenant);
        Assert.Equal("contoso", Assert.IsType<AuditSigner>(provider.GetRequiredService<IAuditSigner>()).DeploymentTenant);
        Assert.Equal("contoso", Assert.IsType<GovernanceAuditService>(provider.GetRequiredService<IGovernanceAuditService>()).DeploymentTenant);
    }

    /// <summary>S13.293: a tenant longer than 128 characters is refused when the options are validated, so the process does not start.</summary>
    [Fact]
    public void AddFrontierAudit_OverLongTenant_FailsOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddFrontierAudit(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
            ["Audit:Tenant"] = new string('t', 129),
        }));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AuditTenantOptions>>().Value);
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

    [Fact]
    public void AddFrontierAudit_RegistersTheGovernanceArchivalHostedService()
    {
        var services = new ServiceCollection();

        services.AddFrontierAudit(BuildConfiguration([]));

        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(ArchivalGovernanceAuditExportHostedService));
    }

    [Fact]
    public void AddFrontierAudit_ExecutionRetryOutOfRange_OptionsResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
            ["ExecutionAudit:AppendMaxAttempts"] = "0",
        });

        services.AddFrontierAudit(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ExecutionAuditOptions>>().Value);
    }

    [Fact]
    public void AddFrontierAudit_GovernanceRetryOutOfRange_OptionsResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
            ["GovernanceAudit:AppendMaxAttempts"] = "0",
        });

        services.AddFrontierAudit(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GovernanceAuditOptions>>().Value);
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
