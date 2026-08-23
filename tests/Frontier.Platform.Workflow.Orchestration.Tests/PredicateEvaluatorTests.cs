using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.7j (doc 14 §6/ADR-CD4): fixed-semantics predicate evaluation — invariant-culture
/// decimals (canonical string decimals included), ISO dates, ordinal strings, and
/// fail-false behaviour for anything unresolvable.
/// </summary>
public sealed class PredicateEvaluatorTests
{
    [Theory]
    [InlineData("gt", "100", true)]   // 250 > 100
    [InlineData("gt", "250", false)]
    [InlineData("lt", "300", true)]
    [InlineData("gte", "250", true)]
    [InlineData("lte", "249", false)]
    [InlineData("eq", "250", true)]
    [InlineData("neq", "250", false)]
    [InlineData("neq", "9", true)]
    public void NumericComparisons_UseDecimalSemantics(string op, string value, bool expected)
    {
        // JSON number on the left, string comparand on the right — both coerce to decimal.
        var sections = Artifacts("""{"budget":250}""");

        Assert.Equal(expected, Evaluate(Field("scope.budget", op, value), sections));
    }

    [Fact]
    public void CanonicalStringDecimal_ComparesNumerically()
    {
        // Canonical profile writes decimals as strings with declared scale — "250.00" must
        // compare as a number, not ordinally (ordinal would make "250.00" < "9").
        var sections = Artifacts("""{"rate":"250.00"}""");

        Assert.True(Evaluate(Field("scope.rate", "gt", "9"), sections));
        Assert.True(Evaluate(Field("scope.rate", "eq", "250.00"), sections));
    }

    [Fact]
    public void IsoDates_CompareChronologically()
    {
        var sections = Artifacts("""{"due":"2026-09-01T00:00:00.000Z"}""");

        Assert.True(Evaluate(Field("scope.due", "gt", "2026-08-01T00:00:00.000Z"), sections));
        Assert.False(Evaluate(Field("scope.due", "lt", "2026-08-01T00:00:00.000Z"), sections));
    }

    [Theory]
    [InlineData("eq", "urgent", true)]
    [InlineData("neq", "routine", true)]
    [InlineData("contains", "rge", true)]
    [InlineData("starts_with", "urg", true)]
    [InlineData("ends_with", "ent", true)]
    [InlineData("starts_with", "gent", false)]
    public void StringOperators_AreOrdinal(string op, string value, bool expected)
    {
        var sections = Artifacts("""{"priority":"urgent"}""");

        Assert.Equal(expected, Evaluate(Field("scope.priority", op, value), sections));
    }

    [Fact]
    public void OrderingOnNonComparableStrings_EvaluatesFalse()
    {
        var sections = Artifacts("""{"priority":"urgent"}""");

        Assert.False(Evaluate(Field("scope.priority", "gt", "routine"), sections));
    }

    [Fact]
    public void In_MatchesMembershipOrdinally()
    {
        var sections = Artifacts("""{"priority":"urgent"}""");
        var predicate = new FieldComparisonPredicate { FieldPath = "scope.priority", Operator = ComparisonOp.In, Values = ["low", "urgent"] };
        var missing = new FieldComparisonPredicate { FieldPath = "scope.priority", Operator = ComparisonOp.In, Values = ["low"] };
        var empty = new FieldComparisonPredicate { FieldPath = "scope.priority", Operator = ComparisonOp.In };

        Assert.True(Evaluate(predicate, sections));
        Assert.False(Evaluate(missing, sections));
        Assert.False(Evaluate(empty, sections));
    }

    [Fact]
    public void Booleans_CompareAsCanonicalStrings()
    {
        var sections = Artifacts("""{"approved":true}""");

        Assert.True(Evaluate(Field("scope.approved", "eq", "true"), sections));
        Assert.False(Evaluate(Field("scope.approved", "eq", "false"), sections));
    }

    [Fact]
    public void NestedPaths_WalkWireNames()
    {
        var sections = Artifacts("""{"client":{"tier":"gold"}}""");

        Assert.True(Evaluate(Field("scope.client.tier", "eq", "gold"), sections));
    }

    [Theory]
    [InlineData("scope.missing")]          // absent field
    [InlineData("other.budget")]           // unknown section
    [InlineData("scope")]                  // too few segments
    [InlineData("scope.client")]           // non-scalar terminal
    public void UnresolvableFields_EvaluateFalse(string path)
    {
        var sections = Artifacts("""{"budget":250,"client":{"tier":"gold"}}""");

        Assert.False(Evaluate(Field(path, "eq", "anything"), sections));
    }

    [Fact]
    public void FalseBooleanAndArrayTerminals_ResolveCanonically()
    {
        var sections = Artifacts("""{"approved":false,"tags":["a"]}""");

        Assert.True(Evaluate(Field("scope.approved", "eq", "false"), sections));
        Assert.False(Evaluate(Field("scope.tags", "eq", "a"), sections)); // array terminal → unresolvable
        Assert.False(Evaluate(Field("scope.approved.deeper", "eq", "x"), sections)); // walk into a scalar
    }

