namespace Frontier.Platform.Hitl.Tests;

/// <summary>S4.6a tests for <see cref="RollbackPlanner"/> (doc 06 §6).</summary>
public sealed class RollbackPlannerTests
{
    private readonly RollbackPlanner planner = new();

    [Fact]
    public void Plan_NullRollbackTargetSection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, [], new Dictionary<string, string>()));
    }

    [Fact]
    public void Plan_NullCascadeDownstreamSections_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => planner.Plan("scope", null!, new Dictionary<string, string>()));
    }

    [Fact]
    public void Plan_NullApprovedSnapshotRefs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => planner.Plan("scope", [], null!));
    }

    [Fact]
    public void Plan_RollbackToScope_InvalidSetIsTargetPlusDownstream()
    {
        var plan = planner.Plan("scope", ["approach", "pricing"], new Dictionary<string, string>());

        Assert.Equal(["scope", "approach", "pricing"], plan.InvalidSet);
    }

    [Fact]
    public void Plan_RollbackToScope_RestoreSetExcludesInvalidSections()
    {
        var approvedSnapshotRefs = new Dictionary<string, string>
        {
            ["scope"] = "eng-1::wf-chain:scope:v1",
            ["approach"] = "eng-1::wf-chain:approach:v1",
            ["pricing"] = "eng-1::wf-chain:pricing:v1",
            ["intake"] = "eng-1::wf-chain:intake:v1",
        };

        var plan = planner.Plan("scope", ["approach", "pricing"], approvedSnapshotRefs);

        Assert.Equal(new Dictionary<string, string> { ["intake"] = "eng-1::wf-chain:intake:v1" }, plan.RestoreSet);
    }

    [Fact]
    public void Plan_NoCascadeDownstream_InvalidSetIsTargetOnly()
    {
        var plan = planner.Plan("pricing", [], new Dictionary<string, string> { ["scope"] = "eng-1::wf-chain:scope:v1" });

        Assert.Equal(["pricing"], plan.InvalidSet);
        Assert.Equal(new Dictionary<string, string> { ["scope"] = "eng-1::wf-chain:scope:v1" }, plan.RestoreSet);
    }
}
