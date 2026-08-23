using System.Diagnostics.CodeAnalysis;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Severity level for a validation finding: blocks publish (Error), advises caution (Warning), or informational only (Info).
/// Defaults are baked into rule definitions; deployments can override via config (doc 13 §4.2).
/// </summary>
public enum ValidationSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Outcome of a validation run: Pass (no findings), PassWithWarnings (findings present but not errors), or Fail (errors present).
/// </summary>
public enum ValidationOutcome
{
    Pass,
    PassWithWarnings,
    Fail
}

/// <summary>
/// Tier of validation: Pure (no I/O, runs in-circuit per edit), Resourced (reads registries/stores, on-demand),
/// or Runtime (executes—sandbox test-run, advisory only).
/// </summary>
public enum RuleTier
{
    Pure,
    Resourced,
    Runtime
}

/// <summary>
/// Context passed to validation rules: the definition being validated, registries they may need.
/// </summary>
public sealed record DefinitionValidationContext(
    WorkflowDefinition Definition,
    string? DraftRevision = null,
    IReadOnlyDictionary<string, string>? ResourceVersions = null
);

/// <summary>
/// A single validation finding: rule id, severity, human-readable message, and anchors for canvas highlighting.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record ValidationFinding(
    string RuleId,
    ValidationSeverity Severity,
    string Message,
    string? NodeId = null,
    string? EdgeRef = null,
    string? FieldPath = null,
    string SourceLibrary = "compiler"
);

/// <summary>
/// Result of a complete validation run: outcome, findings, and resource versions consulted (for drift detection at approval).
/// Doc 13 §3: approval re-checks that resourced inputs haven't drifted since validation.
/// </summary>
public sealed record ValidationReport(
    string WorkflowId,
    string DraftRevision,
    DateTime ValidatedAtUtc,
    ValidationOutcome Outcome,
    IReadOnlyList<ValidationFinding> Findings,
    IReadOnlyDictionary<string, string> ResourceVersions
)
{
    /// <summary>Computed: Outcome == Pass or Outcome == PassWithWarnings (publishable).</summary>
    public bool IsPublishable => Outcome != ValidationOutcome.Fail;
}
