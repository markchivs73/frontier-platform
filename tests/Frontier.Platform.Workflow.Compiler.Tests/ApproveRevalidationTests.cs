using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.248 (doc 13 §3): approval validates the proposed revision again against the live registries and
/// refuses with the findings, and writes the proposal, the version and the pointer as one store
/// operation, so a failure part-way leaves nothing for a re-run to trip over.
/// </summary>
public sealed class ApproveRevalidationTests
{
    private const string WorkflowId = "wf-test";
    private const string ProposalId = "prop-1";
    private const string Revision = "rev-1";
    private const string ToolRef = "crm/tickets/get";

    private readonly Mock<IDefinitionStore> _store = new();
    private readonly Mock<IDefinitionCompiler> _compiler = new();
    private readonly Mock<IDefinitionContractEmbedder> _embedder = new();

    public ApproveRevalidationTests()
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(Proposal());
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(WorkflowDefinitionFixture.MinimalDefinition()));
        _store.Setup(s => s.GetValidationReportAsync(WorkflowId, Revision, It.IsAny<CancellationToken>())).ReturnsAsync(ReportDocument());
        _store.Setup(s => s.GetAllVersionsAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _store.Setup(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _embedder.Setup(e => e.EmbedAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition d, CancellationToken _) => d);
        _compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<WorkflowDefinition>())).Returns("sha256:new");
    }

    private DefinitionLifecycleService Service(IDefinitionCompiler? compiler = null) =>
        new(_store.Object, compiler ?? _compiler.Object, _embedder.Object);

    [Fact]
    public async Task ApproveAsync_ARevisionThatNoLongerPasses_IsRefusedWithItsErrorFindings_AndWritesNothing()
    {
        var error = new ValidationFinding("mcp.tool-resolves", ValidationSeverity.Error, "tool gone", NodeId: "m1");
        var warning = new ValidationFinding("graph.something", ValidationSeverity.Warning, "advisory");
        ArrangeRevalidation(ValidationOutcome.Fail, error, warning);

        var ex = await Assert.ThrowsAsync<PublishRevalidationException>(() => Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Equal([error], ex.Findings);
        Assert.Contains("[mcp.tool-resolves] m1: tool gone", ex.Message, StringComparison.Ordinal);
        _embedder.Verify(e => e.EmbedAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_WarningsAlone_DoNotBlockThePublish()
    {
        ArrangeRevalidation(ValidationOutcome.PassWithWarnings, new ValidationFinding("graph.something", ValidationSeverity.Warning, "advisory"));

        var published = await Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(1, published.DefinitionVersion);
    }

    /// <summary>The fresh report only decides the approval; the report stored at proposal is left as it was.</summary>
    [Fact]
    public async Task ApproveAsync_DoesNotPersistTheFreshReport()
    {
        ArrangeRevalidation(ValidationOutcome.Pass);

        await Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        _store.Verify(s => s.PersistValidationReportAsync(It.IsAny<ValidationReportDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The revalidation reads the frozen draft itself, at its revision and its stored schema version.</summary>
    [Fact]
    public async Task ApproveAsync_ValidatesTheFrozenDraftAtItsRevisionAndStoredSchemaVersion()
    {
        var definition = WorkflowDefinitionFixture.MinimalDefinition();
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Draft(definition) with { StoredSchemaVersion = "1.4" });
        ArrangeRevalidation(ValidationOutcome.Pass);

        await Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        _compiler.Verify(c => c.ValidateAsync(definition, Revision, "1.4", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The drift the task names: the revision passed with the registry as it was at submission; the
    /// revalidation sweep then flags the tool's server as drifted. The real rule catalogue, reading the
    /// live catalogue, refuses the approval with <c>mcp.server-not-drifted</c>.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_AToolWhoseServerDriftedAfterSubmission_IsRefusedByTheLiveRegistry()
    {
        var catalog = new MutableToolCatalog { Tools = [Tool(drifted: false)] };
        var compiler = RealCompiler(catalog);
        var definition = DefinitionCalling(ToolRef);
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(definition));
        var atSubmission = await compiler.ValidateAsync(definition, Revision, null, CancellationToken.None);

        catalog.Tools = [Tool(drifted: true)];
        var ex = await Assert.ThrowsAsync<PublishRevalidationException>(() => Service(compiler).ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Equal(ValidationOutcome.Pass, atSubmission.Outcome);
        Assert.Equal(["mcp.server-not-drifted"], ex.Findings.Select(f => f.RuleId));
        _store.Verify(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A tool retired from the registry after submission no longer resolves, and blocks the approval the same way.</summary>
    [Fact]
    public async Task ApproveAsync_AToolRetiredAfterSubmission_IsRefusedByTheLiveRegistry()
    {
        var catalog = new MutableToolCatalog { Tools = [Tool(drifted: false)] };
        var compiler = RealCompiler(catalog);
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(DefinitionCalling(ToolRef)));

        catalog.Tools = [];
        var ex = await Assert.ThrowsAsync<PublishRevalidationException>(() => Service(compiler).ApproveAsync(ProposalId, "user:approver", CancellationToken.None));

        Assert.Equal(["mcp.tool-resolves"], ex.Findings.Select(f => f.RuleId));
    }

    /// <summary>
    /// A simulated failure inside the approve-and-publish write: nothing landed, so the proposal is still
    /// in review, nothing is superseded, and a re-run approves it at the same version number.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_AFailedWrite_LeavesNothingHalfDone_AndARerunPublishesTheSameVersion()
    {
        ArrangeRevalidation(ValidationOutcome.Pass);
        _store.SetupSequence(s => s.ApproveAndPublishAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("batch failed with status 503"))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None));
        var rerun = await Service().ApproveAsync(ProposalId, "user:approver", CancellationToken.None);

        Assert.Equal(1, rerun.DefinitionVersion);
        _store.Verify(s => s.ApproveAndPublishAsync(
            It.IsAny<PublishProposalDocument>(), It.Is<DefinitionVersionDocument>(d => d.DefinitionVersion == 1), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _store.Verify(s => s.ApproveProposalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void ArrangeRevalidation(ValidationOutcome outcome, params ValidationFinding[] findings) =>
        _compiler.Setup(c => c.ValidateAsync(It.IsAny<WorkflowDefinition>(), Revision, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationReport(WorkflowId, Revision, DateTime.UtcNow, outcome, findings, new Dictionary<string, string>()));

    private static DefinitionValidator RealCompiler(IDesignerToolCatalog catalog) =>
        new([new McpServerNotDriftedRule(catalog), new McpToolResolvesRule(catalog)]);

    private static WorkflowDefinition DefinitionCalling(string toolRef) => WorkflowDefinitionFixture.MinimalDefinition() with
    {
        Nodes = [new McpToolNode { NodeId = "m1", ToolRef = toolRef, TimeoutSeconds = 30, IdempotencyKeySpec = "engagementId" }],
    };

    private static DesignerToolDescriptor Tool(bool drifted) => new()
    {
        ToolRef = ToolRef,
        Server = "crm/tickets",
        Name = "get",
        Description = "Reads a ticket",
        ServerDrifted = drifted,
    };

    private static PublishProposalDocument Proposal() => new()
    {
        Id = ProposalId,
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ProposerId = "user:proposer",
        ProposedAtUtc = DateTime.UtcNow,
        ValidationReportRef = new ValidationReportRef { DocumentId = "report-1" },
        State = ProposalState.InReview,
    };

    private static DefinitionDraftDocument Draft(WorkflowDefinition definition) => new()
    {
        Id = $"{WorkflowId}:draft",
        WorkflowId = WorkflowId,
        State = "draft",
        BaseVersion = 0,
        DraftRevision = Revision,
        Definition = definition,
        LastEditedBy = "user:proposer",
        LastEditedUtc = DateTime.UtcNow,
    };

    private static ValidationReportDocument ReportDocument() => new()
    {
        Id = "report-1",
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ValidatedAtUtc = DateTime.UtcNow,
        Outcome = ValidationOutcome.Pass,
        Findings = [],
        ResourceVersions = new Dictionary<string, string>(),
    };

    /// <summary>A registry whose tools a test changes between submission and approval.</summary>
    private sealed class MutableToolCatalog : IDesignerToolCatalog
    {
        public IReadOnlyList<DesignerToolDescriptor> Tools { get; set; } = [];

        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) => Task.FromResult(Tools);
    }
}
