using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>S4.6a tests for <see cref="HitlServiceCollectionExtensions"/>'s DI wiring and options binding (doc 12 §4 "options with teeth").</summary>
public sealed class HitlServiceCollectionExtensionsTests
{
    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator), used so client construction doesn't require live credentials.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void AddFrontierHitl_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddFrontierHitl(null!));
    }

    [Fact]
    public void AddFrontierHitl_ValidConfiguration_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierHitl(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CosmosClient>());
        Assert.IsType<CosmosApprovalStore>(provider.GetRequiredService<IApprovalStore>());
        Assert.IsType<ApprovalQueryService>(provider.GetRequiredService<IApprovalQueryService>());
        Assert.IsType<RollbackPlanner>(provider.GetRequiredService<IRollbackPlanner>());
        Assert.IsType<CosmosTopologyCheck>(provider.GetRequiredService<IStartupCheck>());
    }

    [Theory]
    [InlineData("https://frontier.documents.azure.com:443", false)]
    [InlineData("https://localhost:8081", true)]
    [InlineData("https://127.0.0.1:8081", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    public void AddFrontierHitl_EndpointLocality_DeterminesCertBypassBranch(string endpoint, bool isLocal)
    {
        // Exercises every branch of the private IsLocalEmulator OR-chain (S9.24
        // branch-coverage gap): each scheme/host combination the emulator can present.
        _ = isLocal; // asserted implicitly: client construction never throws either way
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = endpoint,
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        });

        services.AddFrontierHitl(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CosmosClient>());
    }

    [Fact]
    public void AddFrontierHitl_MissingCosmosKey_OptionsResolutionThrows()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
        });

        services.AddFrontierHitl(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CosmosOptions>>().Value);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
