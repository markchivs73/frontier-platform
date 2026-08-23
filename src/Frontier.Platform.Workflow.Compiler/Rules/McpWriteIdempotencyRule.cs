
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// mcp.write-idempotency (doc 13 §4.2 R2, master design §2.1): MCP writes must declare an
/// idempotency key spec. Phase 1 has no trusted read/write distinction on the node or in the
/// connector catalogue (MCP tool annotations such as <c>readOnlyHint</c> are unsurfaced,
/// untrusted hints), so the rule requires the spec on every <see cref="McpToolNode"/> — the
/// fail-safe over-approximation: a read carrying a spec is harmless, a write without one is not.
/// Annotation-based read exemption is the recorded refinement (DESIGN-DECISIONS.md S9.30).
/// </summary>
public sealed class McpWriteIdempotencyRule : PureTierRule
{
    public override string RuleId => "mcp.write-idempotency";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes.OfType<McpToolNode>()
            .Where(node => string.IsNullOrWhiteSpace(node.IdempotencyKeySpec))
            .Select(node => new ValidationFinding(RuleId, DefaultSeverity,
                "mcp_tool node declares no idempotency_key_spec.",
                node.NodeId, FieldPath: "idempotency_key_spec"))
            .ToList();
}
