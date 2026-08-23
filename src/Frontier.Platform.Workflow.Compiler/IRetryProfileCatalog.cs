using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// A named resilience profile as the <c>resilience.*</c>/<c>timeouts.*</c> validation rules see
/// it (doc 13 §4.2 R2/R3, doc 10 §4/§7, S9.30): identity plus the caps a node-level
/// <c>RetryPolicySpec</c> override may tighten but never exceed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record RetryProfileDescriptor
{
    /// <summary>The profile id a <c>RetryPolicySpec.ProfileName</c> may reference, e.g. <c>"llm-default"</c>.</summary>
    public required string ProfileId { get; init; }

    /// <summary>The profile's attempt cap — <c>max_attempts_override</c> may not exceed it.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>The profile's per-attempt timeout in milliseconds — <c>timeout_seconds_override</c> may not exceed it.</summary>
    public required int TimeoutMs { get; init; }
}

/// <summary>
/// Supplies the resilience profiles the <c>resilience.profile-exists</c> and
/// <c>timeouts.nesting</c> rules validate against. A consumer-owned abstraction: the
/// implementation adapts the Resilience library's profile catalogue and is wired only in the
/// composition root, so the Definition Compiler stays within its library boundary.
/// </summary>
public interface IRetryProfileCatalog
{
    /// <summary>Returns every named resilience profile available to definitions.</summary>
    Task<IReadOnlyList<RetryProfileDescriptor>> GetProfilesAsync(CancellationToken ct);
}
