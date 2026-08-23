using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// determinism.predicates-compile (doc 13 §4.2 R4; upgraded S13.7j per ADR-5 Decision 6):
/// every <see cref="DecisionNode"/> declares a doc 14 §6 branch tree that resolves
/// structurally — branch targets are Control-edge successors, field paths resolve against
/// the producing section's contract type (wire names), the producing section is an
/// ancestor of the decision (its payload provably exists at evaluation time), operators
/// suit the field's type, and values coerce to it. Loop bounds stay positive definition
/// constants. The deprecated string predicate is never evaluated, so a branch-less
/// decision is an error, not a legacy pass.
/// </summary>
public sealed class DeterminismPredicatesCompileRule : IDefinitionValidationRule
{
    private readonly IContractTypeCatalog _contracts;

    public DeterminismPredicatesCompileRule(IContractTypeCatalog contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _contracts = contracts;
    }

    public string RuleId => "determinism.predicates-compile";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var decisionFindings = ctx.Definition.Nodes.OfType<DecisionNode>()
            .SelectMany(decision => DecisionFindings(decision, ctx.Definition));

        var loopFindings = ctx.Definition.Nodes.OfType<LoopNode>()
            .Where(l => l.MaxIterations < 1)
            .Select(l => new ValidationFinding(RuleId, DefaultSeverity,
                "max_iterations must be a positive definition constant.", l.NodeId, FieldPath: "max_iterations"));

