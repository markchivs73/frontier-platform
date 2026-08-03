using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.7 tests for <see cref="AuditQueryBuilder"/> (doc 05 §7).</summary>
public sealed class AuditQueryBuilderTests
{
    [Fact]
    public void Build_EmptyQuery_ReturnsSelectClauseWithNoWhere()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery());

        Assert.Equal(AuditQueryBuilder.SelectClause, definition.QueryText);
        Assert.Empty(definition.GetQueryParameters());
    }

    [Fact]
    public void Build_NullQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AuditQueryBuilder.Build(null!));
    }

    [Fact]
    public void Build_EngagementIdSet_AddsEngagementFilter()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { EngagementId = "eng-1" });

        Assert.Contains("c.engagement_id = @engagementId", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@engagementId", (object)"eng-1"), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_ModelIdSet_AddsAgentInvocationExistsFilter()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { ModelId = "claude-fable-5" });

        Assert.Contains("EXISTS(SELECT VALUE a FROM a IN c.record.agent_invocations WHERE a.resolved_model.model_id = @modelId)", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@modelId", (object)"claude-fable-5"), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_ValidatorIdSet_AddsValidatorOutcomeExistsFilter()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { ValidatorId = "validator-1" });

        Assert.Contains("EXISTS(SELECT VALUE v FROM v IN c.record.validator_outcomes WHERE v.validator_id = @validatorId)", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@validatorId", (object)"validator-1"), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_OverridesOnlyTrue_AddsHumanDecisionRejectFilter()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { OverridesOnly = true });

        Assert.Contains("EXISTS(SELECT VALUE d FROM d IN c.record.human_decisions WHERE d.kind = @overrideKind)", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@overrideKind", (object)DecisionKind.Reject.Name), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_OverridesOnlyFalse_NoFilterAdded()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { OverridesOnly = false });

        Assert.Equal(AuditQueryBuilder.SelectClause, definition.QueryText);
    }

    [Fact]
    public void Build_FromUtcSet_AddsClosedAtLowerBoundFilter()
    {
        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var definition = AuditQueryBuilder.Build(new AuditQuery { FromUtc = fromUtc });

        Assert.Contains("c.record.closed_at_utc >= @fromUtc", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@fromUtc", (object)fromUtc), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_ToUtcSet_AddsClosedAtUpperBoundFilter()
    {
        var toUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var definition = AuditQueryBuilder.Build(new AuditQuery { ToUtc = toUtc });

        Assert.Contains("c.record.closed_at_utc <= @toUtc", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@toUtc", (object)toUtc), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_DefinitionHashSet_AddsDefinitionHashFilter()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery { DefinitionHash = "abc123" });

        Assert.Contains("c.record.definition_hash = @definitionHash", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(("@definitionHash", (object)"abc123"), definition.GetQueryParameters());
    }

    [Fact]
    public void Build_MultipleFilters_JoinedWithAnd()
    {
        var definition = AuditQueryBuilder.Build(new AuditQuery
        {
            EngagementId = "eng-1",
            ModelId = "claude-fable-5",
            DefinitionHash = "abc123",
        });

        var whereIndex = definition.QueryText.IndexOf("WHERE", StringComparison.Ordinal);
        Assert.True(whereIndex > 0);

        var whereClause = definition.QueryText[whereIndex..];
        Assert.Contains("c.engagement_id = @engagementId AND EXISTS(SELECT VALUE a FROM a IN c.record.agent_invocations WHERE a.resolved_model.model_id = @modelId) AND c.record.definition_hash = @definitionHash", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FromAndToUtcSet_BothFiltersAddedInOrder()
    {
        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var definition = AuditQueryBuilder.Build(new AuditQuery { FromUtc = fromUtc, ToUtc = toUtc });

        Assert.Contains("c.record.closed_at_utc >= @fromUtc AND c.record.closed_at_utc <= @toUtc", definition.QueryText, StringComparison.Ordinal);
    }
}
