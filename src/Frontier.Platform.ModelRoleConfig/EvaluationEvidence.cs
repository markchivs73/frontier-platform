namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The evaluation-evidence rules a mapping is held to (ADR-E27 D16, ADR-PA40), public so the
/// consumer's API and D3 screen apply the same grammar and the same eligibility test the governance
/// service does, rather than restating them.
/// </summary>
public static class EvaluationEvidence
{
    /// <summary>The prefix of a ref to evidence recorded by hand, until the eval kit exists (S13.137 refuses it).</summary>
    public const string ManualPrefix = "eval-run:manual:";

    /// <summary>The prefix of a ref to an eval-kit run.</summary>
    public const string KitPrefix = "eval-run:kit:";

    /// <summary>
    /// Whether <paramref name="evidenceRef"/> is <c>eval-run:{manual|kit}:{id}</c> with a non-blank id.
    /// Checked by prefix only: no eval store exists to resolve the id against (ADR-PA40).
    /// </summary>
    public static bool IsWellFormed(string? evidenceRef) =>
        evidenceRef is not null && (HasNonBlankId(evidenceRef, ManualPrefix) || HasNonBlankId(evidenceRef, KitPrefix));

    /// <summary>
    /// Whether a new contract version has re-opened <paramref name="role"/>'s eligibility under
    /// <paramref name="mapping"/> (D16): the role is agent-backed and its current contract hashes are
    /// not the ones the mapping was proposed against. Such a mapping keeps serving (ADR-PA40, ADR-M2);
    /// this is the signal D3 shows, and the next proposal for the role must bring evidence for the new
    /// hashes. Always false for a model-backed role, whose evidence is optional.
    /// </summary>
    /// <exception cref="ArgumentException">The mapping is for another role.</exception>
    public static bool IsEligibilityReopened(RoleDefinition role, RoleMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(mapping);
        if (!string.Equals(role.RoleId, mapping.RoleId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"mapping is for role '{mapping.RoleId}', not '{role.RoleId}'.", nameof(mapping));
        }

        return role.Backing == RoleBacking.Agent && !CoversCurrentContracts(role, mapping);
    }

    /// <summary>Whether <paramref name="mapping"/> was proposed against <paramref name="role"/>'s current contract hashes.</summary>
    internal static bool CoversCurrentContracts(RoleDefinition role, RoleMapping mapping) =>
        string.Equals(role.InputContractHash, mapping.InputContractHash, StringComparison.Ordinal)
        && string.Equals(role.OutputContractHash, mapping.OutputContractHash, StringComparison.Ordinal);

    private static bool HasNonBlankId(string evidenceRef, string prefix) =>
        evidenceRef.StartsWith(prefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(evidenceRef[prefix.Length..]);
}
