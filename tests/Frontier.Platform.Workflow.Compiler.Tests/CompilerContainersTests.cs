using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.148: the compiler's stores use the configured Cosmos database. They used to hard-code
/// <c>frontier-workflow</c>, so a consumer or a test that set <c>Cosmos:Database</c> still had its
/// definitions read from, and written to, the default database.
/// </summary>
public sealed class CompilerContainersTests
{
    // GetDatabase and GetContainer are lazy proxies: building them makes no network call.
    private static readonly CosmosClient Client = new("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;");

    [Fact]
    public void Definitions_IsTheWorkflowDefinitionsContainer_InTheConfiguredDatabase()
    {
        var container = CompilerContainers.Definitions(Client, Options.Create(new CosmosOptions { Database = "tenant-a" }));

        Assert.Equal("tenant-a", container.Database.Id);
        Assert.Equal("workflow-definitions", container.Id);
    }

    [Fact]
    public void Containers_RefuseANullClientOrOptions()
    {
        Assert.Throws<ArgumentNullException>(() => CompilerContainers.Definitions(null!, Options.Create(new CosmosOptions())));
        Assert.Throws<ArgumentNullException>(() => CompilerContainers.Definitions(Client, null!));
    }

    [Fact]
    public void CosmosOptions_DefaultsToTheEmulatorDatabase() =>
        Assert.Equal("frontier-workflow", new CosmosOptions().Database);

    [Fact]
    public void AddFrontierWorkflowCompiler_BindsCosmosDatabase_FromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cosmos:Database"] = "tenant-b" })
            .Build();

        using var provider = new ServiceCollection().AddFrontierWorkflowCompiler(configuration).BuildServiceProvider();

        Assert.Equal("tenant-b", provider.GetRequiredService<IOptions<CosmosOptions>>().Value.Database);
    }

    [Fact]
    public void AddFrontierWorkflowCompiler_WithAnEmptyDatabaseName_FailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cosmos:Database"] = "" })
            .Build();

        using var provider = new ServiceCollection().AddFrontierWorkflowCompiler(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CosmosOptions>>().Value);
    }

    /// <summary>S13.223: ADR-DC4's candidate query is deferred, so the compiler registers no retirement monitor.</summary>
    [Fact]
    [Obsolete("Asserts the absence of the deferred IRetirementMonitor.")]
    public void AddFrontierWorkflowCompiler_RegistersNoRetirementMonitor()
    {
        var services = new ServiceCollection().AddFrontierWorkflowCompiler(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRetirementMonitor));
    }

    [Fact]
    public void AddFrontierWorkflowCompiler_RefusesANullConfiguration() =>
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierWorkflowCompiler(null!));
}
