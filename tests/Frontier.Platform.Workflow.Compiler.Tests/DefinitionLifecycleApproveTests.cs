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
        _service = new DefinitionLifecycleService(_store.Object, _compiler.Object, NoRegistryContracts.Embedder);

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
        _store.Setup(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ArrangeRevalidation(ValidationOutcome.Pass);
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
        _compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<WorkflowDefinition>())).Returns("sha256:new");
    }

    private void ArrangeRevalidation(ValidationOutcome outcome, params ValidationFinding[] findings) =>
        _compiler.Setup(c => c.ValidateAsync(It.IsAny<WorkflowDefinition>(), Revision, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationReport(WorkflowId, Revision, DateTime.UtcNow, outcome, findings, new Dictionary<string, string>()));

    /// <summary>Verifies the one approve-and-publish write named the proposal and a version matching <paramref name="version"/>.</summary>
    private void VerifyPublished(System.Linq.Expressions.Expression<Func<DefinitionVersionDocument, bool>> version, Times? times = null) =>
        _store.Verify(s => s.ApproveAndPublishAsync(
            It.Is<PublishProposalDocument>(p => p.Id == ProposalId),
            It.Is(version),
            It.IsAny<CancellationToken>()), times ?? Times.Once());

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
        VerifyPublished(d => d.DefinitionVersion == 2 && d.State == "published" && d.ApprovedBy == "user:approver");
    }

    /// <summary>
    /// S13.138: the published definition names its own hash. A run carries this definition inline,
    /// and the audit reads its hash from it, so before the fix every UI-published run's audit
    /// record had an empty definition hash.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_StampsTheHashOntoThePublishedDefinition()
    {
        ArrangeHappyPath(currentVersion: 1);

        await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        VerifyPublished(d => d.Definition.DefinitionHash == "sha256:new" && d.DefinitionHash == "sha256:new");
    }

    /// <summary>ADR-E27 D15: publish embeds the contracts first, and hashes and stores the definition that carries them.</summary>
    [Fact]
    public async Task ApproveAsync_HashesAndStoresTheDefinitionWithItsContractsEmbedded()
    {
        ArrangeHappyPath(currentVersion: 1);
        var embedder = new Mock<IDefinitionContractEmbedder>();
        IReadOnlyList<Frontier.Platform.Abstractions.ContractSnapshot> pinned = [Frontier.Platform.Workflow.Model.EngineContracts.GateDecision];
        embedder.Setup(e => e.EmbedAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition d, CancellationToken _) => d with { Contracts = pinned });
        var service = new DefinitionLifecycleService(_store.Object, _compiler.Object, embedder.Object);

        await service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        _compiler.Verify(c => c.ComputeDefinitionHash(It.Is<WorkflowDefinition>(d => d.Contracts == pinned)), Times.Once);
        VerifyPublished(d => d.Definition.Contracts == pinned && d.Definition.DefinitionHash == "sha256:new");
    }

    /// <summary>ADR-E27 D15: a contract that cannot be pinned stops the publish before the proposal is approved.</summary>
    [Fact]
    public async Task ApproveAsync_AContractThatCannotBePinned_LeavesTheProposalUnapproved()
    {
        ArrangeHappyPath(currentVersion: 1);
        var embedder = new Mock<IDefinitionContractEmbedder>();
        embedder.Setup(e => e.EmbedAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not registered"));
        var service = new DefinitionLifecycleService(_store.Object, _compiler.Object, embedder.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        VerifyPublished(_ => true, Times.Never());
    }

    [Fact]
    public void Constructor_RefusesANullEmbedder()
    {
        Assert.Throws<ArgumentNullException>(() => new DefinitionLifecycleService(_store.Object, _compiler.Object, null!));
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
        VerifyPublished(d => d.DefinitionVersion == 4);
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
        ArrangeRevalidation(ValidationOutcome.Pass);
        _store.Setup(s => s.GetAllVersionsAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _store.Setup(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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

    /// <summary>
    /// S13.223 (GOV-14): publishing v3 supersedes the versions it replaces, after the pointer moves,
    /// and leaves the draft open for the next edit.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_SupersedesThePredecessors_AfterMovingThePointer_AndKeepsTheDraft()
    {
        ArrangeHappyPath(currentVersion: 2);
        var calls = new List<string>();
        _store.Setup(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.Is<DefinitionVersionDocument>(d => d.DefinitionVersion == 3), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("publish")).ReturnsAsync(true);
        _store.Setup(s => s.SupersedeVersionAsync(WorkflowId, It.IsAny<int>(), 3, It.IsAny<CancellationToken>()))
            .Callback((string _, int v, int _, CancellationToken _) => calls.Add($"supersede v{v}")).ReturnsAsync(true);

        await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(["publish", "supersede v1", "supersede v2"], calls);
        _store.Verify(s => s.DeleteDraftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A version that changed state concurrently (the store answers false) does not fail the publish.</summary>
    [Fact]
    public async Task ApproveAsync_APredecessorThatCannotBeSuperseded_StillPublishes()
    {
        ArrangeHappyPath(currentVersion: 1);
        _store.Setup(s => s.SupersedeVersionAsync(WorkflowId, 1, 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var published = await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(2, published.DefinitionVersion);
        _store.Verify(s => s.SupersedeVersionAsync(WorkflowId, 1, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A store failure while superseding surfaces to the caller, but only after the new version is
    /// stored and current: the old versions stay published, never the new one left unreachable.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_SupersedeFails_AfterTheNewVersionIsCurrent()
    {
        ArrangeHappyPath(currentVersion: 1);
        _store.Setup(s => s.SupersedeVersionAsync(WorkflowId, 1, 2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        VerifyPublished(d => d.DefinitionVersion == 2);
    }

    [Fact]
    public async Task ApproveAsync_FirstEverPublish_SupersedesNothing()
    {
        ArrangeHappyPath(currentVersion: null);

        await _service.ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        _store.Verify(s => s.SupersedeVersionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Published and retired versions without a successor are targets, oldest first; a superseded
    /// version (with or without a recorded successor), one that already names a successor, and the
    /// successor itself are not.
    /// </summary>
    [Fact]
    public void SupersessionTargets_NamesOlderPublishedAndRetiredVersionsWithoutASuccessor()
    {
        IReadOnlyList<DefinitionVersionDocument> versions =
        [
            VersionDoc(6) with { State = "published" },
            VersionDoc(5) with { State = "published" },
            VersionDoc(4) with { State = "retired" },
            VersionDoc(3) with { State = "retired", SupersededByVersion = 4 },
            VersionDoc(2) with { State = "superseded", SupersededByVersion = 3 },
            VersionDoc(1) with { State = "published" },
            VersionDoc(0) with { State = "superseded" },
        ];

        Assert.Equal([1, 4, 5], DefinitionLifecycleService.SupersessionTargets(versions, successor: 6));
    }
}
