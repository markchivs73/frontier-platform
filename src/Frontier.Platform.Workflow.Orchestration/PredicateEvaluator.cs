using System.Globalization;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Pure evaluation of doc 14 §6 <see cref="ConditionalPredicate"/> trees (S13.7j, ADR-5
/// Decision 6/ADR-CD4): navigates recorded section outputs by field path and compares
/// with fixed semantics — invariant culture, ordinal strings, decimals when both sides
/// parse as decimals, ISO-8601 dates when both parse as dates. No I/O, no reflection, no
/// clocks — payloads come from <see cref="GraphExecutionState.NodeOutputPayloads"/>
/// (historized activity results), so replay re-evaluates identically (dtf-determinism).
/// An absent field, unresolvable path, or non-scalar terminal evaluates <c>false</c>,
/// never throws — the mandatory default branch is the fall-through.
/// </summary>
internal static class PredicateEvaluator
{
    /// <summary>
    /// Selects <paramref name="decision"/>'s route: the first branch (in declared order)
    /// whose condition evaluates true, else <see cref="DecisionNode.DefaultBranchNodeId"/>.
    /// Throws <see cref="ContractViolationException"/> for a branch-less decision — the
    /// deprecated string predicate is never evaluated, and publish-time validation
    /// (<c>determinism.predicates-compile</c>) makes this unreachable for governed definitions.
    /// </summary>
    internal static string SelectBranch(DecisionNode decision, WorkflowDefinition definition, GraphExecutionState state)
    {
        if (decision.Branches is not { Count: > 0 })
        {
            throw new ContractViolationException(nameof(DecisionNode), [$"Decision '{decision.NodeId}' declares no branches; the string predicate is deprecated and never evaluated (S13.7j)."]);
        }

        var sections = BuildArtifactPayloads(definition, state);
        try
        {
            var selected = decision.Branches.FirstOrDefault(branch => Evaluate(branch.Condition, sections));
            return selected?.TargetNodeId ?? decision.DefaultBranchNodeId;
        }
        finally
        {
            foreach (var document in sections.Values)
            {
                document.Dispose();
            }
        }
    }

    /// <summary>Parses each produced section's recorded output payload, keyed by section key (payload strings are canonical JSON from historized activity results).</summary>
    internal static IReadOnlyDictionary<string, JsonDocument> BuildArtifactPayloads(WorkflowDefinition definition, GraphExecutionState state)
    {
        var payloads = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (node.ArtifactKey is { } sectionKey && state.NodeOutputPayloads.TryGetValue(node.NodeId, out var payload))
            {
                payloads[sectionKey] = JsonDocument.Parse(payload);
            }
        }

        return payloads;
    }

    /// <summary>Evaluates <paramref name="predicate"/> against the produced sections.</summary>
    internal static bool Evaluate(ConditionalPredicate predicate, IReadOnlyDictionary<string, JsonDocument> sections) => predicate switch
    {
        FieldComparisonPredicate field => EvaluateField(field, sections),
        LogicalPredicate logical => EvaluateLogical(logical, sections),
        _ => false,
    };

    /// <summary>And: all operands true; Or: any operand true; Not: single operand false. A malformed arity evaluates false (validation rejects it at publish).</summary>
    internal static bool EvaluateLogical(LogicalPredicate logical, IReadOnlyDictionary<string, JsonDocument> sections)
    {
        if (logical.Op == LogicalOp.And)
        {
            return logical.Operands.Count > 0 && logical.Operands.All(operand => Evaluate(operand, sections));
        }

        if (logical.Op == LogicalOp.Or)
        {
            return logical.Operands.Any(operand => Evaluate(operand, sections));
        }

        return logical.Operands.Count == 1 && !Evaluate(logical.Operands[0], sections);
    }

    /// <summary>Resolves the field and applies the operator; absent/non-scalar fields evaluate false.</summary>
    internal static bool EvaluateField(FieldComparisonPredicate field, IReadOnlyDictionary<string, JsonDocument> sections)
    {
        var fieldValue = ResolveField(field.FieldPath, sections);
        if (fieldValue is null)
        {
            return false;
        }

        if (field.Operator == ComparisonOp.In)
        {
            return field.Values is { Count: > 0 } && field.Values.Contains(fieldValue, StringComparer.Ordinal);
        }

        if (field.Value is null)
        {
            return false;
        }

        if (field.Operator == ComparisonOp.Contains)
        {
            return fieldValue.Contains(field.Value, StringComparison.Ordinal);
        }

        if (field.Operator == ComparisonOp.StartsWith)
        {
            return fieldValue.StartsWith(field.Value, StringComparison.Ordinal);
        }

        if (field.Operator == ComparisonOp.EndsWith)
        {
            return fieldValue.EndsWith(field.Value, StringComparison.Ordinal);
        }

        return CompareScalars(fieldValue, field.Operator, field.Value);
    }

    /// <summary>
    /// Walks <paramref name="fieldPath"/> (<c>{artifact_key}.{wire.property.path}</c>)
    /// over the produced sections; returns the terminal scalar's canonical string form,
    /// or null when any segment is absent or the terminal is an object/array.
    /// </summary>
    internal static string? ResolveField(string fieldPath, IReadOnlyDictionary<string, JsonDocument> sections)
    {
        var segments = fieldPath.Split('.');
        if (segments.Length < 2 || !sections.TryGetValue(segments[0], out var document))
        {
            return null;
        }

        var element = document.RootElement;
        foreach (var segment in segments.Skip(1))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    /// <summary>
    /// Ordering/equality with fixed coercion (doc 14 §6): decimals when both sides parse
    /// invariantly (canonical string decimals included), ISO dates when both parse
    /// round-trip, else ordinal strings for Eq/Neq — ordering operators on
    /// non-comparable values evaluate false.
    /// </summary>
    internal static bool CompareScalars(string fieldValue, ComparisonOp op, string comparand)
    {
        int? comparison = null;
        if (decimal.TryParse(fieldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftDecimal) &&
            decimal.TryParse(comparand, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightDecimal))
        {
            comparison = leftDecimal.CompareTo(rightDecimal);
        }
        else if (DateTime.TryParse(fieldValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var leftDate) &&
                 DateTime.TryParse(comparand, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var rightDate))
        {
            comparison = leftDate.CompareTo(rightDate);
        }

        if (comparison is null)
        {
            if (op == ComparisonOp.Eq)
            {
                return string.Equals(fieldValue, comparand, StringComparison.Ordinal);
            }

            return op == ComparisonOp.Neq && !string.Equals(fieldValue, comparand, StringComparison.Ordinal);
        }

        if (op == ComparisonOp.Gt)
        {
            return comparison > 0;
        }

        if (op == ComparisonOp.Lt)
        {
            return comparison < 0;
        }

        if (op == ComparisonOp.Gte)
        {
            return comparison >= 0;
        }

        if (op == ComparisonOp.Lte)
        {
            return comparison <= 0;
        }

        // Only Eq/Neq reach this tail — ordering operators returned above, and the
        // string-only operators returned in EvaluateField before coercion.
        return op == ComparisonOp.Eq ? comparison == 0 : comparison != 0;
    }
}
