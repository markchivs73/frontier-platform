using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7j (ADR-5 Decision 6): <c>determinism.predicates-compile</c>'s upgraded structural
/// checks over doc 14 §6 branch trees — targets are Control-edge successors, field paths
/// resolve on ancestor sections' contract wire names, operators suit the field type,
/// values coerce, logical arity holds.
/// </summary>
public sealed class S137jPredicateRuleTests
{
    [Fact]
    public async Task ValidBranchTree_ReturnsEmpty()
    {
        var definition = Definition(Branch("agent-high", Field("scope.title", ComparisonOp.Eq, "HIGH")));

        Assert.Empty(await Evaluate(definition));
    }

    [Fact]
    public async Task BranchTargetNotControlSuccessor_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-elsewhere", Field("scope.title", ComparisonOp.Eq, "HIGH")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].target_node_id", finding.FieldPath);
        Assert.Contains("agent-elsewhere", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FieldPathOnNonAncestorArtifact_ReturnsFinding()
    {
        // "high" is produced downstream of the decision — its payload cannot exist when
        // the decision evaluates.
        var definition = Definition(Branch("agent-high", Field("high.title", ComparisonOp.Eq, "x")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.field_path", finding.FieldPath);
        Assert.Contains("ancestor", finding.Message, StringComparison.Ordinal);
        Assert.Contains("scope", finding.Message, StringComparison.Ordinal); // names the known sections
    }

    [Fact]
    public async Task UnknownWireField_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", Field("scope.nonexistent", ComparisonOp.Eq, "x")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Contains("'nonexistent' is not a wire field of SummaryArtifact", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderingOperatorOnStringField_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", Field("scope.title", ComparisonOp.Gt, "9")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.operator", finding.FieldPath);
        Assert.Contains("needs a numeric or date field", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContainsOnListField_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", Field("scope.objectives", ComparisonOp.Contains, "x")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Contains("needs a string field", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericValueThatDoesNotCoerce_ReturnsFinding()
    {
        var definition = Definition(
            Branch("agent-high", Field("approach.cost_estimate", ComparisonOp.Gt, "lots")),
            approachUpstream: true);

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.value", finding.FieldPath);
        Assert.Contains("does not coerce to the numeric field", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InWithoutValues_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", new FieldComparisonPredicate
        {
            FieldPath = "scope.title",
            Operator = ComparisonOp.In,
        }));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.values", finding.FieldPath);
    }

    [Fact]
    public async Task NotWithTwoOperands_ReturnsFinding()
    {
        var valid = Field("scope.title", ComparisonOp.Eq, "x");
        var definition = Definition(Branch("agent-high", new LogicalPredicate
        {
            Op = LogicalOp.Not,
            Operands = [valid, valid],
        }));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Contains("'not' takes exactly one operand", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedLogicalTree_ValidatesOperandsRecursively()
    {
        var definition = Definition(Branch("agent-high", new LogicalPredicate
        {
            Op = LogicalOp.And,
            Operands =
            [
                Field("scope.title", ComparisonOp.Eq, "x"),
                new LogicalPredicate { Op = LogicalOp.Or, Operands = [Field("scope.bogus", ComparisonOp.Eq, "x")] },
            ],
        }));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.operands[1].operands[0].field_path", finding.FieldPath);
    }

    [Fact]
    public async Task ValidNot_ReturnsEmpty()
    {
        var definition = Definition(Branch("agent-high", new LogicalPredicate
        {
            Op = LogicalOp.Not,
            Operands = [Field("scope.title", ComparisonOp.Eq, "x")],
        }));

        Assert.Empty(await Evaluate(definition));
    }

    [Fact]
    public async Task MissingValueOnNonInOperator_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", new FieldComparisonPredicate
        {
            FieldPath = "scope.title",
            Operator = ComparisonOp.Eq,
        }));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Equal("branches[0].condition.value", finding.FieldPath);
        Assert.Contains("requires a value", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiamondAncestry_ResolvesArtifactsOnce()
    {
        // Two paths from the same ancestor to the decision — ancestry BFS must not loop
        // or duplicate; the section still resolves.
        List<WorkflowNode> nodes =
        [
            S930Fixtures.Agent("agent-scope", outputContract: "SummaryArtifact", sectionKey: "scope"),
            S930Fixtures.Agent("agent-mid-a", outputContract: "PlanArtifact", sectionKey: "mid-a"),
            S930Fixtures.Agent("agent-mid-b", outputContract: "PlanArtifact", sectionKey: "mid-b"),
            new DecisionNode
            {
                NodeId = "decision-1",
                DefaultBranchNodeId = "agent-low",
                Branches = [Branch("agent-high", Field("scope.title", ComparisonOp.Eq, "HIGH"))],
            },
            S930Fixtures.Agent("agent-high", outputContract: "PlanArtifact", sectionKey: "high"),
            S930Fixtures.Agent("agent-low", outputContract: "PlanArtifact", sectionKey: "low"),
        ];
        List<WorkflowEdge> edges =
        [
            S930Fixtures.Control("agent-scope", "agent-mid-a"),
            S930Fixtures.Control("agent-scope", "agent-mid-b"),
            S930Fixtures.Control("agent-mid-a", "decision-1"),
            S930Fixtures.Control("agent-mid-b", "decision-1"),
            S930Fixtures.Control("decision-1", "agent-high"),
            S930Fixtures.Control("decision-1", "agent-low"),
        ];

        Assert.Empty(await Evaluate(S930Fixtures.Build(nodes, edges: edges)));
    }

    [Fact]
    public async Task EmptyAnd_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", new LogicalPredicate { Op = LogicalOp.And, Operands = [] }));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Contains("requires at least one operand", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleSegmentFieldPath_ReturnsFinding()
    {
        var definition = Definition(Branch("agent-high", Field("scope", ComparisonOp.Eq, "x")));

        var finding = Assert.Single(await Evaluate(definition));
        Assert.Contains("must be '{artifact_key}.{property}'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InWithValues_ReturnsEmpty()
    {
        var definition = Definition(Branch("agent-high", new FieldComparisonPredicate
        {
            FieldPath = "scope.title",
            Operator = ComparisonOp.In,
            Values = ["a", "b"],
        }));

        Assert.Empty(await Evaluate(definition));
    }

    [Theory]
    [InlineData("due_at", "gt", "not-a-date", "does not coerce to the date field")]
    [InlineData("approved", "eq", "maybe", "does not coerce to the boolean field")]
    public async Task DateAndBoolCoercion_FailuresReturnFindings(string field, string op, string value, string expected)
    {
        var definition = Definition(Branch("agent-high", Field($"scope.{field}", ComparisonOp.FromName(op), value)));

        var finding = Assert.Single(await EvaluateWith(definition, new TypedFixtureCatalog()));
        Assert.Contains(expected, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("due_at", "gte", "2026-08-01T00:00:00.000Z")]
    [InlineData("approved", "eq", "true")]
    [InlineData("budget", "lt", "10.00")]
    public async Task DateBoolAndNumericCoercion_SuccessesReturnEmpty(string field, string op, string value)
    {
        var definition = Definition(Branch("agent-high", Field($"scope.{field}", ComparisonOp.FromName(op), value)));

        Assert.Empty(await EvaluateWith(definition, new TypedFixtureCatalog()));
    }

    /// <summary>A catalog whose every contract resolves to <see cref="TypedFixture"/> — gives the coercion checks date/bool/decimal terminals no shipped section contract exposes today.</summary>
    private sealed class TypedFixtureCatalog : IContractTypeCatalog
    {
        public bool Resolves(string contractTypeName) => true;
        public Type? Resolve(string contractTypeName) => typeof(TypedFixture);
        public IReadOnlyList<string> Names => [nameof(TypedFixture)];
    }

    private sealed record TypedFixture
    {
        /// <summary>No wire attribute — proves path walking matches only [JsonPropertyName] names.</summary>
        public string NotOnTheWire { get; init; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("due_at")]
        public DateTime DueAt { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("approved")]
        public bool Approved { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("budget")]
        public decimal Budget { get; init; }
    }

    private static async Task<IReadOnlyList<ValidationFinding>> EvaluateWith(WorkflowDefinition definition, IContractTypeCatalog catalog) =>
        await new DeterminismPredicatesCompileRule(catalog)
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

    private static async Task<IReadOnlyList<ValidationFinding>> Evaluate(WorkflowDefinition definition) =>
        await new DeterminismPredicatesCompileRule(new ReflectionContractTypeCatalog(TestContractSet.Instance))
            .EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);

    private static FieldComparisonPredicate Field(string path, ComparisonOp op, string value) =>
        new() { FieldPath = path, Operator = op, Value = value };

    private static ConditionalBranch Branch(string target, ConditionalPredicate condition) =>
        new() { TargetNodeId = target, Condition = condition };

    /// <summary>agent-scope(scope) [+ agent-approach(approach)] → decision ─┬→ agent-high(high) / default agent-low(low).</summary>
    private static WorkflowDefinition Definition(ConditionalBranch branch, bool approachUpstream = false)
    {
        List<WorkflowNode> nodes =
        [
            S930Fixtures.Agent("agent-scope", outputContract: "SummaryArtifact", sectionKey: "scope"),
            new DecisionNode { NodeId = "decision-1", DefaultBranchNodeId = "agent-low", Branches = [branch] },
            S930Fixtures.Agent("agent-high", outputContract: "PlanArtifact", sectionKey: "high"),
            S930Fixtures.Agent("agent-low", outputContract: "PlanArtifact", sectionKey: "low"),
        ];
        List<WorkflowEdge> edges =
        [
            S930Fixtures.Control("agent-scope", "decision-1"),
            S930Fixtures.Control("decision-1", "agent-high"),
            S930Fixtures.Control("decision-1", "agent-low"),
            S930Fixtures.Data("agent-scope", "agent-high", "SummaryArtifact"), // Data edges are ignored by branch-target checks
        ];
        if (approachUpstream)
        {
            nodes.Add(S930Fixtures.Agent("agent-approach", outputContract: "PlanArtifact", sectionKey: "approach"));
            edges.Add(S930Fixtures.Control("agent-approach", "decision-1"));
        }

        return S930Fixtures.Build(nodes, edges: edges);
    }
}
