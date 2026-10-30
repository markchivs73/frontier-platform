using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model.Tests.Serialization;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class GateDecisionTests
{
    [Fact]
    public void From_CopiesEveryFieldOfTheRecordedDecision()
    {
        var recorded = new HitlDecision
        {
            GateId = "approve-quote",
            RequestId = "exec-1:approve-quote:1",
            ApproverId = "user:4f1c2a",
            Kind = DecisionKind.Reject,
            Notes = "Too low.",
            RollbackToNodeId = "price-quote",
            DecidedAtUtc = new DateTime(2026, 10, 30, 9, 15, 0, DateTimeKind.Utc),
        };

        var decision = GateDecision.From(recorded);

        Assert.Equal(
            new GateDecision
            {
                GateId = "approve-quote",
                Decision = DecisionKind.Reject,
                ApproverId = "user:4f1c2a",
                DecidedAtUtc = recorded.DecidedAtUtc,
                Notes = "Too low.",
                RollbackToNodeId = "price-quote",
            },
            decision);
    }

    [Fact]
    public void From_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => GateDecision.From(null!));
    }

    [Fact]
    public void Validate_AcceptsAWellFormedDecision()
    {
        ContractSamples.GateDecision().Validate();
    }

    [Fact]
    public void Validate_ReportsEveryViolation()
    {
        var decision = ContractSamples.GateDecision() with { GateId = " ", ApproverId = "", Decision = DecisionKind.Approve };

        var exception = Assert.Throws<ContractViolationException>(decision.Validate);

        Assert.Equal(nameof(GateDecision), exception.ContractType);
        Assert.Equal(
            ["gate_id is required", "approver_id is required", "rollback_to_node_id is only set on a rejection"],
            exception.Violations);
    }
}
