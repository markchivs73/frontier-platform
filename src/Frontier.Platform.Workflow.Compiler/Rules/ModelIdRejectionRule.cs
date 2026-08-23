using System.Text.RegularExpressions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// model-role.no-model-ids (doc 13 §4.2 R2, doc 08 §2): no string matching a model-ID pattern
/// anywhere in the definition — total indirection; agents resolve models via role→model mappings
/// so models can be updated/rolled back without recompiling definitions (ADR-M1). "Anywhere"
/// means the canonical wire bytes, not selected properties: the definition is serialized through
/// the canonical profile and scanned, so a model id hidden in a prompt template or instructions
/// ref is caught the same as one in a role field. Body implemented at S9.30 (registered hollow
/// since S8.2 — see docs/state/SPEC-TRACEABILITY.md).
/// </summary>
public sealed class ModelIdRejectionRule : PureTierRule
{
    /// <summary>Well-known provider model-id prefixes followed by a version-ish tail (e.g. <c>claude-opus-4-8</c>, <c>gpt-4o</c>).</summary>
    private static readonly Regex ModelIdPattern = new(
        @"\b(?:claude|gpt|gemini|llama|mistral|deepseek)-[a-z0-9][a-z0-9.\-]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public override string RuleId => "model-role.no-model-ids";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var canonicalJson = System.Text.Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(ctx.Definition));

        return ModelIdPattern.Matches(canonicalJson)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new ValidationFinding(RuleId, DefaultSeverity,
                $"definition contains the model id '{id}' — models resolve via role→model mappings, never inline (doc 08 §2)."))
            .ToList();
    }
}
