using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// In-memory <see cref="IDefinitionStore"/> for tests that exercise the publish lifecycle
/// without an emulator. Lifted with the lifecycle it supports at E3b step 4; the designer's
/// own copy stays with the designer.
/// </summary>
internal sealed class InMemoryDefinitionStore : IDefinitionStore
{
    // S9.57: cross-partition version-health read (unused by this fake).
    public Task<IReadOnlyList<WorkflowHealthDocument>> ListAllVersionHealthAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkflowHealthDocument>>([]);

    // S9.56: workflow usage rollup surface (unused by this fake).
    public Task<WorkflowUsageDocument> UpsertWorkflowUsageAsync(WorkflowUsageDocument usage, CancellationToken ct) =>
        Task.FromResult(usage);
    public Task<IReadOnlyList<WorkflowUsageDocument>> ListAllWorkflowUsageAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkflowUsageDocument>>([]);

    // S9.55: version-health projection surface (unused by this fake).
    public Task<IReadOnlyList<DefinitionVersionDocument>> ListPublishedVersionsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DefinitionVersionDocument>>([]);
    public Task<WorkflowHealthDocument> UpsertVersionHealthAsync(WorkflowHealthDocument health, CancellationToken ct) =>
        Task.FromResult(health);
    public Task<IReadOnlyList<WorkflowHealthDocument>> ListVersionHealthAsync(string workflowId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkflowHealthDocument>>([]);

    private readonly Dictionary<string, DefinitionDraftDocument> drafts = new();
    private readonly Dictionary<string, ChatHistoryDocument> chatHistories = new();
    private readonly Dictionary<string, DesignTurnDocument> turns = new();

    public Task<WorkflowCataloguePage> ListWorkflowsAsync(
        string? engagementType, string? status, string? search, int offset, int limit, CancellationToken ct) =>
        Task.FromResult(new WorkflowCataloguePage([], 0));

    public Task<DefinitionDraftDocument> CreateDraftAsync(
        string workflowId,
        int baseVersion,
        DefinitionDraftDocument draft,
        CancellationToken ct) =>
        Task.FromResult(draft);

    public Task<DefinitionDraftDocument?> GetDraftAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult(drafts.TryGetValue(workflowId, out var draft) ? draft : null);

    public Task<SaveDraftResult> SaveDraftAsync(
        string workflowId,
        DefinitionDraftDocument draft,
        string expectedETag,
        CancellationToken ct)
    {
        drafts[workflowId] = draft;
        return Task.FromResult<SaveDraftResult>(new SaveDraftResultSuccess(draft));
    }

    public Task<DefinitionVersionDocument> PublishVersionAsync(
        DefinitionVersionDocument versionDoc,
        CancellationToken ct) =>
        Task.FromResult(versionDoc);

    public Task<DefinitionVersionDocument?> GetVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct) =>
        Task.FromResult<DefinitionVersionDocument?>(null);

    public Task<IReadOnlyList<DefinitionVersionDocument>> GetAllVersionsAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DefinitionVersionDocument>>(Array.Empty<DefinitionVersionDocument>());

    public Task<CurrentVersionPointerDocument?> GetCurrentVersionPointerAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult<CurrentVersionPointerDocument?>(null);

    public Task SetCurrentVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct) =>
        Task.CompletedTask;

    public Task<PublishProposalDocument> CreateProposalAsync(
        PublishProposalDocument proposal,
        CancellationToken ct) =>
        Task.FromResult(proposal);

    public Task<PublishProposalDocument?> GetProposalAsync(
        string proposalId,
        CancellationToken ct) =>
        Task.FromResult<PublishProposalDocument?>(null);

    public Task<bool> ApproveProposalAsync(
        string proposalId,
        CancellationToken ct) =>
        Task.FromResult(true);

    public Task<bool> RejectProposalAsync(
        string proposalId,
        string reason,
        CancellationToken ct) =>
        Task.FromResult(true);

    public Task<bool> WithdrawProposalAsync(
        string proposalId,
        CancellationToken ct) =>
        Task.FromResult(true);

    public Task<PublishProposalDocument?> GetActiveProposalAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult<PublishProposalDocument?>(null);

    public Task<IReadOnlyList<PublishProposalDocument>> ListPendingProposalsAsync(
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PublishProposalDocument>>([]);

    public Task DeleteDraftAsync(
        string workflowId,
        CancellationToken ct)
    {
        drafts.Remove(workflowId);
        return Task.CompletedTask;
    }

    public Task<ValidationReportDocument> PersistValidationReportAsync(
        ValidationReportDocument report,
        CancellationToken ct) =>
        Task.FromResult(report);

    public Task<ValidationReportDocument?> GetValidationReportAsync(
        string workflowId,
        string draftRevision,
        CancellationToken ct) =>
        Task.FromResult<ValidationReportDocument?>(null);

    public Task<TestRunDocument> PersistTestRunAsync(
        TestRunDocument testRun,
        CancellationToken ct) =>
        Task.FromResult(testRun);

    public Task<TestRunDocument?> GetTestRunAsync(
        string testRunId,
        CancellationToken ct) =>
        Task.FromResult<TestRunDocument?>(null);

    public Task<IReadOnlyList<TestRunDocument>> ListTestRunsAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TestRunDocument>>([]);

    public Task<IReadOnlyList<TestRunDocument>> ListActiveTestRunsAsync(
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TestRunDocument>>([]);

    public Task<DesignTurnDocument> PersistDesignTurnAsync(
        DesignTurnDocument turn,
        CancellationToken ct)
    {
        turns[turn.Id] = turn;
        return Task.FromResult(turn);
    }

    public Task<DesignTurnDocument?> GetDesignTurnAsync(
        string turnDocumentId,
        CancellationToken ct) =>
        Task.FromResult(turns.TryGetValue(turnDocumentId, out var turn) ? turn : null);

    public Task<ChatHistoryDocument?> GetChatHistoryAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult(chatHistories.TryGetValue(workflowId, out var history) ? history : null);

    public Task<ChatHistoryDocument> CreateOrUpdateChatHistoryAsync(
        ChatHistoryDocument history,
        CancellationToken ct)
    {
        chatHistories[history.WorkflowId] = history;
        return Task.FromResult(history);
    }

    public Task<IReadOnlyList<DesignTurnDocument>> GetAllDesignTurnsAsync(
        string workflowId,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DesignTurnDocument>>(
            turns.Values.Where(t => t.WorkflowId == workflowId).ToList().AsReadOnly());
}
