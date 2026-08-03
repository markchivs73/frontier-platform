using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Observability;

/// <summary>
/// Query scope for <see cref="IEmpiricalQueryService"/> (doc 11 §2): narrows the audit
/// store read to the specified combination of engagement type, workflow, version, role,
/// and date range. All fields are optional — null means "all values for that dimension".
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record EmpiricalScope(
    string? EngagementType = null,
    string? WorkflowId = null,
    int? DefinitionVersion = null,
    string? AgentRole = null,
    DateRange? DateRange = null);

/// <summary>An inclusive UTC date range for <see cref="EmpiricalScope"/> queries.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record DateRange(DateTimeOffset From, DateTimeOffset To);
