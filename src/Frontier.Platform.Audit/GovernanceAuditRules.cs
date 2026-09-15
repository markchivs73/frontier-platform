using System.Text.RegularExpressions;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The validation both governance audit contracts share (ADR-PA30). Every violation is a
/// <see cref="ContractViolationException"/>: a permanent failure that is never retried.
/// </summary>
internal static partial class GovernanceAuditRules
{
    /// <summary>The prefix of an explicit machine actor (ADR-E8).</summary>
    internal const string SystemActorPrefix = "system:";

    [GeneratedRegex("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SnakeCase();

    /// <summary>Whether <paramref name="value"/> is a non-empty snake_case token.</summary>
    internal static bool IsSnakeCase(string? value) => value is not null && SnakeCase().IsMatch(value);

    /// <summary>Adds every violation in <paramref name="entry"/> to <paramref name="violations"/>.</summary>
    internal static void CollectEntryViolations(GovernanceAuditEntry entry, List<string> violations)
    {
        RequireSnakeCase("scope", entry.Scope, violations);
        RequireSnakeCase("event_type", entry.EventType, violations);
        RequireSnakeCase("subject_type", entry.SubjectType, violations);
        Require(!string.IsNullOrWhiteSpace(entry.SubjectId), "subject_id must not be empty.", violations);
        Require(!string.IsNullOrWhiteSpace(entry.Reason), "reason must not be empty.", violations);
        Require(entry.CompensatesRecordId is null || !string.IsNullOrWhiteSpace(entry.CompensatesRecordId), "compensates_record_id must not be empty when present.", violations);
        CollectActorViolations(entry.Actor, violations);
        CollectChangeViolations(entry, violations);
    }

    /// <summary>ADR-E8: an actor is present, is never <c>unknown</c>, and a <c>system:</c> actor names its origin.</summary>
    internal static void CollectActorViolations(string? actor, List<string> violations)
    {
        var trimmed = actor?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            violations.Add("actor must not be empty (ADR-E8).");
        }
        else if (string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("actor must not be 'unknown' — an unattributed change is not recordable (ADR-E8).");
        }
        else if (string.Equals(trimmed, SystemActorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("a system actor must name its origin, e.g. 'system:role-sweeper' (ADR-E8).");
        }
    }

    /// <summary>Cascades the change envelope's own validation, prefixing its violations.</summary>
    internal static void CollectChangeViolations(GovernanceAuditEntry entry, List<string> violations)
    {
        if (entry.Change is null)
        {
            violations.Add("change must be present.");
            return;
        }

        try
        {
            entry.Change.Validate();
        }
        catch (ContractViolationException ex)
        {
            violations.AddRange(ex.Violations.Select(violation => $"change: {violation}"));
        }
    }

    /// <summary>Throws a <see cref="ContractViolationException"/> for <paramref name="contractType"/> when there are violations.</summary>
    internal static void ThrowIfAny(string contractType, List<string> violations)
    {
        if (violations.Count > 0)
        {
            throw new ContractViolationException(contractType, violations);
        }
    }

    private static void RequireSnakeCase(string field, string? value, List<string> violations) =>
        Require(IsSnakeCase(value), $"{field} must be a non-empty snake_case token.", violations);

    private static void Require(bool condition, string violation, List<string> violations)
    {
        if (!condition)
        {
            violations.Add(violation);
        }
    }
}
