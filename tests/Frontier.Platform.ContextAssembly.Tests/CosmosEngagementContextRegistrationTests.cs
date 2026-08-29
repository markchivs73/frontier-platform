using Frontier.Platform.ContextAssembly;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Tests for the opt-in Cosmos engagement-context registration. Until it existed the durable
/// store was internal and registered nowhere, so every engagement outside the compiled-in
/// catalogue resolved to no dynamic context (frontier-workflow S13.50).
/// </summary>
public sealed class CosmosEngagementContextRegistrationTests
{
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        }).Build();

    [Fact]
    public void AddFrontierCosmosEngagementContext_ReplacesThePhase1Store()
    {
        var services = new ServiceCollection();
        services.AddFrontierContextAssembly();

        services.AddFrontierCosmosEngagementContext(Configuration());

        using var provider = services.BuildServiceProvider();
        Assert.IsNotType<Phase1EngagementContextStore>(provider.GetRequiredService<IEngagementContextStore>());
    }

    /// <summary>
    /// Replace, not append: leaving both registered would make the resolved implementation depend
    /// on call order — true in one head and false in the other.
    /// </summary>
    [Fact]
    public void AddFrontierCosmosEngagementContext_LeavesExactlyOneStoreRegistered()
    {
        var services = new ServiceCollection();
        services.AddFrontierContextAssembly();
        services.AddFrontierCosmosEngagementContext(Configuration());

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEngagementContextStore));
    }

    /// <summary>A consumer that already shares a CosmosClient keeps it (TryAdd), rather than opening a second one.</summary>
    [Fact]
    public void AddFrontierCosmosEngagementContext_KeepsAConsumerSuppliedCosmosClient()
    {
        using var shared = new Microsoft.Azure.Cosmos.CosmosClient("https://localhost:8081", EmulatorKey);
        var services = new ServiceCollection();
        services.AddSingleton(shared);
        services.AddFrontierContextAssembly();

        services.AddFrontierCosmosEngagementContext(Configuration());

        using var provider = services.BuildServiceProvider();
        Assert.Same(shared, provider.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>());
    }

    [Fact]
    public void AddFrontierCosmosEngagementContext_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFrontierCosmosEngagementContext(Configuration()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierCosmosEngagementContext(null!));
    }

    [Theory]
    [InlineData("https://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("https://frontier.documents.azure.com:443", false)]
    public void IsLocalEmulator_DistinguishesTheEmulator(string endpoint, bool expected) =>
        Assert.Equal(expected, ContextAssemblyServiceCollectionExtensions.IsLocalEmulator(endpoint));
}
