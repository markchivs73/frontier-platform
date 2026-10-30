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

    /// <summary>
    /// S13.77: the stored schema version reaches the rule through ValidateAsync, the path the
    /// Validate button and publish proposals take. Before, nothing joined the probed version to a
    /// validation run, so schema.version-supported read null and never fired.
    /// </summary>
    [Theory]
    [InlineData("1.0", ValidationSeverity.Info, ValidationOutcome.Pass)]
    [InlineData("99.0", ValidationSeverity.Error, ValidationOutcome.Fail)]
    public async Task ValidateAsync_StoredSchemaVersion_ReachesTheSchemaRule(string stored, ValidationSeverity severity, ValidationOutcome outcome)
    {
        var validator = new DefinitionValidator([new SchemaVersionSupportedRule()]);

        var result = await validator.ValidateAsync(CreateMinimalDefinition("wf-1"), "rev-1", stored, CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(("schema.version-supported", severity), (finding.RuleId, finding.Severity));
        Assert.Contains($"schema {stored}", finding.Message, StringComparison.Ordinal);
        Assert.Equal(outcome, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.0")]
    public async Task ValidateAsync_NoStoredVersionOrTheCurrentOne_SaysNothingAboutSchema(string? stored)
    {
        var validator = new DefinitionValidator([new SchemaVersionSupportedRule()]);

        var result = await validator.ValidateAsync(CreateMinimalDefinition("wf-1"), "rev-1", stored, CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ValidateAsync_NoRules_ReturnsPass()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var draftRevision = "rev-1";

        var result = await _validator.ValidateAsync(definition, draftRevision, storedSchemaVersion: null, CancellationToken.None);

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
            () => _validator.ValidateAsync(null!, draftRevision, storedSchemaVersion: null, CancellationToken.None));

        Assert.Equal("draft", ex.ParamName);
    }

    [Fact]
    public async Task ValidateAsync_WithErrorRule_ReturnsFail()
    {
        var rule = new ErrorReturningRule();
        var validator = new DefinitionValidator(new[] { rule });
        var definition = CreateMinimalDefinition("wf-1");

        var result = await validator.ValidateAsync(definition, "rev-1", storedSchemaVersion: null, CancellationToken.None);

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

        var result = await validator.ValidateAsync(definition, "rev-1", storedSchemaVersion: null, CancellationToken.None);

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

        var result = await validator.ValidateAsync(definition, "rev-1", storedSchemaVersion: null, CancellationToken.None);

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

    /// <summary>
    /// S13.138: the hash covers the definition with its own <c>definition_hash</c> blank, so a
    /// definition stamped with its hash at publish hashes back to the same value. A draft's field
    /// is empty, so the value is exactly the one recorded before the change: every stored hash
    /// stays valid.
    /// </summary>
    [Fact]
    public void ComputeDefinitionHash_IsJcsOverTheProfileBytes_AndIgnoresItsOwnField()
    {
        // ADR-E27 D15 moved the formula from the profile bytes to their RFC 8785 form.
        var draft = CreateMinimalDefinition("wf-1") with { DefinitionHash = string.Empty };
        var expected = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Frontier.Platform.Serialization.JsonCanonicalizer.Canonicalize(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(draft, Frontier.Platform.Serialization.CanonicalProfile.Options))));

        var hash = _validator.ComputeDefinitionHash(draft);
        var stamped = draft with { DefinitionHash = hash };

        Assert.Equal(expected, hash);
        Assert.Equal(hash, _validator.ComputeDefinitionHash(stamped));
        Assert.Equal(hash, _validator.ComputeDefinitionHash(draft with { DefinitionHash = "seed-placeholder-hash" }));
    }

    [Fact]
    public void ComputeDefinitionHash_DoesNotDependOnTheKeyOrderOfAnEmbeddedSchema()
    {
        var definition = CreateMinimalDefinition("wf-1");

        var one = _validator.ComputeDefinitionHash(definition with { Contracts = [Snapshot("""{"type":"object","description":"d"}""")] });
        var other = _validator.ComputeDefinitionHash(definition with { Contracts = [Snapshot("""{"description":"d","type":"object"}""")] });

        Assert.Equal(one, other);
        Assert.NotEqual(_validator.ComputeDefinitionHash(definition), one);
    }

    private static Frontier.Platform.Abstractions.ContractSnapshot Snapshot(string schema) => new()
    {
        ContractRef = "acme/thing/1.0",
        Schema = System.Text.Json.JsonDocument.Parse(schema).RootElement.Clone(),
        Rules = [],
        Hash = "sha256:test",
    };

    [Fact]
    public void ComputeDefinitionHash_RefusesANullDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.ComputeDefinitionHash(null!));
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
