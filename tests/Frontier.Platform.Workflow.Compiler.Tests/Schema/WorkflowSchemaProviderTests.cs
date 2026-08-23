using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Schema;

namespace Frontier.Platform.Workflow.Compiler.Tests.Schema;

/// <summary>Unit tests for <see cref="WorkflowSchemaProvider"/> — lazy generation and caching (S9.7).</summary>
public sealed class WorkflowSchemaProviderTests
{
    [Fact]
    public void GetSchema_ReturnsCachedInstanceAcrossCalls()
    {
        var generator = new WorkflowSchemaGenerator(XmlDocReader.ForAssembly(typeof(WorkflowNode).Assembly), new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance);
        var provider = new WorkflowSchemaProvider(generator);

        var first = provider.GetSchema();
        var second = provider.GetSchema();

        Assert.Same(first, second);
        Assert.Equal("1.0", first.SchemaVersion);
    }

    [Fact]
    public void DefaultConstructor_GeneratesSchemaFromAbstractions() =>
        Assert.Equal(8, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance).GetSchema().NodeTypes.Count);

    [Fact]
    public void Constructor_NullGenerator_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new WorkflowSchemaProvider((WorkflowSchemaGenerator)null!));
}
