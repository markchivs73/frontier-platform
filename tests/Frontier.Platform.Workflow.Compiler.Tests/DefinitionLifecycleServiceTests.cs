using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

public sealed class DefinitionLifecycleServiceTests
{
    private readonly Mock<IDefinitionStore> _mockStore;
    private readonly Mock<IDefinitionCompiler> _mockCompiler;
    private readonly DefinitionLifecycleService _service;

    public DefinitionLifecycleServiceTests()
    {
        _mockStore = new Mock<IDefinitionStore>();
        _mockCompiler = new Mock<IDefinitionCompiler>();
        _service = new DefinitionLifecycleService(_mockStore.Object, _mockCompiler.Object);
    }

    [Fact]
    public async Task CreateDraftAsync_FromExistingVersion_ReturnsNewDraft()
    {
        const string workflowId = "wf-test";
        const string userId = "user:mark";
        const int sourceVersion = 1;

        var sourceDef = WorkflowDefinitionFixture.MinimalDefinition();
        var sourceDoc = new DefinitionVersionDocument
        {
            Id = $"{workflowId}:v{sourceVersion}",
            WorkflowId = workflowId,
            State = "published",
            DefinitionVersion = sourceVersion,
            DefinitionHash = "sha256:test",
            Definition = sourceDef,
            ProposedBy = "user:sarah",
            ApprovedBy = "user:sarah",
            ProposedUtc = DateTime.UtcNow,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = "report-ref"
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, sourceVersion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceDoc);

        var draftRevision = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var draftDoc = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = sourceVersion,
            DraftRevision = draftRevision,
            Definition = sourceDef,
            LastEditedBy = userId,
            LastEditedUtc = now
        };