        return Task.FromResult<IReadOnlyList<ValidationFinding>>(decisionFindings.Concat(loopFindings).ToList());
    }

    internal IEnumerable<ValidationFinding> DecisionFindings(DecisionNode decision, WorkflowDefinition definition)
    {
        if (decision.Branches is not { Count: > 0 })
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                "decision node declares no branches; the string predicate is deprecated and never evaluated — author the doc 14 §6 branch tree.",
                decision.NodeId, FieldPath: "branches");
            yield break;
        }

        var controlSuccessors = definition.Edges
            .Where(e => e.Kind == EdgeKind.Control && string.Equals(e.FromNodeId, decision.NodeId, StringComparison.Ordinal))
            .Select(e => e.ToNodeId)
            .ToHashSet(StringComparer.Ordinal);
        var ancestorArtifacts = AncestorArtifactContracts(decision.NodeId, definition);

        foreach (var (branch, index) in decision.Branches.Select((branch, index) => (branch, index)))
        {
            if (!controlSuccessors.Contains(branch.TargetNodeId))
            {
                yield return new ValidationFinding(RuleId, DefaultSeverity,
                    $"branch {index} targets '{branch.TargetNodeId}', which is not a Control-edge successor of this decision.",
                    decision.NodeId, FieldPath: $"branches[{index}].target_node_id");
            }

            foreach (var finding in PredicateFindings(branch.Condition, ancestorArtifacts, decision.NodeId, $"branches[{index}].condition"))
            {
                yield return finding;
            }
        }
    }

    /// <summary>Artifact key → output contract CLR type, for every section produced by an ancestor of <paramref name="decisionNodeId"/> (the only payloads that provably exist when the decision evaluates).</summary>
    internal Dictionary<string, Type> AncestorArtifactContracts(string decisionNodeId, WorkflowDefinition definition)
    {
        var predecessors = definition.Edges
            .GroupBy(e => e.ToNodeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.FromNodeId).ToList(), StringComparer.Ordinal);

        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>([decisionNodeId]);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var predecessor in predecessors.GetValueOrDefault(current, []).Where(ancestors.Add))
            {
                frontier.Enqueue(predecessor);
            }
        }

        var contracts = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes.OfType<AgentTaskNode>())
        {
            if (node.ArtifactKey is { } sectionKey && ancestors.Contains(node.NodeId) && _contracts.Resolve(node.OutputContractType) is { } type)
            {
                contracts[sectionKey] = type;
            }
        }

        return contracts;
    }

    internal IEnumerable<ValidationFinding> PredicateFindings(ConditionalPredicate predicate, Dictionary<string, Type> ancestorArtifacts, string nodeId, string fieldPath)
    {
        if (predicate is LogicalPredicate logical)
        {
            foreach (var finding in LogicalFindings(logical, ancestorArtifacts, nodeId, fieldPath))
            {
                yield return finding;
            }

            yield break;
        }

        if (predicate is FieldComparisonPredicate field)
        {
            foreach (var finding in FieldFindings(field, ancestorArtifacts, nodeId, fieldPath))
            {
                yield return finding;
            }
        }
    }

    internal IEnumerable<ValidationFinding> LogicalFindings(LogicalPredicate logical, Dictionary<string, Type> ancestorArtifacts, string nodeId, string fieldPath)
    {
        if (logical.Op == LogicalOp.Not && logical.Operands.Count != 1)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity, "'not' takes exactly one operand.", nodeId, FieldPath: $"{fieldPath}.operands");
        }
        else if (logical.Op != LogicalOp.Not && logical.Operands.Count == 0)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity, $"'{logical.Op.Name}' requires at least one operand.", nodeId, FieldPath: $"{fieldPath}.operands");
        }

        foreach (var (operand, index) in logical.Operands.Select((operand, index) => (operand, index)))
        {
            foreach (var finding in PredicateFindings(operand, ancestorArtifacts, nodeId, $"{fieldPath}.operands[{index}]"))
            {
                yield return finding;
            }
        }
    }

    internal IEnumerable<ValidationFinding> FieldFindings(FieldComparisonPredicate field, Dictionary<string, Type> ancestorArtifacts, string nodeId, string fieldPath)
    {
        var terminalType = ResolveTerminalType(field.FieldPath, ancestorArtifacts, out var pathError);
        if (pathError is not null)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity, pathError, nodeId, FieldPath: $"{fieldPath}.field_path");
            yield break;
        }

        if (field.Operator == ComparisonOp.In)
        {
            if (field.Values is not { Count: > 0 })
            {
                yield return new ValidationFinding(RuleId, DefaultSeverity, "'in' requires a non-empty values list.", nodeId, FieldPath: $"{fieldPath}.values");
            }

            yield break;
        }

        if (field.Value is null)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity, $"'{field.Operator.Name}' requires a value.", nodeId, FieldPath: $"{fieldPath}.value");
            yield break;
        }

        foreach (var finding in OperatorSuitabilityFindings(field, terminalType!, nodeId, fieldPath))
        {
            yield return finding;
        }
    }

    internal IEnumerable<ValidationFinding> OperatorSuitabilityFindings(FieldComparisonPredicate field, Type terminalType, string nodeId, string fieldPath)
    {
        var underlying = Nullable.GetUnderlyingType(terminalType) ?? terminalType;
        var isOrderable = IsNumeric(underlying) || underlying == typeof(DateTime);
        var isText = underlying == typeof(string);

        if ((field.Operator == ComparisonOp.Gt || field.Operator == ComparisonOp.Lt || field.Operator == ComparisonOp.Gte || field.Operator == ComparisonOp.Lte) && !isOrderable)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"'{field.Operator.Name}' needs a numeric or date field; '{field.FieldPath}' is {underlying.Name}.", nodeId, FieldPath: $"{fieldPath}.operator");
            yield break;
        }

        if ((field.Operator == ComparisonOp.Contains || field.Operator == ComparisonOp.StartsWith || field.Operator == ComparisonOp.EndsWith) && !isText)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"'{field.Operator.Name}' needs a string field; '{field.FieldPath}' is {underlying.Name}.", nodeId, FieldPath: $"{fieldPath}.operator");
            yield break;
        }

        if (IsNumeric(underlying) && !decimal.TryParse(field.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"value '{field.Value}' does not coerce to the numeric field '{field.FieldPath}'.", nodeId, FieldPath: $"{fieldPath}.value");
        }
        else if (underlying == typeof(DateTime) && !DateTime.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"value '{field.Value}' does not coerce to the date field '{field.FieldPath}' (ISO-8601 expected).", nodeId, FieldPath: $"{fieldPath}.value");
        }
        else if (underlying == typeof(bool) && field.Value is not ("true" or "false"))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"value '{field.Value}' does not coerce to the boolean field '{field.FieldPath}' ('true'/'false').", nodeId, FieldPath: $"{fieldPath}.value");
        }
    }

    /// <summary>Walks the field path's wire-name segments over the section's contract type; returns the terminal CLR type or an error description.</summary>
    internal static Type? ResolveTerminalType(string fieldPathValue, Dictionary<string, Type> ancestorArtifacts, out string? error)
    {
        var segments = fieldPathValue.Split('.');
        if (segments.Length < 2)
        {
            error = $"field_path '{fieldPathValue}' must be '{{artifact_key}}.{{property}}'.";
            return null;
        }

        if (!ancestorArtifacts.TryGetValue(segments[0], out var current))
        {
            error = $"field_path '{fieldPathValue}' does not start with a section produced by an ancestor of this decision (known: {string.Join(", ", ancestorArtifacts.Keys.Order(StringComparer.Ordinal))}).";
            return null;
        }

        foreach (var segment in segments.Skip(1))
        {
            var property = current.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name, segment, StringComparison.Ordinal));
            if (property is null)
            {
                error = $"'{segment}' is not a wire field of {current.Name} (field_path '{fieldPathValue}').";
                return null;
            }

            current = property.PropertyType;
        }

        error = null;
        return current;
    }

    internal static bool IsNumeric(Type type) =>
        type == typeof(decimal) || type == typeof(double) || type == typeof(float) ||
        type == typeof(int) || type == typeof(long) || type == typeof(short);
}
