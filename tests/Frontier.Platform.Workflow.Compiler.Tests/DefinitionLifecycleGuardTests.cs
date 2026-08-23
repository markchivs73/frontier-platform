using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S9.24: argument-guard branches on <see cref="DefinitionLifecycleService"/>'s lifecycle methods — none had a missing-argument test.</summary>
public sealed class DefinitionLifecycleGuardTests
{
    private readonly DefinitionLifecycleService _service = new(new Mock<IDefinitionStore>().Object, new Mock<IDefinitionCompiler>().Object);

    // CreateDraftAsync had no guard-clause test at all (S9.24 branch-coverage gap: lines 37-38).
    [Theory]
    [InlineData(null, "user-1")]
    [InlineData("wf-1", null)]
    public async Task CreateDraftAsync_MissingStringArgument_Throws(string? workflowId, string? userId)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateDraftAsync(workflowId!, fromVersion: null, userId!, CancellationToken.None));
    }

    // ApplyAgentMergeAsync had no guard-clause test at all (S9.24 branch-coverage gap: lines 155,157,158).
    [Theory]
    [InlineData(null, "rev-1", "user-1")]
    [InlineData("wf-1", null, "user-1")]
    [InlineData("wf-1", "rev-1", null)]
    public async Task ApplyAgentMergeAsync_MissingStringArgument_Throws(string? workflowId, string? expectedRevision, string? userId)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ApplyAgentMergeAsync(workflowId!, new List<string>(), expectedRevision!, userId!, CancellationToken.None));
    }

    // ApplyAgentMergeAsync's ArgumentNullException.ThrowIfNull(approvedChangeIds) guard.
    [Fact]
    public async Task ApplyAgentMergeAsync_NullApprovedChangeIds_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ApplyAgentMergeAsync("wf-1", null!, "rev-1", "user-1", CancellationToken.None));
    }

    // GetHistoryAsync had no guard-clause test at all (S9.24 branch-coverage gap: line 419).
    [Fact]
    public async Task GetHistoryAsync_MissingWorkflowId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetHistoryAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "rev-1", "user-1")]
    [InlineData("wf-1", null, "user-1")]
    [InlineData("wf-1", "rev-1", null)]
    public async Task SaveDraftAsync_MissingStringArgument_Throws(string? workflowId, string? expectedRevision, string? userId)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SaveDraftAsync(workflowId!, WorkflowDefinitionFixture.MinimalDefinition(), expectedRevision!, userId!, CancellationToken.None));
    }

    [Fact]
    public async Task SaveDraftAsync_NullDefinition_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.SaveDraftAsync("wf-1", null!, "rev-1", "user-1", CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "rev-1", "user-1")]
    [InlineData("wf-1", null, "user-1")]
    [InlineData("wf-1", "rev-1", null)]
    public async Task ProposePublishAsync_MissingStringArgument_Throws(string? workflowId, string? draftRevision, string? userId)
    {
        var report = new ValidationReport("wf-1", "rev-1", DateTime.UtcNow, ValidationOutcome.Pass, [], new Dictionary<string, string>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ProposePublishAsync(workflowId!, draftRevision!, report, userId!, CancellationToken.None));
    }

    [Fact]
    public async Task ProposePublishAsync_NullReport_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ProposePublishAsync("wf-1", "rev-1", null!, "user-1", CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "approver-1", "reason")]
    [InlineData("prop-1", null, "reason")]
    [InlineData("prop-1", "approver-1", null)]
    public async Task RejectAsync_MissingArgument_Throws(string? proposalId, string? approverId, string? reason)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.RejectAsync(proposalId!, approverId!, reason!, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "admin-1", "reason")]
    [InlineData("wf-1", null, "reason")]
    [InlineData("wf-1", "admin-1", null)]
    public async Task RetireAsync_MissingArgument_Throws(string? workflowId, string? adminId, string? reason)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.RetireAsync(workflowId!, 1, adminId!, reason!, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "admin-1", "reason")]
    [InlineData("wf-1", null, "reason")]
    [InlineData("wf-1", "admin-1", null)]
    public async Task UnretireAsync_MissingArgument_Throws(string? workflowId, string? adminId, string? reason)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UnretireAsync(workflowId!, 1, adminId!, reason!, CancellationToken.None));
    }
}
