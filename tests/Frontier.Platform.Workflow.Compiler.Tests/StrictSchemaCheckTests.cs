using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7h (ADR-DC7): pins the structured-output constraint that took down a live test run — the
/// designer picked <see cref="DictionaryShapedProjection"/> as a node's output contract, and
/// Anthropic rejected the generated schema because its <c>Dictionary</c> member is an open map.
/// </summary>
public sealed class StrictSchemaCheckTests
{
    [Theory]
    [InlineData(typeof(UpdateResult))]
    [InlineData(typeof(LookupResult))]        // flat record with a list of strings
    [InlineData(typeof(ScoredMatch))]
    [InlineData(typeof(AssignmentResult))]
    public void IsBindable_FlatStepContracts_AreBindable(Type contract)
    {
        Assert.Null(StrictSchemaCheck.FirstOpenMapPath(contract));
        Assert.True(StrictSchemaCheck.IsBindable(contract));
    }

    [Fact]
    public void IsBindable_ContractWithADictionaryMember_IsNotBindable()
    {
        // The exact failure: DictionaryShapedProjection.Artifacts is
        // Dictionary<string, EngagementArtifactProgress> — an open map.
        var path = StrictSchemaCheck.FirstOpenMapPath(typeof(DictionaryShapedProjection));

        Assert.NotNull(path);
        Assert.False(StrictSchemaCheck.IsBindable(typeof(DictionaryShapedProjection)));
    }

    [Fact]
    public void FirstOpenMapPath_NamesTheOffendingMember()
    {
        var path = StrictSchemaCheck.FirstOpenMapPath(typeof(DictionaryShapedProjection));

        // The path must locate the member, not just say "somewhere in here".
        // S13.12b: the member is DictionaryShapedProjection.Artifacts since the rename
        // (internal projection — no JsonPropertyName annotations, so no wire impact).
        Assert.Contains("rtifact", path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstOpenMapPath_NullType_Throws() =>
        Assert.Throws<ArgumentNullException>(() => StrictSchemaCheck.FirstOpenMapPath(null!));
}
