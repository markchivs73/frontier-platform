namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The consumer-owned port through which every model-role governance decision is recorded before it
/// is made (ADR-PA32; K6). This library is governance-tier and may reference only
/// <c>Platform.Abstractions</c> and <c>Platform.Serialization</c> among platform packages (ADR-PA5,
/// enforced by <c>GovernanceLibrary_OnlyReferencesAbstractionsAndSerializationAmongPlatformLibraries</c>),
/// so it cannot reference <c>Frontier.Platform.Audit</c> and cannot append the S13.103 signed
/// governance record itself. It declares the port; the consumer adapts it over its own
/// <c>IGovernanceAuditWriter</c>, which already sits on the platform's signed chain (ADR-PA30).
/// <para>
/// There is deliberately <b>no default registration</b>. A no-op default would turn an unwired
/// consumer into a silent audit gap, which is precisely the K6 failure this port exists to close —
/// so an unregistered recorder fails at composition, exactly as the
/// <see cref="IReferencedRolesSource"/> port does.
/// </para>
/// </summary>
public interface IMappingDecisionRecorder
{
    /// <summary>
    /// Records <paramref name="decision"/> and returns the audit record's id. Called <b>before</b> the
    /// change it describes, so a record that cannot be written stops the change (fail closed).
    /// </summary>
    /// <exception cref="Abstractions.ContractViolationException">The decision is not recordable. Permanent; never retried.</exception>
    Task<string> RecordAsync(MappingGovernanceDecision decision, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the change described by <paramref name="recordId"/> did not happen after its record
    /// landed. Never cancellable at the call site: once the first record exists, the trail must say so.
    /// </summary>
    Task RecordCompensationAsync(MappingGovernanceDecision decision, string recordId, string failureReason, CancellationToken cancellationToken);
}

/// <summary>
/// One model-role governance decision, in terms this library owns. Deliberately free of
/// <c>Frontier.Platform.Audit</c> types: the consumer maps it onto its own governance-change
/// vocabulary (ADR-E2, ADR-E3a — the event catalogue is consumer-owned).
/// </summary>
public sealed record MappingGovernanceDecision
{
    /// <summary>What happened, as one of <see cref="MappingGovernanceEventTypes"/>.</summary>
    public required string EventType { get; init; }

    /// <summary>The role the decision concerns — the audit subject.</summary>
    public required string RoleId { get; init; }

    /// <summary>The proposal the decision concerns, when there is one. A rollback has none.</summary>
    public string? ProposalId { get; init; }

    /// <summary>Who directed the decision, from the authenticated principal (ADR-E8). Never <c>unknown</c>.</summary>
    public required string Actor { get; init; }

    /// <summary>Why. Required on every decision, including rollback (doc 08 §8).</summary>
    public required string Reason { get; init; }

    /// <summary>When the decision was taken.</summary>
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>The mapping version live before the decision, when one was.</summary>
    public int? FromVersion { get; init; }

    /// <summary>The mapping version live after it, when the decision moves the pointer.</summary>
    public int? ToVersion { get; init; }

    /// <summary>The ring the decision puts that version in, when it changes one.</summary>
    public RolloutRing? Ring { get; init; }

    /// <summary>The proposal's state after the decision.</summary>
    public MappingProposalState? State { get; init; }
}

/// <summary>
/// The event vocabulary this library emits through <see cref="IMappingDecisionRecorder"/>. Snake_case,
/// matching the platform governance record's <c>event_type</c> rules (ADR-PA30).
/// </summary>
public static class MappingGovernanceEventTypes
{
    /// <summary>A mapping change was proposed.</summary>
    public const string Proposed = "model_role_mapping_proposed";

    /// <summary>A proposal was approved and its version went live in the canary ring.</summary>
    public const string Approved = "model_role_mapping_approved";

    /// <summary>A proposal was refused.</summary>
    public const string Rejected = "model_role_mapping_rejected";

    /// <summary>A proposal was retracted by its proposer.</summary>
    public const string Withdrawn = "model_role_mapping_withdrawn";

    /// <summary>An approved mapping was promoted to the fleet ring.</summary>
    public const string Promoted = "model_role_mapping_promoted";

    /// <summary>A role's current pointer was rolled back to an earlier version.</summary>
    public const string RolledBack = "model_role_mapping_rolled_back";
}
