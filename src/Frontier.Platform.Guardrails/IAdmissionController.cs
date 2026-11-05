namespace Frontier.Platform.Guardrails;

/// <summary>
/// Admission-control middleware (doc 07 §1, §5): the unbypassable check immediately
/// before the MAF call, mirroring how Context Assembly is unbypassable for prompts.
/// </summary>
public interface IAdmissionController
{
    /// <summary>
    /// Decides whether the invocation described by <paramref name="estimate"/> may
    /// proceed against the resolved <see cref="GuardrailPolicy"/> (doc 07 §9 — Phase 1
    /// always resolves <see cref="Phase1GuardrailPolicyCatalogue.Default"/>). Throws
    /// <see cref="Abstractions.GuardrailsSuspendedException"/>, a permanent failure, while the fleet kill switch
    /// is thrown (doc 07 §5a, §10).
    /// </summary>
    Task<AdmissionDecision> AdmitAsync(InvocationCostEstimate estimate, CancellationToken cancellationToken);
}
