using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// schema.version-supported (S13.34): a draft stored below — or above — the schema major this
/// build reads must <em>say so</em>, in one definition-scoped sentence.
///
/// <para>Without it, a stored version with no registered adapter deserializes straight through:
/// unrecognised keys are dropped with no log, no exception and no finding, and the nulled fields
/// surface as ordinary content errors ("rollback target … produces no section (no artifact_key)")
/// that send the designer to a node that is not the problem.</para>
///
/// <para>Severity: <see cref="ValidationSeverity.Info"/> when a registered adapter carried the
/// draft forward — it is readable and correct, the designer only needs to know why it looks old —
/// and <see cref="ValidationSeverity.Error"/> when the stored version cannot be read at all, which
/// blocks publish as an unreadable definition should. Nothing at all when the version is current,
/// or when it was never probed: absence of evidence is not a finding.</para>
/// </summary>
public sealed class SchemaVersionSupportedRule : PureTierRule
{
    /// <inheritdoc />
    public override string RuleId => "schema.version-supported";

    /// <inheritdoc />
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var stored = ctx.StoredSchemaVersion;
        if (stored is null)
        {
            return [];
        }

        var current = DefinitionSchemaCompatibility.CurrentSchemaVersion;

        return DefinitionSchemaCompatibility.Classify(stored) switch
        {
            SchemaCompatibility.Current => [],
            SchemaCompatibility.Adapted =>
            [
                new ValidationFinding(RuleId, ValidationSeverity.Info,
                    $"This draft predates schema {current} — it was stored at schema {stored} and adapted forward for validation.")
            ],
            _ =>
            [
                new ValidationFinding(RuleId, ValidationSeverity.Error,
                    $"This draft was stored at schema {stored}, which this build cannot read — schema {current} is current. Open it in a build that reads schema {stored}, or recreate it here.")
            ],
        };
    }
}
