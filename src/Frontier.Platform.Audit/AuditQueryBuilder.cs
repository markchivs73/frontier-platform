using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Audit;

/// <summary>
/// Builds the <c>audit-records</c> Cosmos SQL projection for
/// <see cref="IAuditQueryService.QueryAsync"/> from an <see cref="AuditQuery"/>'s optional
/// filters (doc 05 §7 queries 1, 2, 4, 8). The projected fields match
/// <see cref="AuditSummary"/>'s <c>[JsonPropertyName]</c>s, so the Cosmos SDK deserializes
/// each row directly into a summary. Pure and unit-tested; <see cref="AuditQueryService"/>
/// only executes the result against the emulator (cosmos-conventions: governance queries
/// may be cross-partition).
/// </summary>
internal static class AuditQueryBuilder
{
    /// <summary>The <see cref="AuditSummary"/> projection shared by every query.</summary>
    internal const string SelectClause =
        "SELECT c.record.execution_id, c.record.engagement_id, c.record.workflow_id, " +
        "c.record.definition_version, c.record.definition_hash, c.record.final_status, " +
        "c.record.started_at_utc, c.record.closed_at_utc FROM c";

    /// <summary>Builds the parameterised <see cref="QueryDefinition"/> for <paramref name="query"/>'s filters.</summary>
    internal static QueryDefinition Build(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var clauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        AppendEngagementFilter(query, clauses, parameters);
        AppendModelFilter(query, clauses, parameters);
        AppendValidatorFilter(query, clauses, parameters);
        AppendOverridesFilter(query, clauses, parameters);
        AppendDateRangeFilters(query, clauses, parameters);
        AppendDefinitionHashFilter(query, clauses, parameters);

        var sql = clauses.Count == 0 ? SelectClause : $"{SelectClause} WHERE {string.Join(" AND ", clauses)}";
        var definition = new QueryDefinition(sql);
        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }

    /// <summary>Doc 05 §7 query 8 partner filter and chain scoping: restricts to one engagement.</summary>
    internal static void AppendEngagementFilter(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (query.EngagementId is not { } engagementId)
        {
            return;
        }

        clauses.Add("c.engagement_id = @engagementId");
        parameters.Add(("@engagementId", (string)engagementId));
    }

    /// <summary>Doc 05 §7 query 1: which executions had an invocation resolved to this model.</summary>
    internal static void AppendModelFilter(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (query.ModelId is not { } modelId)
        {
            return;
        }

        clauses.Add("EXISTS(SELECT VALUE a FROM a IN c.record.agent_invocations WHERE a.resolved_model.model_id = @modelId)");
        parameters.Add(("@modelId", modelId));
    }

    /// <summary>Doc 05 §7 query 2: which executions recorded an outcome from this validator (always <c>[]</c> until Stage 6).</summary>
    internal static void AppendValidatorFilter(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (query.ValidatorId is not { } validatorId)
        {
            return;
        }

        clauses.Add("EXISTS(SELECT VALUE v FROM v IN c.record.validator_outcomes WHERE v.validator_id = @validatorId)");
        parameters.Add(("@validatorId", validatorId));
    }

    /// <summary>
    /// Doc 05 §7 query 4: which executions had a human override a prior automated
    /// outcome. This codebase's <see cref="DecisionKind"/> has no <c>override</c> value;
    /// <see cref="DecisionKind.Reject"/> is the closest analogue (a human declining
    /// section content that was produced/validated automatically).
    /// </summary>
    internal static void AppendOverridesFilter(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (!query.OverridesOnly)
        {
            return;
        }

        clauses.Add("EXISTS(SELECT VALUE d FROM d IN c.record.human_decisions WHERE d.kind = @overrideKind)");
        parameters.Add(("@overrideKind", DecisionKind.Reject.Name));
    }

    /// <summary>Restricts to records closed within [<see cref="AuditQuery.FromUtc"/>, <see cref="AuditQuery.ToUtc"/>].</summary>
    internal static void AppendDateRangeFilters(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (query.FromUtc is { } fromUtc)
        {
            clauses.Add("c.record.closed_at_utc >= @fromUtc");
            parameters.Add(("@fromUtc", fromUtc));
        }

        if (query.ToUtc is { } toUtc)
        {
            clauses.Add("c.record.closed_at_utc <= @toUtc");
            parameters.Add(("@toUtc", toUtc));
        }
    }

    /// <summary>Doc 05 §7 query 8: graph-version accountability — restricts to one exact definition graph.</summary>
    internal static void AppendDefinitionHashFilter(AuditQuery query, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (query.DefinitionHash is not { } definitionHash)
        {
            return;
        }

        clauses.Add("c.record.definition_hash = @definitionHash");
        parameters.Add(("@definitionHash", definitionHash));
    }
}
