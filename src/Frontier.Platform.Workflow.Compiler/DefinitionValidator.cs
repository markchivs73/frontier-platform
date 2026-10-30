using Frontier.Platform.Serialization;

using System.Security.Cryptography;
using System.Text;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Validator service: validates definitions (pure + resourced tiers), canonicalises, and computes hashes.
/// Doc 13 §6: IDefinitionCompiler implementation. Rules are discovered from DI; the validator aggregates them.
/// </summary>
public sealed class DefinitionValidator : IDefinitionCompiler
{
    private readonly IEnumerable<IDefinitionValidationRule> _rules;

    public DefinitionValidator(IEnumerable<IDefinitionValidationRule> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public async Task<ValidationReport> ValidateAsync(
        WorkflowDefinition draft,
        string draftRevision,
        string? storedSchemaVersion,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var ctx = new DefinitionValidationContext(draft, draftRevision, StoredSchemaVersion: storedSchemaVersion);
        var findings = new List<ValidationFinding>();

        foreach (var rule in _rules.Where(r => r.Tier == RuleTier.Pure || r.Tier == RuleTier.Resourced))
        {
            var ruleFinding = await rule.EvaluateAsync(ctx, ct).ConfigureAwait(false);
            findings.AddRange(ruleFinding);
        }

        var hasErrors = findings.Any(f => f.Severity == ValidationSeverity.Error);
        var outcome = hasErrors ? ValidationOutcome.Fail :
                      findings.Any(f => f.Severity == ValidationSeverity.Warning) ? ValidationOutcome.PassWithWarnings :
                      ValidationOutcome.Pass;

        return new ValidationReport(
            WorkflowId: draft.WorkflowId,
            DraftRevision: draftRevision,
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: outcome,
            Findings: findings.AsReadOnly(),
            ResourceVersions: new Dictionary<string, string>().AsReadOnly());
    }

    public IReadOnlyList<ValidationFinding> ValidateStructural(WorkflowDefinition draft)
    {
        var ctx = new DefinitionValidationContext(draft);
        var findings = new List<ValidationFinding>();

        foreach (var rule in _rules.Where(r => r.Tier == RuleTier.Pure))
        {
            var ruleFinding = rule.EvaluateAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            findings.AddRange(ruleFinding);
        }

        return findings.AsReadOnly();
    }

    public string ComputeDefinitionHash(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // The hash covers the definition with its own definition_hash field blank, as the field's
        // contract says, so a definition stamped with its hash at publish hashes back to the same
        // value. The canonical profile writes the typed shell; RFC 8785 then fixes the byte order of
        // what it cannot, the embedded contract schemas (ADR-E27 D15). Nothing recomputes a stored
        // hash, so versions published before this keep the hash they were recorded with.
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(definition with { DefinitionHash = string.Empty }, CanonicalProfile.Options);
        var bytes = JsonCanonicalizer.Canonicalize(json);

        // Lower-case hex is wire-visible: the hash is stored on every published definition, pinned
        // by running executions and used as a cache key, so its case never changes.
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