        _mockStore
            .Setup(s => s.CreateDraftAsync(workflowId, sourceVersion, It.IsAny<DefinitionDraftDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draftDoc);

        var handle = await _service.CreateDraftAsync(workflowId, sourceVersion, userId, CancellationToken.None);

        Assert.NotNull(handle);
        Assert.Equal(workflowId, handle.WorkflowId);
        Assert.NotEmpty(handle.DraftRevision);

        _mockStore.Verify(s => s.GetVersionAsync(workflowId, sourceVersion, It.IsAny<CancellationToken>()), Times.Once);
        _mockStore.Verify(s => s.CreateDraftAsync(workflowId, sourceVersion, It.IsAny<DefinitionDraftDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WithoutSourceVersion_CreatesEmptyDraft()
    {
        const string workflowId = "wf-new";
        const string userId = "user:mark";

        var draftRevision = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var returnedDoc = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 0,
            DraftRevision = draftRevision,
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = userId,
            LastEditedUtc = now
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        _mockStore
            .Setup(s => s.CreateDraftAsync(workflowId, 0, It.IsAny<DefinitionDraftDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedDoc);

        var handle = await _service.CreateDraftAsync(workflowId, fromVersion: null, userId, CancellationToken.None);

        Assert.NotNull(handle);
        Assert.Equal(workflowId, handle.WorkflowId);
        Assert.NotEmpty(handle.DraftRevision);

        _mockStore.Verify(
            s => s.CreateDraftAsync(workflowId, 0, It.Is<DefinitionDraftDocument>(d => d.BaseVersion == 0), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_DraftAlreadyExists_Throws()
    {
        const string workflowId = "wf-test";
        const string userId = "user:mark";

        var existingDraft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 0,
            DraftRevision = "rev-1",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDraft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateDraftAsync(workflowId, 1, userId, CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
    }

    // fromVersion.HasValue is true but GetVersionAsync returns null — every other from-version test
    // supplies an existing source version (S9.24 branch-coverage gap: line 51).
    [Fact]
    public async Task CreateDraftAsync_SourceVersionNotFound_Throws()
    {
        const string workflowId = "wf-test";
        const string userId = "user:mark";

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionVersionDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateDraftAsync(workflowId, 5, userId, CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveDraftAsync_ValidDefinition_ReturnSuccess()
    {
        const string workflowId = "wf-test";
        const string userId = "user:mark";
        const string expectedRevision = "rev-old";

        var currentDraft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 0,
            DraftRevision = expectedRevision,
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        var definition = WorkflowDefinitionFixture.MinimalDefinition();

        var newRevision = Guid.NewGuid().ToString();
        var savedDraft = currentDraft with
        {
            DraftRevision = newRevision,
            Definition = definition,
            LastEditedBy = userId
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentDraft);

        _mockStore
            .Setup(s => s.SaveDraftAsync(workflowId, It.IsAny<DefinitionDraftDocument>(), expectedRevision, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveDraftResultSuccess(savedDraft));

        var response = await _service.SaveDraftAsync(workflowId, definition, expectedRevision, userId, CancellationToken.None);

        Assert.IsType<SaveDraftResponseSuccess>(response);
        var success = (SaveDraftResponseSuccess)response;
        Assert.Equal(workflowId, success.Draft.WorkflowId);
        Assert.NotEmpty(success.Draft.DraftRevision);
    }

    [Fact]
    public async Task SaveDraftAsync_StaleRevision_ReturnsConflict()
    {
        const string workflowId = "wf-test";
        const string userId = "user:mark";
        const string expectedRevision = "rev-old";
        const string currentETag = "rev-current";

        var currentDraft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 0,
            DraftRevision = "rev-latest",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        var definition = WorkflowDefinitionFixture.MinimalDefinition();

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentDraft);

        _mockStore
            .Setup(s => s.SaveDraftAsync(workflowId, It.IsAny<DefinitionDraftDocument>(), expectedRevision, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveDraftResultConflict(currentETag, "rev-latest", currentDraft));

        var response = await _service.SaveDraftAsync(workflowId, definition, expectedRevision, userId, CancellationToken.None);

        Assert.IsType<SaveDraftResponseConflict>(response);
        var conflict = (SaveDraftResponseConflict)response;
        Assert.Equal("rev-latest", conflict.CurrentRevision);
    }

    // current == null — every other SaveDraftAsync test has an existing draft (S9.24 branch-coverage gap: line 110).
    [Fact]
    public async Task SaveDraftAsync_DraftNotFound_Throws()
    {
        const string workflowId = "wf-missing";

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveDraftAsync(workflowId, WorkflowDefinitionFixture.MinimalDefinition(), "rev-1", "user-1", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A <see cref="SaveDraftResult"/> subtype no switch arm recognises (the type is abstract,
    /// not a closed union), used to force the default arm of SaveDraftAsync's result switch.</summary>
    private sealed record UnrecognisedSaveDraftResult : SaveDraftResult;

    // saveResult switch default arm ("_ => throw") — every other test supplies Success or Conflict
    // (S9.24 branch-coverage gap: line 130).
    [Fact]
    public async Task SaveDraftAsync_UnrecognisedResultType_Throws()
    {
        const string workflowId = "wf-test";
        var currentDraft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 0,
            DraftRevision = "rev-old",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentDraft);

        _mockStore
            .Setup(s => s.SaveDraftAsync(workflowId, It.IsAny<DefinitionDraftDocument>(), "rev-old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnrecognisedSaveDraftResult());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveDraftAsync(workflowId, WorkflowDefinitionFixture.MinimalDefinition(), "rev-old", "user:mark", CancellationToken.None));

        Assert.Contains("Unknown save result", ex.Message, StringComparison.Ordinal);
    }

    // current == null — every other ApplyAgentMergeAsync test has an existing draft
    // (S9.24 branch-coverage gap: line 162).
    [Fact]
    public async Task ApplyAgentMergeAsync_DraftNotFound_Throws()
    {
        const string workflowId = "wf-missing";

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyAgentMergeAsync(workflowId, new List<string>(), "rev-1", "user:mark", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveAsync_WithDistinctApproverPolicy_RejectsSelf()
    {
        const string proposalId = "prop-123";
        const string approverId = "user:mark";

        var proposal = new PublishProposalDocument
        {
            Id = proposalId,
            WorkflowId = "wf-test",
            DraftRevision = "rev-1",
            ProposerId = approverId,  // Same as approver
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.InReview
        };

        _mockStore
            .Setup(s => s.GetProposalAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var strictService = new DefinitionLifecycleService(
            _mockStore.Object,
            _mockCompiler.Object,
            new PublishGovernanceConfig { RequireDistinctApprover = true });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strictService.ApproveAsync(proposalId, approverId, CancellationToken.None));

        Assert.Contains("Distinct approver required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAgentMergeAsync_NoConflict_ReturnsSuccess()
    {
        const string workflowId = "wf-test";
        const string expectedRevision = "rev-1";

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = expectedRevision,
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var result = await _service.ApplyAgentMergeAsync(
            workflowId,
            new List<string>(),
            expectedRevision,
            "user:mark",
            CancellationToken.None);

        Assert.IsType<MergeOutcomeSuccess>(result);
        var success = (MergeOutcomeSuccess)result;
        Assert.Equal(workflowId, success.Draft.WorkflowId);
    }

    [Fact]
    public async Task ApplyAgentMergeAsync_RevisionsDoNotMatch_ReturnsConflict()
    {
        const string workflowId = "wf-test";
        const string expectedRevision = "rev-1";

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = "rev-2",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var result = await _service.ApplyAgentMergeAsync(
            workflowId,
            new List<string>(),
            expectedRevision,
            "user:mark",
            CancellationToken.None);

        Assert.IsType<MergeOutcomeConflict>(result);
    }

    [Fact]
    public async Task ProposePublishAsync_ValidReport_CreatesProposal()
    {
        const string workflowId = "wf-test";
        const string draftRevision = "rev-1";
        const string userId = "user:mark";

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = draftRevision,
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: draftRevision,
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Pass,
            Findings: Array.Empty<ValidationFinding>().AsReadOnly(),
            ResourceVersions: new Dictionary<string, string>().AsReadOnly());

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var proposalDoc = new PublishProposalDocument
        {
            Id = "prop-123",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ProposerId = userId,
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.InReview
        };

        var reportDoc = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:{draftRevision}",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ValidatedAtUtc = DateTime.UtcNow,
            Outcome = ValidationOutcome.Pass,
            Findings = Array.Empty<ValidationFinding>().AsReadOnly(),
            ResourceVersions = new Dictionary<string, string>().AsReadOnly()
        };

        _mockStore
            .Setup(s => s.PersistValidationReportAsync(It.IsAny<ValidationReportDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reportDoc);

        _mockStore
            .Setup(s => s.CreateProposalAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposalDoc);

        var result = await _service.ProposePublishAsync(workflowId, draftRevision, report, userId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(workflowId, result.WorkflowId);
        Assert.Equal(ProposalState.InReview, result.State);
    }

    // draft == null — every other ProposePublishAsync test has an existing draft
    // (S9.24 branch-coverage gap: line 196).
    [Fact]
    public async Task ProposePublishAsync_DraftNotFound_Throws()
    {
        const string workflowId = "wf-missing";
        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: "rev-1",
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Pass,
            Findings: Array.Empty<ValidationFinding>().AsReadOnly(),
            ResourceVersions: new Dictionary<string, string>().AsReadOnly());

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ProposePublishAsync(workflowId, "rev-1", report, "user:mark", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    // draft.DraftRevision != draftRevision — every other test passes the current revision
    // (S9.24 branch-coverage gap: line 199).
    [Fact]
    public async Task ProposePublishAsync_RevisionMismatch_Throws()
    {
        const string workflowId = "wf-test";
        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = "rev-current",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };
        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: "rev-stale",
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Pass,
            Findings: Array.Empty<ValidationFinding>().AsReadOnly(),
            ResourceVersions: new Dictionary<string, string>().AsReadOnly());

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ProposePublishAsync(workflowId, "rev-stale", report, "user:mark", CancellationToken.None));

        Assert.Contains("revision mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposePublishAsync_FailingReport_Throws()
    {
        const string workflowId = "wf-test";
        const string draftRevision = "rev-1";
        const string userId = "user:mark";

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = draftRevision,
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: draftRevision,
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Fail,
            Findings: Array.Empty<ValidationFinding>().AsReadOnly(),
            ResourceVersions: new Dictionary<string, string>().AsReadOnly());

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ProposePublishAsync(workflowId, draftRevision, report, userId, CancellationToken.None));

        Assert.Contains("failing validation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectAsync_ValidProposal_Succeeds()
    {
        const string proposalId = "prop-123";
        const string approverId = "user:mark";
        const string reason = "Needs revision";

        var proposal = new PublishProposalDocument
        {
            Id = proposalId,
            WorkflowId = "wf-test",
            DraftRevision = "rev-1",
            ProposerId = "user:sarah",
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.InReview
        };

        _mockStore
            .Setup(s => s.GetProposalAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _mockStore
            .Setup(s => s.RejectProposalAsync(proposalId, reason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.RejectAsync(proposalId, approverId, reason, CancellationToken.None);

        _mockStore.Verify(s => s.RejectProposalAsync(proposalId, reason, It.IsAny<CancellationToken>()), Times.Once);
    }

    // proposal == null — every other RejectAsync test has an existing proposal
    // (S9.24 branch-coverage gap: line 342).
    [Fact]
    public async Task RejectAsync_ProposalNotFound_Throws()
    {
        const string proposalId = "prop-missing";

        _mockStore
            .Setup(s => s.GetProposalAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishProposalDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RejectAsync(proposalId, "approver-1", "reason", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    // !rejected — every other RejectAsync test has RejectProposalAsync succeed
    // (S9.24 branch-coverage gap: line 349).
    [Fact]
    public async Task RejectAsync_StoreReportsFailure_Throws()
    {
        const string proposalId = "prop-123";
        var proposal = new PublishProposalDocument
        {
            Id = proposalId,
            WorkflowId = "wf-test",
            DraftRevision = "rev-1",
            ProposerId = "user:sarah",
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.InReview
        };

        _mockStore
            .Setup(s => s.GetProposalAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _mockStore
            .Setup(s => s.RejectProposalAsync(proposalId, "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RejectAsync(proposalId, "approver-1", "reason", CancellationToken.None));

        Assert.Contains("modified concurrently", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectAsync_NotInReview_Throws()
    {
        const string proposalId = "prop-123";
        const string approverId = "user:mark";

        var proposal = new PublishProposalDocument
        {
            Id = proposalId,
            WorkflowId = "wf-test",
            DraftRevision = "rev-1",
            ProposerId = "user:sarah",
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.Approved
        };

        _mockStore
            .Setup(s => s.GetProposalAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RejectAsync(proposalId, approverId, "reason", CancellationToken.None));

        Assert.Contains("not in review", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetireAsync_PublishedVersion_Succeeds()
    {
        const string workflowId = "wf-test";
        const int version = 1;
        const string adminId = "user:mark";
        const string reason = "End of life";

        var versionDoc = new DefinitionVersionDocument
        {
            Id = $"{workflowId}:v{version}",
            WorkflowId = workflowId,
            State = "published",
            DefinitionVersion = version,
            DefinitionHash = "sha256:test",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            ProposedBy = "user:sarah",
            ApprovedBy = "user:mark",
            ProposedUtc = DateTime.UtcNow,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = "report-ref"
        };

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionDoc);

        _mockStore
            .Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionDoc);

        await _service.RetireAsync(workflowId, version, adminId, reason, CancellationToken.None);

        _mockStore.Verify(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // versionDoc == null — every other RetireAsync test has an existing version
    // (S9.24 branch-coverage gap: line 365).
    [Fact]
    public async Task RetireAsync_VersionNotFound_Throws()
    {
        const string workflowId = "wf-test";

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionVersionDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RetireAsync(workflowId, 9, "admin", "reason", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    // versionDoc.State == "retired" — every other RetireAsync test targets a "published" version
    // (S9.24 branch-coverage gap: line 368).
    [Fact]
    public async Task RetireAsync_AlreadyRetired_Throws()
    {
        const string workflowId = "wf-test";
        const int version = 1;
        var versionDoc = new DefinitionVersionDocument
        {
            Id = $"{workflowId}:v{version}",
            WorkflowId = workflowId,
            State = "retired",
            DefinitionVersion = version,
            DefinitionHash = "sha256:test",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            ProposedBy = "user:sarah",
            ApprovedBy = "user:mark",
            ProposedUtc = DateTime.UtcNow,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = "report-ref",
            Retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "admin", Reason = "prior" }
        };

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionDoc);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RetireAsync(workflowId, version, "admin", "reason", CancellationToken.None));

        Assert.Contains("already retired", ex.Message, StringComparison.Ordinal);
    }

    // versionDoc == null — every other UnretireAsync test has an existing version
    // (S9.24 branch-coverage gap: line 400).
    [Fact]
    public async Task UnretireAsync_VersionNotFound_Throws()
    {
        const string workflowId = "wf-test";

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionVersionDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UnretireAsync(workflowId, 9, "admin", "reason", CancellationToken.None));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    // versionDoc.State != "retired" — every other UnretireAsync test targets a "retired" version
    // (S9.24 branch-coverage gap: line 403).
    [Fact]
    public async Task UnretireAsync_NotRetired_Throws()
    {
        const string workflowId = "wf-test";
        const int version = 1;
        var versionDoc = new DefinitionVersionDocument
        {
            Id = $"{workflowId}:v{version}",
            WorkflowId = workflowId,
            State = "published",
            DefinitionVersion = version,
            DefinitionHash = "sha256:test",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            ProposedBy = "user:sarah",
            ApprovedBy = "user:mark",
            ProposedUtc = DateTime.UtcNow,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = "report-ref"
        };

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionDoc);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UnretireAsync(workflowId, version, "admin", "reason", CancellationToken.None));

        Assert.Contains("is not retired", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnretireAsync_RetiredVersion_Succeeds()
    {
        const string workflowId = "wf-test";
        const int version = 1;
        const string adminId = "user:mark";
        const string reason = "Restore service";

        var versionDoc = new DefinitionVersionDocument
        {
            Id = $"{workflowId}:v{version}",
            WorkflowId = workflowId,
            State = "retired",
            DefinitionVersion = version,
            DefinitionHash = "sha256:test",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            ProposedBy = "user:sarah",
            ApprovedBy = "user:mark",
            ProposedUtc = DateTime.UtcNow,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = "report-ref",
            Retirement = new RetirementInfoDocument
            {
                RetiredAtUtc = DateTime.UtcNow,
                RetiredBy = "user:admin",
                Reason = "Old version"
            }
        };

        _mockStore
            .Setup(s => s.GetVersionAsync(workflowId, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionDoc);

        var unretiredDoc = versionDoc with { State = "published", Retirement = null };
        _mockStore
            .Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unretiredDoc);

        await _service.UnretireAsync(workflowId, version, adminId, reason, CancellationToken.None);

        _mockStore.Verify(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_WithVersions_ReturnsHistory()
    {
        const string workflowId = "wf-test";

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            DraftRevision = "rev-1",
            Definition = WorkflowDefinitionFixture.MinimalDefinition(),
            LastEditedBy = "user:sarah",
            LastEditedUtc = DateTime.UtcNow
        };

        var versions = new List<DefinitionVersionDocument>
        {
            new DefinitionVersionDocument
            {
                Id = $"{workflowId}:v1",
                WorkflowId = workflowId,
                State = "published",
                DefinitionVersion = 1,
                DefinitionHash = "sha256:test",
                Definition = WorkflowDefinitionFixture.MinimalDefinition(),
                ProposedBy = "user:sarah",
                ApprovedBy = "user:mark",
                ProposedUtc = DateTime.UtcNow,
                ApprovedUtc = DateTime.UtcNow,
                ValidationReportRef = "report-ref"
            }
        };

        var pointer = new CurrentVersionPointerDocument
        {
            Id = $"{workflowId}:current",
            WorkflowId = workflowId,
            CurrentVersion = 1
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        _mockStore
            .Setup(s => s.GetCurrentVersionPointerAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pointer);

        _mockStore
            .Setup(s => s.GetAllVersionsAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versions.AsReadOnly());

        var result = await _service.GetHistoryAsync(workflowId, CancellationToken.None);

        Assert.Equal(workflowId, result.WorkflowId);
        Assert.NotNull(result.Draft);
        Assert.Equal(1, result.CurrentVersion);
        Assert.Single(result.Versions);
    }

    // draft != null ? ... : null (else arm) and pointer?.CurrentVersion ?? 0 (null arm) — every
    // other GetHistoryAsync test has both a draft and a current-version pointer
    // (S9.24 branch-coverage gap: lines 440, 448).
    [Fact]
    public async Task GetHistoryAsync_NoDraftOrPointer_ReturnsNullDraftAndZeroVersion()
    {
        const string workflowId = "wf-no-draft";

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        _mockStore
            .Setup(s => s.GetCurrentVersionPointerAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrentVersionPointerDocument?)null);

        _mockStore
            .Setup(s => s.GetAllVersionsAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DefinitionVersionDocument>().AsReadOnly());

        var result = await _service.GetHistoryAsync(workflowId, CancellationToken.None);

        Assert.Null(result.Draft);
        Assert.Equal(0, result.CurrentVersion);
    }

    // v.Retirement != null ? ... : null inside the Select lambda — the existing history test only
    // supplies non-retired versions; this mixes a retired and a non-retired version so both arms of
    // the per-version ternary run (S9.24 branch-coverage gap: <>c line 425 / the ternary at line 433).
    [Fact]
    public async Task GetHistoryAsync_MixedRetirementVersions_MapsRetirementInfoOnlyForRetired()
    {
        const string workflowId = "wf-test";

        var versions = new List<DefinitionVersionDocument>
        {
            new()
            {
                Id = $"{workflowId}:v1",
                WorkflowId = workflowId,
                State = "retired",
                DefinitionVersion = 1,
                DefinitionHash = "sha256:v1",
                Definition = WorkflowDefinitionFixture.MinimalDefinition(),
                ProposedBy = "user:sarah",
                ApprovedBy = "user:mark",
                ProposedUtc = DateTime.UtcNow,
                ApprovedUtc = DateTime.UtcNow,
                ValidationReportRef = "report-ref-1",
                Retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "admin", Reason = "superseded" }
            },
            new()
            {
                Id = $"{workflowId}:v2",
                WorkflowId = workflowId,
                State = "published",
                DefinitionVersion = 2,
                DefinitionHash = "sha256:v2",
                Definition = WorkflowDefinitionFixture.MinimalDefinition(),
                ProposedBy = "user:sarah",
                ApprovedBy = "user:mark",
                ProposedUtc = DateTime.UtcNow,
                ApprovedUtc = DateTime.UtcNow,
                ValidationReportRef = "report-ref-2"
            }
        };

        _mockStore
            .Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DefinitionDraftDocument?)null);

        _mockStore
            .Setup(s => s.GetCurrentVersionPointerAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentVersionPointerDocument { Id = $"{workflowId}:current", WorkflowId = workflowId, CurrentVersion = 2 });

        _mockStore
            .Setup(s => s.GetAllVersionsAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versions.AsReadOnly());

        var result = await _service.GetHistoryAsync(workflowId, CancellationToken.None);

        var v1 = result.Versions.Single(v => v.Version == 1);
        var v2 = result.Versions.Single(v => v.Version == 2);
        Assert.NotNull(v1.RetirementInfo);
        Assert.Equal("superseded", v1.RetirementInfo!.Reason);
        Assert.Null(v2.RetirementInfo);
    }

    // ── DeleteDraftAsync (S9.42, doc 13 §2 delete transition) ────────────────────

    [Fact]
    public async Task DeleteDraftAsync_EmptyWorkflowId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.DeleteDraftAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteDraftAsync_NoDraft_ReturnsNotFound()
    {
        const string workflowId = "wf-missing";
        _mockStore.Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync((DefinitionDraftDocument?)null);

        var result = await _service.DeleteDraftAsync(workflowId, CancellationToken.None);

        Assert.IsType<DeleteDraftResultNotFound>(result);
        _mockStore.Verify(s => s.DeleteDraftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDraftAsync_ActiveProposalInReview_ReturnsInReview()
    {
        const string workflowId = "wf-in-review";
        _mockStore.Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildDraft(workflowId));
        _mockStore.Setup(s => s.GetActiveProposalAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(new PublishProposalDocument
        {
            Id = $"{workflowId}:proposal:p1",
            WorkflowId = workflowId,
            DraftRevision = "rev-1",
            ProposerId = "user:designer",
            ProposedAtUtc = DateTime.UtcNow,
            ValidationReportRef = new ValidationReportRef { DocumentId = "report-ref" },
            State = ProposalState.InReview
        });

        var result = await _service.DeleteDraftAsync(workflowId, CancellationToken.None);

        Assert.IsType<DeleteDraftResultInReview>(result);
        _mockStore.Verify(s => s.DeleteDraftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDraftAsync_NoPublishedVersions_DeletesAndReportsWorkflowDeleted()
    {
        const string workflowId = "wf-never-published";
        _mockStore.Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildDraft(workflowId));
        _mockStore.Setup(s => s.GetActiveProposalAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync((PublishProposalDocument?)null);
        _mockStore.Setup(s => s.GetAllVersionsAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<DefinitionVersionDocument>)[]);

        var result = await _service.DeleteDraftAsync(workflowId, CancellationToken.None);

        var success = Assert.IsType<DeleteDraftResultSuccess>(result);
        Assert.True(success.WorkflowDeleted);
        _mockStore.Verify(s => s.DeleteDraftAsync(workflowId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDraftAsync_HasPublishedVersion_DeletesButWorkflowSurvives()
    {
        const string workflowId = "wf-published";
        _mockStore.Setup(s => s.GetDraftAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(BuildDraft(workflowId));
        _mockStore.Setup(s => s.GetActiveProposalAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync((PublishProposalDocument?)null);
        _mockStore.Setup(s => s.GetAllVersionsAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<DefinitionVersionDocument>)
        [
            new DefinitionVersionDocument
            {
                Id = $"{workflowId}:v1",
                WorkflowId = workflowId,
                State = "published",
                DefinitionVersion = 1,
                DefinitionHash = "sha256:v1",
                Definition = WorkflowDefinitionFixture.MinimalDefinition(),
                ProposedBy = "user:sarah",
                ApprovedBy = "user:mark",
                ProposedUtc = DateTime.UtcNow,
                ApprovedUtc = DateTime.UtcNow,
                ValidationReportRef = "report-ref-1"
            }
        ]);

        var result = await _service.DeleteDraftAsync(workflowId, CancellationToken.None);

        var success = Assert.IsType<DeleteDraftResultSuccess>(result);
        Assert.False(success.WorkflowDeleted);
        _mockStore.Verify(s => s.DeleteDraftAsync(workflowId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DefinitionDraftDocument BuildDraft(string workflowId) => new()
    {
        Id = $"{workflowId}:draft",
        WorkflowId = workflowId,
        State = "draft",
        BaseVersion = 0,
        DraftRevision = "rev-1",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        LastEditedBy = "user:designer",
        LastEditedUtc = DateTime.UtcNow
    };
}

internal static class WorkflowDefinitionFixture
{
    public static WorkflowDefinition MinimalDefinition()
    {
        return new WorkflowDefinition
        {
            WorkflowId = "wf-test",
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = new List<WorkflowNode>(),
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "",
            Mode = ExecutionMode.OneShot
        };
    }
}
