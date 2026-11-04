using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.209 (doc 14 §3): the one-shot entry agent_task must take the entry contract the design
/// agent is told about, and request the dynamic field that carries it.
/// </summary>
public sealed class StructureEntryInputContractRuleTests
{
    private static readonly StructureEntryInputContractRule Rule = new(new ChatDesignerServiceTests.FakeEntryContractCatalog());

    [Fact]
    public void DeclaresItsCatalogueIdentity()
    {
        Assert.Equal("structure.entry-input-contract", Rule.RuleId);
        Assert.Equal(RuleTier.Resourced, Rule.Tier);
        Assert.Equal(ValidationSeverity.Error, Rule.DefaultSeverity);
    }

    [Fact]
    public async Task EntryTakingTheEntryContract_ReturnsEmpty()
    {
        var definition = S930Fixtures.Build([CompliantEntry("a"), S930Fixtures.Agent("b")], [S930Fixtures.Control("a", "b")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task EntryWithAnotherInputContract_ReturnsFindingOnInputContractType()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a", inputContract: "BriefArtifact", dynamicFields: ["case_summary"])]);

        var finding = Assert.Single(await EvaluateAsync(definition));
        Assert.Equal("structure.entry-input-contract", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("a", finding.NodeId);
        Assert.Equal("input_contract_type", finding.FieldPath);
        Assert.Contains("'CaseSummary'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntryWithoutTheEntryDynamicField_ReturnsFindingOnDynamicFields()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a", inputContract: "CaseSummary", dynamicFields: ["other_field"])]);

        var finding = Assert.Single(await EvaluateAsync(definition));
        Assert.Equal("context_request.dynamic_fields", finding.FieldPath);
        Assert.Contains("'case_summary'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntryMissingBoth_ReturnsTwoFindings()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a")]);

        var findings = await EvaluateAsync(definition);

        Assert.Equal(["input_contract_type", "context_request.dynamic_fields"], findings.Select(f => f.FieldPath));
    }

    [Fact]
    public async Task EntryWhoseContextRequestDidNotDeserialize_ReturnsDynamicFieldsFinding()
    {
        var entry = CompliantEntry("a") with { ContextRequest = null! };

        var finding = Assert.Single(await EvaluateAsync(S930Fixtures.Build([entry])));
        Assert.Equal("context_request.dynamic_fields", finding.FieldPath);
    }

    [Fact]
    public async Task SeveralEntries_EachGetsItsOwnFinding()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", dynamicFields: ["case_summary"]), CompliantEntry("b"), S930Fixtures.Agent("c", dynamicFields: ["case_summary"])]);

        var findings = await EvaluateAsync(definition);

        Assert.Equal(["a", "c"], findings.Select(f => f.NodeId));
        Assert.All(findings, f => Assert.Equal("input_contract_type", f.FieldPath));
    }

    [Fact]
    public async Task ChecksAgainstWhateverTheCatalogueAnswers()
    {
        var rule = new StructureEntryInputContractRule(new IntakeCatalog());
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a", inputContract: "IntakeForm", dynamicFields: ["intake_form"])]);

        Assert.Empty(await rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
        Assert.Equal(2, (await EvaluateAsync(definition)).Count);
    }

    [Fact]
    public async Task NonEntryAgentWithAnotherContract_IsNotChecked()
    {
        var definition = S930Fixtures.Build(
            [CompliantEntry("a"), S930Fixtures.Agent("b", inputContract: "SummaryArtifact")],
            [S930Fixtures.Control("a", "b")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task EntryThatIsNotAnAgentTask_IsNotChecked()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Gate("g"), S930Fixtures.Agent("b")], [S930Fixtures.Control("g", "b")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task DispatcherDefinition_IsLeftToDispatcherInput()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a")], mode: ExecutionMode.Dispatcher);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public void NullCatalog_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new StructureEntryInputContractRule(null!));

    [Fact]
    public async Task NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Rule.EvaluateAsync(null!, CancellationToken.None));

    private sealed class IntakeCatalog : IEntryContractCatalog
    {
        public Task<EntryContractDescriptor> GetEntryContractAsync(CancellationToken ct) =>
            Task.FromResult(new EntryContractDescriptor { ContractTypeName = "IntakeForm", DynamicFieldName = "intake_form", Description = "the intake form" });
    }

    private static AgentTaskNode CompliantEntry(string id) =>
        S930Fixtures.Agent(id, inputContract: "CaseSummary", dynamicFields: ["other_field", "case_summary"]);

    private static Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(WorkflowDefinition definition) =>
        Rule.EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);
}
