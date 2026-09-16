using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The base of every governance refusal (ADR-PA34). A refusal is a decision the service will not
/// take; it is permanent and never retried (invariant 7), which is why it derives from
/// <see cref="ContractViolationException"/> rather than standing beside it — every existing
/// <c>catch (ContractViolationException)</c> keeps catching, and the permanent classification in
/// <c>FailureClassifier</c> and DTF's <c>IsCausedBy&lt;T&gt;</c> (both honour base types) still holds.
/// <para>
/// Before ADR-PA34 one exception type covered unknown proposal, illegal transition, distinct
/// approver, proposer-only, already-pending and invalid input, so a caller could only tell them
/// apart by parsing message text. Each now has its own type, so the consumer's API maps a status
/// code by <c>catch</c> rather than by string matching.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "A refusal is only ever raised by this library with the structured violation list a consumer maps its status code from; message-only constructors would allow a refusal carrying no ContractType and no violations, and would be dead, untestable surface on a published type (ADR-PA34).")]
public abstract class MappingGovernanceRefusalException(string contractType, IReadOnlyList<string> violations)
    : ContractViolationException(contractType, violations);

/// <summary>
/// The change or proposal is not well-formed (ADR-PA34): no role, no reason, an ill-shaped or
/// empty chain, a target-less entry, or a canary percent outside 1–100. The consumer maps it to
/// <b>400</b> — the request itself is wrong, and re-sending it unchanged fails identically.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "A refusal is only ever raised by this library with its structured violation list, which is what the consumer maps a status code from; message-only constructors would be dead, untestable surface on a published type (ADR-PA34).")]
public sealed class InvalidMappingChangeException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The role holds no proposal by that id (ADR-PA34). The consumer maps it to <b>404</b>.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class UnknownMappingProposalException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The role has no such mapping version, or no <c>current</c> pointer at all (ADR-PA34). The
/// consumer maps it to <b>404</b>: the rollback target named does not exist.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class UnknownMappingVersionException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The mapping version exists but may not become <c>current</c> — a shadow version has never
/// served (ADR-PA34). Distinct from <see cref="UnknownMappingVersionException"/> because the
/// version is real: the consumer maps it to <b>409</b>, not 404.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class MappingVersionNotRollbackEligibleException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The proposal cannot move from where it is to where the decision would take it (doc 08 §7,
/// ADR-PA34) — approving an approved proposal, promoting a pending one, deciding a terminal one.
/// The consumer maps it to <b>409</b> and names the proposal's current state.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class IllegalMappingTransitionException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The principal deciding is the one who proposed (<c>requireDistinctApprover</c>, doc 15 §4;
/// ADR-PA34). The consumer maps it to <b>403</b> <c>distinct_approver_required</c> — the caller is
/// authenticated and the request well-formed; this principal may not take this decision.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class DistinctApproverRequiredException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// Only the proposer may withdraw their own proposal (ADR-PA34). The consumer maps it to
/// <b>403</b> <c>not_proposer</c>. The mirror of <see cref="DistinctApproverRequiredException"/>,
/// and a separate type because the remedy the UI offers differs: reject it instead.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class ProposerOnlyWithdrawalException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The role already holds a proposal awaiting a decision (Mark's call, 2026-09-16; ADR-PA34). The
/// consumer maps it to <b>409</b> <c>proposal_already_pending</c> and renders the open proposal and
/// its proposer, which the message names.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class MappingProposalAlreadyPendingException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);

/// <summary>
/// The caller supplied an expected concurrency token and the stored proposal no longer carries it
/// (ADR-PA34): somebody else decided this proposal while the caller was looking at it. The consumer
/// maps it to <b>409</b> and re-reads.
/// <para>
/// <b>This is a refusal, never a retry.</b> The service's internal retry covers a different thing —
/// losing a mapping-<i>version</i> allocation race, where re-reading and re-allocating produces the
/// decision the caller asked for. Here re-reading would produce a decision on a proposal the caller
/// has never seen, so the only correct answer is to stop and say so.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "See InvalidMappingChangeException; refusals are constructed only by the governance service with their violation list (ADR-PA34).")]
public sealed class MappingConcurrencyConflictException(string contractType, IReadOnlyList<string> violations)
    : MappingGovernanceRefusalException(contractType, violations);
