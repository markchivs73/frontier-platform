using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.201: a publish proposal keeps the proposer's optional title and change summary, and approval
/// copies both onto the version. Before this the platform had nowhere to put them, so the consumer
/// API accepted and dropped them.
/// </summary>
public sealed class ProposalTitleAndSummaryTests
{
    private const string WorkflowId = "wf-s13201";
    private const string Revision = "rev-1";
    private const string ProposalId = "wf-s13201:proposal:1";

    private readonly Mock<IDefinitionStore> _store = new();
    private readonly Mock<IDefinitionCompiler> _compiler = new();
    private readonly DefinitionLifecycleService _service;
    private PublishProposalDocument? _created;
    private DefinitionVersionDocument? _published;

    public ProposalTitleAndSummaryTests()
    {
        _service = new DefinitionLifecycleService(_store.Object, _compiler.Object, NoRegistryContracts.Embedder);
        _store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _store.Setup(s => s.PersistValidationReportAsync(It.IsAny<ValidationReportDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidationReportDocument d, CancellationToken _) => d);
        _store.Setup(s => s.CreateProposalAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<CancellationToken>()))
            .Callback((PublishProposalDocument d, CancellationToken _) => _created = d)
            .ReturnsAsync((PublishProposalDocument d, CancellationToken _) => d);
    }

    [Fact]
    public async Task ProposePublishAsync_WithTitleAndSummary_StoresBothOnTheProposal()
    {
        await _service.ProposePublishAsync(WorkflowId, Revision, Report(), "user:mark", "Email step", "Adds an email delivery node", CancellationToken.None);

        Assert.Equal("Email step", _created!.Title);
        Assert.Equal("Adds an email delivery node", _created.ChangeSummary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProposePublishAsync_BlankTitleAndSummary_StoresNeither(string? blank)
    {
        await _service.ProposePublishAsync(WorkflowId, Revision, Report(), "user:mark", blank, blank, CancellationToken.None);

        Assert.Null(_created!.Title);
        Assert.Null(_created.ChangeSummary);
    }

    [Fact]
    public async Task ProposePublishAsync_OverloadWithoutText_StoresNeither()
    {
        await _service.ProposePublishAsync(WorkflowId, Revision, Report(), "user:mark", CancellationToken.None);

        Assert.Null(_created!.Title);
        Assert.Null(_created.ChangeSummary);
    }

    [Fact]
    public async Task ProposePublishAsync_OverloadWithText_KeepsTheGuardsOfTheOriginal()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ProposePublishAsync(WorkflowId, Revision, null!, "user:mark", "t", "s", CancellationToken.None));
        _store.Verify(s => s.CreateProposalAsync(It.IsAny<PublishProposalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Email step", "Adds an email delivery node")]
    [InlineData(null, null)]
    public async Task ApproveAsync_CopiesTheProposalsTitleAndSummaryOntoTheVersion(string? title, string? summary)
    {
        ArrangeApproval(Proposal() with { Title = title, ChangeSummary = summary });

        await _service.ApproveAsync(ProposalId, "user:sarah", CancellationToken.None);

        Assert.Equal(title, _published!.Title);
        Assert.Equal(summary, _published.ChangeSummary);
    }

    [Fact]
    public async Task ProposePublishAsync_WithText_ImplementerThatPredatesTheOverload_ThrowsNotSupported()
    {
        var legacy = new Mock<IDefinitionLifecycleService> { CallBase = true };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => legacy.Object.ProposePublishAsync(WorkflowId, Revision, Report(), "user:mark", "t", "s", CancellationToken.None));

        Assert.Contains(nameof(IDefinitionLifecycleService.ProposePublishAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Documents_WithoutTitleOrSummary_WriteNeitherKey()
    {
        var proposal = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(Proposal()));
        var version = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(Version()));

        Assert.DoesNotContain("\"title\"", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("\"changeSummary\"", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\"", version, StringComparison.Ordinal);
        Assert.DoesNotContain("\"changeSummary\"", version, StringComparison.Ordinal);
    }

    [Fact]
    public void Documents_WithTitleAndSummary_RoundTripThroughTheCanonicalProfile()
    {
        var proposal = Proposal() with { Title = "T", ChangeSummary = "S" };
        var version = Version() with { Title = "T", ChangeSummary = "S" };

        var readProposal = JsonSerializer.Deserialize<PublishProposalDocument>(CanonicalProfile.SerializeCanonical(proposal), CanonicalProfile.Options)!;
        var readVersion = JsonSerializer.Deserialize<DefinitionVersionDocument>(CanonicalProfile.SerializeCanonical(version), CanonicalProfile.Options)!;

        Assert.Equal(("T", "S"), (readProposal.Title, readProposal.ChangeSummary));
        Assert.Equal(("T", "S"), (readVersion.Title, readVersion.ChangeSummary));
    }

    [Fact]
    public void Documents_WrittenWithoutTheKeys_ReadBackWithNeither()
    {
        var proposalBytes = CanonicalProfile.SerializeCanonical(Proposal());
        var versionBytes = CanonicalProfile.SerializeCanonical(Version());

        var readProposal = JsonSerializer.Deserialize<PublishProposalDocument>(proposalBytes, CanonicalProfile.Options)!;
        var readVersion = JsonSerializer.Deserialize<DefinitionVersionDocument>(versionBytes, CanonicalProfile.Options)!;

        Assert.Equal(((string?)null, (string?)null), (readProposal.Title, readProposal.ChangeSummary));
        Assert.Equal(((string?)null, (string?)null), (readVersion.Title, readVersion.ChangeSummary));
    }

    [Fact]
    public void VersionDocument_TitleAndSummary_LeaveTheDefinitionBytesUnchanged()
    {
        var without = JsonDocument.Parse(CanonicalProfile.SerializeCanonical(Version()));
        var with = JsonDocument.Parse(CanonicalProfile.SerializeCanonical(Version() with { Title = "T", ChangeSummary = "S" }));

        Assert.Equal(without.RootElement.GetProperty("definition").GetRawText(), with.RootElement.GetProperty("definition").GetRawText());
    }

    private void ArrangeApproval(PublishProposalDocument proposal)
    {
        _store.Setup(s => s.GetProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(proposal);
        _store.Setup(s => s.GetValidationReportAsync(WorkflowId, Revision, It.IsAny<CancellationToken>())).ReturnsAsync(ReportDocument());
        _store.Setup(s => s.ApproveProposalAsync(ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _store.Setup(s => s.GetAllVersionsAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _store.Setup(s => s.PublishVersionAsync(It.IsAny<DefinitionVersionDocument>(), It.IsAny<CancellationToken>()))
            .Callback((DefinitionVersionDocument d, CancellationToken _) => _published = d)
            .ReturnsAsync((DefinitionVersionDocument d, CancellationToken _) => d);
        _compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<WorkflowDefinition>())).Returns("sha256:new");
    }

    private static DefinitionDraftDocument Draft() => new()
    {
        Id = $"{WorkflowId}:draft",
        WorkflowId = WorkflowId,
        State = "draft",
        BaseVersion = 0,
        DraftRevision = Revision,
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        LastEditedBy = "user:mark",
        LastEditedUtc = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
    };

    private static ValidationReport Report() => new(
        WorkflowId: WorkflowId,
        DraftRevision: Revision,
        ValidatedAtUtc: new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
        Outcome: ValidationOutcome.Pass,
        Findings: [],
        ResourceVersions: new Dictionary<string, string>());

    private static ValidationReportDocument ReportDocument() => new()
    {
        Id = $"{WorkflowId}:report:{Revision}",
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ValidatedAtUtc = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
        Outcome = ValidationOutcome.Pass,
        Findings = [],
        ResourceVersions = new Dictionary<string, string>(),
    };

    private static PublishProposalDocument Proposal() => new()
    {
        Id = ProposalId,
        WorkflowId = WorkflowId,
        DraftRevision = Revision,
        ProposerId = "user:mark",
        ProposedAtUtc = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
        ValidationReportRef = new ValidationReportRef { DocumentId = $"{WorkflowId}:report:{Revision}" },
        State = ProposalState.InReview,
    };

    private static DefinitionVersionDocument Version() => new()
    {
        Id = $"{WorkflowId}:v1",
        WorkflowId = WorkflowId,
        State = "published",
        DefinitionVersion = 1,
        DefinitionHash = "sha256:abc",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
        ApprovedUtc = new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Utc),
        ValidationReportRef = "ref",
    };
}
