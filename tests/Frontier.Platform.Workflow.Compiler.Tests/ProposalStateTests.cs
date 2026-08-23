// S13.12f: ProposalState moved to Frontier.Platform.Workflow.Compiler with the publish
// lifecycle it drives. These transition assertions came with it in spirit but not yet in
// location — the type is line-covered there by the lifecycle tests, which is not the same as
// having its state machine asserted. Relocated to the platform at S13.12g rather than dropped
// here on the strength of a coverage percentage.
using Frontier.Platform.Workflow.Compiler;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Compiler.Schema;
namespace Frontier.Platform.Workflow.Compiler.Tests;

public sealed class ProposalStateTests
{
    [Fact]
    public void List_Always_ReturnsAllFourValuesInDeclarationOrder()
    {
        Assert.Equal(
            [ProposalState.InReview, ProposalState.Approved, ProposalState.Rejected, ProposalState.Withdrawn],
            ProposalState.List);
    }

    [Theory]
    [InlineData("in_review")]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ProposalState.FromName(name).Name);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public void CanTransitionTo_FromInReview_AllowsEveryTerminalState(string next)
    {
        Assert.True(ProposalState.InReview.CanTransitionTo(ProposalState.FromName(next)));
    }

    [Fact]
    public void CanTransitionTo_InReviewToItself_IsIllegal()
    {
        Assert.False(ProposalState.InReview.CanTransitionTo(ProposalState.InReview));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public void CanTransitionTo_FromTerminalState_IsAlwaysIllegal(string from)
    {
        var terminal = ProposalState.FromName(from);

        Assert.All(ProposalState.List, next => Assert.False(terminal.CanTransitionTo(next)));
    }
}
