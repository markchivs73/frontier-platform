using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>
/// S13.187: the shared guardrails compose like the other platform Cosmos stores. The configuration overload binds
/// <c>Cosmos:Database</c> and resolves its containers from the registered <see cref="CosmosClient"/> when a guardrail
/// is first resolved, so a head composes without Cosmos containers in hand.
/// </summary>
public sealed class GuardrailsContainersTests
{
    // GetDatabase and GetContainer are lazy proxies: building them makes no network call.
    private static readonly CosmosClient Client = new("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;");

    [Fact]
    public void Ledger_IsTheGuardrailLedgerContainer_InTheConfiguredDatabase()
    {
        var container = GuardrailsContainers.Ledger(Client, Options.Create(new CosmosOptions { Database = "tenant-a" }));

        Assert.Equal("tenant-a", container.Database.Id);
        Assert.Equal("guardrail-ledger", container.Id);
    }

    [Fact]
    public void PlatformConfig_IsThePlatformConfigContainer_InTheConfiguredDatabase()
    {
        var container = GuardrailsContainers.PlatformConfig(Client, Options.Create(new CosmosOptions { Database = "tenant-a" }));

        Assert.Equal("tenant-a", container.Database.Id);
        Assert.Equal("platform-config", container.Id);
    }

    [Fact]
    public void Containers_RefuseANullClientOrOptions()
    {
        Assert.Throws<ArgumentNullException>(() => GuardrailsContainers.Ledger(null!, Options.Create(new CosmosOptions())));
        Assert.Throws<ArgumentNullException>(() => GuardrailsContainers.PlatformConfig(Client, null!));
    }

    [Fact]
    public void CosmosOptions_DefaultsToTheEmulatorDatabase() =>
        Assert.Equal("frontier-workflow", new CosmosOptions().Database);

    [Fact]
    public void AddFrontierGuardrails_FromConfiguration_SharesTheKillSwitchThroughTheRegisteredClient()
    {
        var cache = TimeSpan.FromSeconds(7);
        using var provider = new ServiceCollection()
            .AddSingleton(Client)
            .AddFrontierGuardrails(Configuration("tenant-b"), cache)
            .BuildServiceProvider();

        Assert.Equal("tenant-b", provider.GetRequiredService<IOptions<CosmosOptions>>().Value.Database);
        Assert.IsType<CosmosKillSwitch>(provider.GetRequiredService<IKillSwitch>());
        Assert.IsType<CosmosBudgetLedger>(provider.GetRequiredService<IBudgetLedger>());
        Assert.IsType<AdmissionController>(provider.GetRequiredService<IAdmissionController>());
        Assert.IsType<BudgetHierarchy>(provider.GetRequiredService<IBudgetHierarchy>());
        Assert.Equal(cache, provider.GetRequiredService<KillSwitchGate>().CacheDuration);
    }

    [Fact]
    public void AddFrontierGuardrails_FromConfiguration_ComposesWithNoClientUntilAGuardrailIsResolved()
    {
        using var provider = new ServiceCollection()
            .AddFrontierGuardrails(Configuration("tenant-b"), GuardrailsServiceCollectionExtensions.DefaultKillSwitchCacheDuration)
            .BuildServiceProvider();

        Assert.Equal("tenant-b", provider.GetRequiredService<IOptions<CosmosOptions>>().Value.Database);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IKillSwitch>());
    }

    [Fact]
    public void AddFrontierGuardrails_FromConfiguration_RejectsANullConfigurationAndANegativeCache()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierGuardrails((IConfiguration)null!, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddFrontierGuardrails(Configuration("tenant-b"), TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void AddFrontierGuardrails_WithAnEmptyDatabaseName_FailsValidation()
    {
        using var provider = new ServiceCollection().AddFrontierGuardrails(Configuration(""), TimeSpan.Zero).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CosmosOptions>>().Value);
    }

    private static IConfiguration Configuration(string database) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cosmos:Database"] = database })
            .Build();
}
