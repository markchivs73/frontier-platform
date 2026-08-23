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
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var ctx = new DefinitionValidationContext(draft, draftRevision);
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
        var json = System.Text.Json.JsonSerializer.Serialize(definition, CanonicalProfile.Options);
        var bytes = Encoding.UTF8.GetBytes(json);

        // CA1308 is suppressed rather than satisfied, and the distinction matters: this hash is
        // wire-visible. It is stored on every published definition, pinned by running executions
        // and used as a cache key, so switching to upper-case hex would silently invalidate every
        // stored hash and every pin. The rule's concern is locale-sensitive normalisation of user
        // text; this is hex from a digest, where the case carries no meaning beyond the format
        // already committed to.
#pragma warning disable CA1308 // Normalize strings to uppercase
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
#pragma warning restore CA1308
    }
}
