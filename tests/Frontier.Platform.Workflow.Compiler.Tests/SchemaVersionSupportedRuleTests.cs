using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.34 (defect): a draft stored below the current schema major must <em>say so</em>. Today
/// <see cref="MigratingWorkflowDefinitionConverter"/> adapts only versions with a registered
/// adapter and deserializes every other version straight through — unrecognised keys are dropped
/// with no log, no exception and no finding, so the nulled fields surface as ordinary content
/// errors ("rollback target … produces no section (no artifact_key)") and the designer debugs a
/// phantom content problem. This rule is the sentence that was missing.
///
/// <para>Severity is Mark's decision D1: <see cref="ValidationSeverity.Info"/> when the stored
/// version was adapted forward by a registered adapter (the draft is readable and correct, the
/// designer just needs to know why it looks old), <see cref="ValidationSeverity.Error"/> when the
/// stored version cannot be read at all — an Error blocks publish, which an unreadable definition
/// should. Nothing at all when the stored version is current: no noise on the common path.</para>
/// </summary>
public sealed class SchemaVersionSupportedRuleTests
{
    /// <summary>The Info sentence for a draft that was adapted forward by a registered adapter.</summary>
    private const string AdaptedSentence =
        "This draft predates schema 2.0 — it was stored at schema 1.0 and adapted forward for validation.";

    /// <summary>The Error sentence for a stored version no adapter and no current reader can handle.</summary>
    private const string UnsupportedSentence =
        "This draft was stored at schema 9.9, which this build cannot read — schema 2.0 is current.";

    private static readonly SchemaVersionSupportedRule Rule = new();

    private static WorkflowDefinition Definition() => new()
    {
        WorkflowId = "wf-x",
        DefinitionVersion = 1,
        EngagementType = "support-triage",
        Name = "Draft",
        DefinitionHash = "sha256:x",
        Nodes = [],
        Edges = [],
        Mode = ExecutionMode.OneShot,
    };

    private static Task<IReadOnlyList<ValidationFinding>> Evaluate(string? storedSchemaVersion) =>
        Rule.EvaluateAsync(
            new DefinitionValidationContext(Definition(), StoredSchemaVersion: storedSchemaVersion),
            CancellationToken.None);

    [Fact]
    public void Rule_IsPureTier_WithTheDocumentedIdAndBlockingDefaultSeverity()
    {
        // Pure: it reads the context's stored version and nothing else, so it runs in-circuit on
        // every edit alongside the other pure rules (doc 13 §4.2 tier column).
        Assert.Equal("schema.version-supported", Rule.RuleId);
        Assert.Equal(RuleTier.Pure, Rule.Tier);
        Assert.Equal(ValidationSeverity.Error, Rule.DefaultSeverity);
        Assert.IsAssignableFrom<PureTierRule>(Rule);
    }

    [Fact]
    public async Task Current_EmitsNothing()
    {
        // The overwhelmingly common case. A finding here would be permanent noise in the A3-R4
        // panel on every draft in the system.
        Assert.Empty(await Evaluate("2.0"));
    }

    [Fact]
    public async Task AdaptedOnePointZero_ReportsThePredatesSentence()
    {
        var finding = Assert.Single(await Evaluate(ArtifactVocabularyMigration.PreRenameSchemaVersion));

        Assert.Equal("schema.version-supported", finding.RuleId);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Equal(AdaptedSentence, finding.Message);
    }

    [Fact]
    public async Task AdaptedFinding_IsDefinitionScoped_NotPinnedToANode()
    {
        // The problem is the document, not any one node: anchoring it to a node would send the
        // designer to the wrong place — exactly the failure mode this defect is about.
        var finding = Assert.Single(await Evaluate("1.0"));

        Assert.Null(finding.NodeId);
        Assert.Null(finding.EdgeRef);
    }

    [Fact]
    public async Task UnsupportedVersion_ReportsTheSentence()
    {
        var finding = Assert.Single(await Evaluate("9.9"));

        Assert.Equal("schema.version-supported", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal(UnsupportedSentence, finding.Message);
    }

    [Fact]
    public async Task UnknownStoredVersion_EmitsNothing()
    {
        // Null means "the caller did not probe" — an in-memory definition under edit, a proposal
        // the agent just produced, a replayed orchestration input. Absence of evidence is not a
        // finding: the rule must never accuse a draft it has no stored bytes for.
        Assert.Empty(await Evaluate(null));
    }

    [Fact]
    public async Task TheRuleNeverTouchesTheDefinitionItself()
    {
        // Invariant 1: this rule reports, it does not repair. A rule that mutated the definition
        // to "fix" the schema would change its canonical bytes and therefore its hash.
        var definition = Definition();
        var before = definition with { };

        await Rule.EvaluateAsync(
            new DefinitionValidationContext(definition, StoredSchemaVersion: "1.0"), CancellationToken.None);

        Assert.Equal(before, definition);
    }
}
