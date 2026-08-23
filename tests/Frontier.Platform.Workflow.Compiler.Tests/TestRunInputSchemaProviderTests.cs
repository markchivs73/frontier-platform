using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.43 (doc 19 §A4-R2/C-31): <see cref="TestRunInputSchemaProvider"/> backs the A4 "Expected
/// shape" panel — it must degrade to <see langword="null"/> (unaided free-text textarea)
/// whenever the entry node isn't a single resolvable <see cref="AgentTaskNode"/>.
/// </summary>
public sealed class TestRunInputSchemaProviderTests
{
    [Fact]
    public void GetInputSchema_SingleAgentTaskEntry_ReturnsSchemaAndExample()
    {
        var provider = new TestRunInputSchemaProvider(new ReflectionContractTypeCatalog(TestContractSet.Instance));
        var definition = S930Fixtures.Build([S930Fixtures.Agent("entry", inputContract: "BriefArtifact")]);

        var result = provider.GetInputSchema(definition);

        Assert.NotNull(result);
        Assert.Equal("BriefArtifact", result.ContractTypeName);
        Assert.NotNull(result.Schema);
        Assert.NotNull(result.Example);
        Assert.Equal("example", result.Example["narrative"]!.GetValue<string>());
    }

    [Fact]
    public void GetInputSchema_NoNodes_ReturnsNull()
    {
        var provider = new TestRunInputSchemaProvider(new ReflectionContractTypeCatalog(TestContractSet.Instance));
        var definition = S930Fixtures.Build([]);

        Assert.Null(provider.GetInputSchema(definition));
    }

    [Fact]
    public void GetInputSchema_TwoEntryNodes_ReturnsNull()
    {
        var provider = new TestRunInputSchemaProvider(new ReflectionContractTypeCatalog(TestContractSet.Instance));
        // Two nodes, no control edge between them - both are entry candidates.
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a"), S930Fixtures.Agent("b")]);

        Assert.Null(provider.GetInputSchema(definition));
    }

    [Fact]
    public void GetInputSchema_EntryNodeIsNotAgentTask_ReturnsNull()
    {
        var provider = new TestRunInputSchemaProvider(new ReflectionContractTypeCatalog(TestContractSet.Instance));
        var definition = S930Fixtures.Build([S930Fixtures.Gate("entry")]);

        Assert.Null(provider.GetInputSchema(definition));
    }

    [Fact]
    public void GetInputSchema_UnresolvableContractType_ReturnsNull()
    {
        var provider = new TestRunInputSchemaProvider(new ReflectionContractTypeCatalog(TestContractSet.Instance));
        var definition = S930Fixtures.Build([S930Fixtures.Agent("entry", inputContract: "NotAContractType")]);

        Assert.Null(provider.GetInputSchema(definition));
    }
}
