using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.206 (option C): a definition with a blank name or engagement type fails validation on
/// <c>structure.required-fields</c>, and the publish proposal refuses that report. Covers the
/// definition a brand-new draft starts with and a draft that has a name but no engagement type.
/// </summary>
public sealed class RequiredFieldsPublishTests
{
    private const string WorkflowId = "wf-required";
    private const string DraftRevision = "rev-1";

    [Fact]
    public async Task CreateDraftAsync_WithoutSourceVersion_StartsWithBlankNameAndEngagementType()
    {
        var store = new Mock<IDefinitionStore>();
        DefinitionDraftDocument? created = null;
        store.Setup(s => s.CreateDraftAsync(WorkflowId, 0, It.IsAny<DefinitionDraftDocument>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, DefinitionDraftDocument, CancellationToken>((_, _, d, _) => created = d)
            .ReturnsAsync((string _, int _, DefinitionDraftDocument d, CancellationToken _) => d);

        await Service(store).CreateDraftAsync(WorkflowId, fromVersion: null, "user:mark", CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(string.Empty, created.Definition.Name);
        Assert.Equal(string.Empty, created.Definition.EngagementType);
    }

    [Fact]
    public async Task ValidateAsync_BrandNewDraftDefinition_FailsOnNameAndEngagementType()
    {
        var report = await Validate(Definition(name: string.Empty, engagementType: string.Empty));

        Assert.Equal(ValidationOutcome.Fail, report.Outcome);
        Assert.All(report.Findings, f => Assert.Equal("structure.required-fields", f.RuleId));
        Assert.Equal(["name", "engagement_type"], report.Findings.Select(f => f.FieldPath));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Scoping", "")]
    [InlineData("Scoping", "  ")]
    public async Task ProposePublishAsync_ReportForBlankRequiredField_IsRefusedAndNotPersisted(string name, string engagementType)
    {
        var definition = Definition(name, engagementType);
        var report = await Validate(definition);
        var store = new Mock<IDefinitionStore>();
        store.Setup(s => s.GetDraftAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Draft(definition));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(store).ProposePublishAsync(WorkflowId, DraftRevision, report, "user:mark", CancellationToken.None));

        Assert.Contains("failing validation", ex.Message, StringComparison.Ordinal);
        store.Verify(s => s.PersistValidationReportAsync(It.IsAny<ValidationReportDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DefinitionLifecycleService Service(Mock<IDefinitionStore> store) =>
        new(store.Object, Mock.Of<IDefinitionCompiler>(), NoRegistryContracts.Embedder);

    private static Task<ValidationReport> Validate(WorkflowDefinition definition) =>
        new DefinitionValidator([new StructureRequiredFieldsRule()])
            .ValidateAsync(definition, DraftRevision, storedSchemaVersion: null, CancellationToken.None);

    private static WorkflowDefinition Definition(string name, string engagementType) => new()
    {
        WorkflowId = WorkflowId,
        DefinitionVersion = 0,
        EngagementType = engagementType,
        Name = name,
        Nodes = [],
        Edges = [],
        DefinitionHash = string.Empty,
        Mode = ExecutionMode.OneShot
    };

    private static DefinitionDraftDocument Draft(WorkflowDefinition definition) => new()
    {
        Id = $"{WorkflowId}:draft",
        WorkflowId = WorkflowId,
        State = "draft",
        BaseVersion = 0,
        DraftRevision = DraftRevision,
        Definition = definition,
        LastEditedBy = "user:sarah",
        LastEditedUtc = DateTime.UtcNow
    };
}
