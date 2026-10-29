using Celly.Ast;
using Celly.Checking;
using Celly.Types;

namespace Frontier.Platform.Contracts;

/// <summary>
/// Answers Celly's cost estimator with the sizes the schema allows (ADR-E27 D2; Mark, 2026-10-29:
/// the static ceiling sees collection sizes). Celly names a path as a select chain from a variable:
/// <c>self.items</c>, or <c>i.tags</c> inside a loop over <c>self.items</c>. Loop variables are
/// mapped to the element schema of the range they iterate by reading the rule's syntax tree. A
/// variable bound to two different ranges is left unmapped, so its sizes are unbounded. The list a
/// <c>map()</c> or <c>filter()</c> builds (Celly asks for it by its accumulator, <c>@result</c>)
/// is never longer than the range it iterates, so its size is that range's; where loops share an
/// accumulator name, the largest of their ranges.
/// </summary>
internal sealed class RuleCostHints : ICostEstimator
{
    private const string Self = "self";

    private readonly CelSchemaTypes _types;
    private readonly Dictionary<string, string?> _loopVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong?> _accumulators = new(StringComparer.Ordinal);

    /// <summary>Hints for one rule, whose checked syntax tree is <paramref name="expression"/>.</summary>
    internal RuleCostHints(CelSchemaTypes types, Expr expression)
    {
        _types = types;
        foreach (var loop in AstTools.DescendantsAndSelf(expression).OfType<ComprehensionExpr>())
        {
            var range = PointerOf(loop.IterRange);
            Bind(loop.IterVar, range is null ? null : _types.ElementPointer(range));
            BindAccumulator(loop.AccuVar, range is null ? null : _types.MaxSize(range));
        }
    }

    /// <inheritdoc />
    public SizeEstimate? EstimateSize(string? path, CelType type)
    {
        var max = path is not null && _accumulators.TryGetValue(path, out var accumulated)
            ? accumulated
            : PointerOfPath(path) is { } pointer ? _types.MaxSize(pointer) : null;
        return max is { } bound ? new SizeEstimate(0, bound) : null;
    }

    /// <summary>The schema pointer a dotted Celly path names, or null when it names none.</summary>
    internal string? PointerOfPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var segments = path.Split('.');
        var pointer = Root(segments[0]);
        foreach (var field in segments.Skip(1))
        {
            pointer = pointer is null ? null : _types.FieldPointer(pointer, field);
        }

        return pointer;
    }

    /// <summary>The schema pointer of a select chain, or null when it is not one.</summary>
    internal string? PointerOf(Expr expression) => expression switch
    {
        IdentExpr ident => Root(ident.Name),
        SelectExpr { TestOnly: false } select => PointerOf(select.Operand) is { } operand ? _types.FieldPointer(operand, select.Field) : null,
        _ => null,
    };

    private string? Root(string variable) => variable == Self ? string.Empty : _loopVariables.GetValueOrDefault(variable);

    private void BindAccumulator(string accumulator, ulong? rangeSize) =>
        _accumulators[accumulator] = _accumulators.TryGetValue(accumulator, out var bound)
            ? (bound is { } a && rangeSize is { } b ? Math.Max(a, b) : null)
            : rangeSize;

    private void Bind(string variable, string? element)
    {
        if (_loopVariables.TryGetValue(variable, out var bound) && bound != element)
        {
            element = null;
        }

        _loopVariables[variable] = element;
    }
}
