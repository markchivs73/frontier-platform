using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.24: <see cref="DefinitionLifecycleService.ApproveAsync"/> happy path and guards —
/// the publish moment itself (doc 13 §3) had no coverage beyond the distinct-approver check.
/// </summary>
public sealed class DefinitionLifecycleApproveTests
{
    private const string WorkflowId = "wf-test";
    private const string ProposalId = "prop-1";
    private const string Revision = "rev-1";

    private readonly Mock<IDefinitionStore> _store = new();
    private readonly Mock<IDefinitionCompiler> _compiler = new();
    private readonly DefinitionLifecycleService _service;

    public DefinitionLifecycleApproveTests() =>
        _service = new DefinitionLifecycleService(_store.Object, _compiler.Object);

    private static PublishProposalDocument Proposal(ProposalState? state = null) => new()
    {
        Id = ProposalId,
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ProposerId = "user:proposer",
        ProposedAtUtc = DateTime.UtcNow,
        ValidationReportRef = new ValidationReportRef { DocumentId = "report-1" },
        State = state ?? ProposalState.InReview,
    };

    private static DefinitionDraftDocument Draft(string revision = Revision) => new()
    {
        Id = $"{WorkflowId}:draft",
        WorkflowId = WorkflowId,
        State = "draft",
        BaseVersion = 1,
        DraftRevision = revision,
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        LastEditedBy = "user:proposer",
        LastEditedUtc = DateTime.UtcNow,
    };

    private static ValidationReportDocument Report() => new()
    {
        Id = "report-1",
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ValidatedAtUtc = DateTime.UtcNow,
        Outcome = ValidationOutcome.Pass,
        Findings = [],
        ResourceVersions = new Dictionary<string, string>(),
    };

    private void ArrangeHappyPath(int? currentVersion = 1, IReadOnlyList<DefinitionVersionDocument>? existingVersions = null)
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _store.Setup(s => s.GetValidationReportAsync(WorkflowId, Revision, It.IsAny<CancellationToken>())).ReturnsAsync(Report());
        _store.Setup(s => s.ApproveProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _store.Setup(s => s.GetCurrentVersionPointerAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentVersion is null ? null : new CurrentVersionPointerDocument
            {
                Id = $"{WorkflowId}:current",
                WorkflowId = WorkflowId,
                CurrentVersion = currentVersion.Value,
            });
        // Under normal operation the highest existing version equals the pointer; mirror that
        // unless a test overrides it to exercise the pointer-lags-max drift case.
        _store.Setup(s => s.GetAllVersionsAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingVersions ?? VersionsUpTo(currentVersion ?? 0));
        _store.Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionVersionDocument d, CancellationToken _) => d);
        _compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<WorkflowDefinition>())).Returns("sha256:new");
    }

    private static IReadOnlyList<DefinitionVersionDocument> VersionsUpTo(int highest) =>
        [.. Enumerable.Range(1, highest).Select(VersionDoc)];

    private static DefinitionVersionDocument VersionDoc(int version) => new()
    {
        Id = $"{WorkflowId}:v{version}",
        WorkflowId = WorkflowId,
        State = "published",
        DefinitionVersion = version,
        DefinitionHash = $"sha256:v{version}",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:proposer",
        ApprovedBy = "user:approver",
        ProposedUtc = DateTime.UtcNow,
        ApprovedUtc = DateTime.UtcNow,
        ValidationReportRef = "report-1",
    };

    [Fact]
    public async Task ApproveAsync_InReviewProposal_PublishesNextVersionAndMovesPointer()
    {
        ArrangeHappyPath(currentVersion: 1);

        var published = await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(2, published.DefinitionVersion);
        Assert.Equal("sha256:new", published.DefinitionHash);
        Assert.Equal("user:proposer", published.ProposedBy);
        Assert.Equal("user:approver", published.ApprovedBy);
        _store.Verify(s => s.PublishVersionAsync(
            It.Is<DefinitionVersionDocument>(d => d.DefinitionVersion == 2 && d.State == "published" && d.ApprovedBy == "user:approver"),
            It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.SetCurrentVersionAsync(WorkflowId, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_FirstEverPublish_StartsAtVersionOne()
    {
        ArrangeHappyPath(currentVersion: null);

        var published = await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(1, published.DefinitionVersion);
    }

    [Fact]
    public async Task ApproveAsync_PointerLagsHighestVersion_PublishesAboveTheMaxNotThePointer()
    {
        // Pointer says the active version is 1, but versions 2 and 3 already exist (e.g. a
        // retire rolled the pointer back, or the environment was re-seeded with a stale pointer).
        // Deriving the next number from the pointer alone would mint v2 and collide with the
        // existing v2 on hash mismatch — the QG-8 C5 flake. Must publish v4.
        ArrangeHappyPath(currentVersion: 1, existingVersions: VersionsUpTo(3));

        var published = await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(4, published.DefinitionVersion);
        _store.Verify(s => s.PublishVersionAsync(
            It.Is<DefinitionVersionDocument>(d => d.DefinitionVersion == 4),
            It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.SetCurrentVersionAsync(WorkflowId, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, new int[0], 1)]                 // first-ever publish
    [InlineData(2, new[] { 1, 2 }, 3)]               // normal: pointer == max
    [InlineData(1, new[] { 1, 2, 3 }, 4)]            // pointer lags max → publish above max
    [InlineData(5, new[] { 1, 2 }, 6)]               // pointer ahead of visible versions → publish above pointer
    public void NextVersionNumber_TakesOnePastTheGreaterOfPointerAndMax(int? pointerVersion, int[] versions, int expected)
    {
        var pointer = pointerVersion is null ? null : new CurrentVersionPointerDocument
        {
            Id = $"{WorkflowId}:current",
            WorkflowId = WorkflowId,
            CurrentVersion = pointerVersion.Value,
        };
        IReadOnlyList<DefinitionVersionDocument> existing = [.. versions.Select(VersionDoc)];

        Assert.Equal(expected, DefinitionLifecycleService.NextVersionNumber(pointer, existing));
    }

    [Fact]
    public async Task ApproveAsync_UnknownProposal_Throws()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync((PublishProposalDocument?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task ApproveAsync_TerminalState_ThrowsPerStateMachine(string state)
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Proposal(ProposalState.FromName(state)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Contains("not in review", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveAsync_DraftEditedSinceProposal_ThrowsWithdrawnGuidance()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(revision: "rev-2"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Contains("re-propose", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveAsync_MissingDraft_Throws()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync((DefinitionDraftDocument?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_MissingValidationReport_Throws()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _store.Setup(s => s.GetValidationReportAsync(WorkflowId, Revision, It.IsAny<CancellationToken>())).ReturnsAsync((ValidationReportDocument?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_ConcurrentDecision_Throws()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _store.Setup(s => s.GetValidationReportAsync(WorkflowId, Revision, It.IsAny<CancellationToken>())).ReturnsAsync(Report());
        _store.Setup(s => s.ApproveProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Contains("concurrently", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "user:approver")]
    [InlineData("prop-1", null)]
    public async Task ApproveAsync_MissingArguments_Throw(string? proposalId, string? approverId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApproveAsync(proposalId!, approverId!, CancellationToken.None));
    }
}