    [Fact]
    public void NumericEq_Mismatch_EvaluatesFalse()
    {
        var sections = Artifacts("""{"budget":250}""");

        Assert.False(Evaluate(Field("scope.budget", "eq", "9"), sections));
    }

    [Fact]
    public void UnknownPredicateSubtype_EvaluatesFalse()
    {
        // Defensive arm: the polymorphic hierarchy is open to the serializer's discriminators
        // only, but evaluation fails closed for anything else.
        var sections = Artifacts("""{"budget":250}""");

        Assert.False(Evaluate(new UnknownPredicate(), sections));
    }

    private sealed record UnknownPredicate : ConditionalPredicate;

    [Fact]
    public void MissingValue_EvaluatesFalse()
    {
        var sections = Artifacts("""{"budget":250}""");
        var predicate = new FieldComparisonPredicate { FieldPath = "scope.budget", Operator = ComparisonOp.Eq, Value = null };

        Assert.False(Evaluate(predicate, sections));
    }

    [Fact]
    public void LogicalOperators_ComposeWithCorrectArity()
    {
        var sections = Artifacts("""{"budget":250,"priority":"urgent"}""");
        var overBudget = Field("scope.budget", "gt", "100");
        var urgent = Field("scope.priority", "eq", "urgent");
        var routine = Field("scope.priority", "eq", "routine");

        Assert.True(Evaluate(Logical(LogicalOp.And, overBudget, urgent), sections));
        Assert.False(Evaluate(Logical(LogicalOp.And, overBudget, routine), sections));
        Assert.True(Evaluate(Logical(LogicalOp.Or, routine, urgent), sections));
        Assert.False(Evaluate(Logical(LogicalOp.Or, routine), sections));
        Assert.True(Evaluate(Logical(LogicalOp.Not, routine), sections));
        Assert.False(Evaluate(Logical(LogicalOp.Not, urgent), sections));
        // Malformed arities fail closed: empty And, two-operand Not.
        Assert.False(Evaluate(Logical(LogicalOp.And), sections));
        Assert.False(Evaluate(Logical(LogicalOp.Not, urgent, overBudget), sections));
    }

    [Fact]
    public void SelectBranch_FirstMatchWins_DefaultOtherwise_ThrowsWhenBranchless()
    {
        var definition = DefinitionWithArtifact("scope", "producer-1");
        var state = new GraphExecutionState();
        state.NodeOutputPayloads["producer-1"] = """{"priority":"urgent"}""";

        var decision = new DecisionNode
        {
            NodeId = "d-1",
            DefaultBranchNodeId = "fallback",
            Branches =
            [
                new ConditionalBranch { TargetNodeId = "first", Condition = Field("scope.priority", "eq", "urgent") },
                new ConditionalBranch { TargetNodeId = "second", Condition = Field("scope.priority", "neq", "low") },
            ],
        };
        Assert.Equal("first", PredicateEvaluator.SelectBranch(decision, definition, state));

        var noMatch = decision with
        {
            Branches = [new ConditionalBranch { TargetNodeId = "first", Condition = Field("scope.priority", "eq", "low") }],
        };
        Assert.Equal("fallback", PredicateEvaluator.SelectBranch(noMatch, definition, state));

        var branchless = new DecisionNode { NodeId = "d-1", DefaultBranchNodeId = "fallback" };
        Assert.Throws<ContractViolationException>(() => PredicateEvaluator.SelectBranch(branchless, definition, state));
    }

    private static bool Evaluate(ConditionalPredicate predicate, IReadOnlyDictionary<string, JsonDocument> sections) =>
        PredicateEvaluator.Evaluate(predicate, sections);

    private static FieldComparisonPredicate Field(string path, string op, string value) =>
        new() { FieldPath = path, Operator = ComparisonOp.FromName(op), Value = value };

    private static LogicalPredicate Logical(LogicalOp op, params ConditionalPredicate[] operands) =>
        new() { Op = op, Operands = operands };

    private static Dictionary<string, JsonDocument> Artifacts(string scopeJson) =>
        new(StringComparer.Ordinal) { ["scope"] = JsonDocument.Parse(scopeJson) };

    private static WorkflowDefinition DefinitionWithArtifact(string sectionKey, string nodeId) => new()
    {
        WorkflowId = "wf-eval",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Eval",
        Nodes =
        [
            new AgentTaskNode
            {
                NodeId = nodeId,
                ArtifactKey = sectionKey,
                Role = "analyst",
                InstructionsRef = "instructions/scope.md",
                InputContractType = "ScopeRequest",
                OutputContractType = "SummaryArtifact",
                ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "analyst", BaselineComponents = ["firm-standards"], DynamicFields = [] },
            },
        ],
        Edges = [],
        DefinitionHash = new string('0', 124),
        Mode = ExecutionMode.OneShot,
    };
}
