using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

public sealed class DefinitionValidatorTests
{
    private static readonly IReadOnlyList<string> EmptyNodes = Array.Empty<string>();

    private readonly DefinitionValidator _validator;

    public DefinitionValidatorTests()
    {
        _validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
    }

    // rules ?? throw — every other test supplies a non-null rules collection; this exercises the
    // constructor's null-guard fallback (S9.24 branch-coverage gap).
    [Fact]
    public void Constructor_NullRules_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new DefinitionValidator(null!));

    [Fact]
    public async Task ValidateAsync_NoRules_ReturnsPass()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var draftRevision = "rev-1";

        var result = await _validator.ValidateAsync(definition, draftRevision, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("wf-1", result.WorkflowId);
        Assert.Equal("rev-1", result.DraftRevision);
        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
        Assert.Empty(result.Findings);
        Assert.NotNull(result.ResourceVersions);
    }

    [Fact]
    public async Task ValidateAsync_NullDefinition_Throws()
    {
        var draftRevision = "rev-1";

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _validator.ValidateAsync(null!, draftRevision, CancellationToken.None));

        Assert.Equal("draft", ex.ParamName);
    }

    [Fact]
    public async Task ValidateAsync_WithErrorRule_ReturnsFail()
    {
        var rule = new ErrorReturningRule();
        var validator = new DefinitionValidator(new[] { rule });
        var definition = CreateMinimalDefinition("wf-1");

        var result = await validator.ValidateAsync(definition, "rev-1", CancellationToken.None);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.Single(result.Findings);
        Assert.Equal(ValidationSeverity.Error, result.Findings[0].Severity);
    }

    [Fact]
    public async Task ValidateAsync_WithWarningRule_ReturnsPassWithWarnings()
    {
        var rule = new WarningReturningRule();
        var validator = new DefinitionValidator(new[] { rule });
        var definition = CreateMinimalDefinition("wf-1");

        var result = await validator.ValidateAsync(definition, "rev-1", CancellationToken.None);

        Assert.Equal(ValidationOutcome.PassWithWarnings, result.Outcome);
        Assert.Single(result.Findings);
        Assert.Equal(ValidationSeverity.Warning, result.Findings[0].Severity);
    }

    [Fact]
    public async Task ValidateAsync_WithBothPureAndResourced_IncludesBoth()
    {
        IDefinitionValidationRule pureRule = new ErrorReturningRule();
        IDefinitionValidationRule resourcedRule = new ResourcedTierRule();
        var validator = new DefinitionValidator(new[] { pureRule, resourcedRule });
        var definition = CreateMinimalDefinition("wf-1");

        var result = await validator.ValidateAsync(definition, "rev-1", CancellationToken.None);

        Assert.Equal(2, result.Findings.Count); // Both pure and resourced rules
        Assert.Contains(result.Findings, f => f.RuleId == "pure-tier-error");
        Assert.Contains(result.Findings, f => f.RuleId == "resourced-tier-error");
    }

    [Fact]
    public void ValidateStructural_NoRules_ReturnsEmpty()
    {
        var definition = CreateMinimalDefinition("wf-1");

        var findings = _validator.ValidateStructural(definition);

        Assert.Empty(findings);
    }

    [Fact]
    public void ValidateStructural_WithPureRule_ReturnsFinding()
    {
        IDefinitionValidationRule rule = new ErrorReturningRule();
        var validator = new DefinitionValidator(new[] { rule });
        var definition = CreateMinimalDefinition("wf-1");

        var findings = validator.ValidateStructural(definition);

        Assert.Single(findings);
        Assert.Equal("pure-tier-error", findings[0].RuleId);
    }

    [Fact]
    public void ValidateStructural_IgnoresResourcedRule()
    {
        IDefinitionValidationRule pureRule = new ErrorReturningRule();
        IDefinitionValidationRule resourcedRule = new ResourcedTierRule();
        var validator = new DefinitionValidator(new[] { pureRule, resourcedRule });
        var definition = CreateMinimalDefinition("wf-1");

        var findings = validator.ValidateStructural(definition);

        Assert.Single(findings); // Only pure rule
    }

    // S9.81 regression: a from-scratch draft (version 0, the unversioned sentinel) must clear the
    // structural gate. This is the exact path TestRunService.StartAsync uses before a test-run and
    // the S9.73 agent-repair loop runs on a proposal — previously VersioningNoClashRule rejected it
    // with an unresolvable "definition_version must be at least 1" error.
    [Fact]
    public void ValidateStructural_FromScratchDraftAtVersionZero_NoVersioningFinding()
    {
        var validator = new DefinitionValidator(new IDefinitionValidationRule[] { new VersioningNoClashRule() });
        var draft = CreateMinimalDefinition("wf-1") with { DefinitionVersion = 0 };

        var findings = validator.ValidateStructural(draft);

        Assert.Empty(findings);
    }

    [Fact]
    public void ComputeDefinitionHash_SameDefinition_SameHash()
    {
        var definition = CreateMinimalDefinition("wf-1");

        var hash1 = _validator.ComputeDefinitionHash(definition);
        var hash2 = _validator.ComputeDefinitionHash(definition);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_DifferentDefinitions_DifferentHash()
    {
        var def1 = CreateMinimalDefinition("wf-1");
        var def2 = CreateMinimalDefinition("wf-2");

        var hash1 = _validator.ComputeDefinitionHash(def1);
        var hash2 = _validator.ComputeDefinitionHash(def2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_StartsWithSha256Prefix()
    {
        var definition = CreateMinimalDefinition("wf-1");

        var hash = _validator.ComputeDefinitionHash(definition);

        Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDefinitionHash_ProducesHexString()
    {
        var definition = CreateMinimalDefinition("wf-1");

        var hash = _validator.ComputeDefinitionHash(definition);
        var hexPart = hash.Substring(7); // Remove "sha256:" prefix

        Assert.True(hexPart.All(c => "0123456789abcdef".Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public void ComputeDefinitionHash_DifferentNodeIds_DifferentHash()
    {
        var def1 = CreateMinimalDefinition("wf-1");
        var def2 = new WorkflowDefinition
        {
            WorkflowId = "wf-1",
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = new List<WorkflowNode>
            {
                new AgentTaskNode
                {
                    NodeId = "different-id",
                    Role = "default-role",
                    InstructionsRef = "default-instructions",
                    InputContractType = "DefaultInput",
                    OutputContractType = "DefaultOutput",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "eng-1",
                        AgentRole = "default-role",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            },
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };

        var hash1 = _validator.ComputeDefinitionHash(def1);
        var hash2 = _validator.ComputeDefinitionHash(def2);

        Assert.NotEqual(hash1, hash2);
    }

    private static WorkflowDefinition CreateMinimalDefinition(string workflowId)
    {
        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = new List<WorkflowNode>
            {
                new AgentTaskNode
                {
                    NodeId = "node-1",
                    Role = "default-role",
                    InstructionsRef = "default-instructions",
                    InputContractType = "DefaultInput",
                    OutputContractType = "DefaultOutput",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "eng-1",
                        AgentRole = "default-role",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            },
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }

    private sealed class ErrorReturningRule : IDefinitionValidationRule
    {
        public string RuleId => "pure-tier-error";
        public RuleTier Tier => RuleTier.Pure;
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(
            DefinitionValidationContext context,
            CancellationToken ct)
        {
            var findings = new List<ValidationFinding>
            {
                new ValidationFinding(
                    RuleId: "pure-tier-error",
                    Severity: ValidationSeverity.Error,
                    Message: "Test error")
            };
            return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings.AsReadOnly());
        }
    }

    private sealed class WarningReturningRule : IDefinitionValidationRule
    {
        public string RuleId => "pure-tier-warning";
        public RuleTier Tier => RuleTier.Pure;
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

        public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(
            DefinitionValidationContext context,
            CancellationToken ct)
        {
            var findings = new List<ValidationFinding>
            {
                new ValidationFinding(
                    RuleId: "pure-tier-warning",
                    Severity: ValidationSeverity.Warning,
                    Message: "Test warning")
            };
            return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings.AsReadOnly());
        }
    }

    private sealed class ResourcedTierRule : IDefinitionValidationRule
    {
        public string RuleId => "resourced-tier-error";
        public RuleTier Tier => RuleTier.Resourced;
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(
            DefinitionValidationContext context,
            CancellationToken ct)
        {
            var findings = new List<ValidationFinding>
            {
                new ValidationFinding(
                    RuleId: "resourced-tier-error",
                    Severity: ValidationSeverity.Error,
                    Message: "Resourced tier error")
            };
            return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings.AsReadOnly());
        }
    }
}
