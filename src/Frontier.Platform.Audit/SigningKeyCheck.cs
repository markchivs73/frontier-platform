using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Boot check (doc 12 §6): confirms the audit signing path works end to end before the process
/// reports ready — the current key version resolves, a probe hash signs, and the resulting
/// signature verifies against that version's material.
///
/// <para>
/// Under ADR-PA33 this became a real reachability check rather than a self-consistency one. It signs
/// through <see cref="IAuditSigningService"/>, so in a deployed environment it makes an actual Key
/// Vault sign call: an unreachable vault, a missing key, a revoked role assignment or a
/// sign-forbidden identity all fail the check and the process refuses to start, instead of failing
/// at the first execution close with an audit record that cannot be written.
/// </para>
/// </summary>
internal sealed class SigningKeyCheck(IKeyProvider keyProvider, IAuditSigningService signingService) : IStartupCheck
{
    /// <summary>Fixed payload signed during the boot-time test sign+verify.</summary>
    internal const string ProbeRecordHash = "frontier-workflow-boot-probe";

    /// <inheritdoc />
    public string Name => "SigningKey";

    /// <inheritdoc />
    public async Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var key = await keyProvider.GetCurrentKeyAsync(cancellationToken);
            if (Evaluate(key) is { Passed: false } failure)
            {
                return failure;
            }

            var signature = await signingService.SignAsync(ProbeRecordHash, cancellationToken);

            return EvaluateProbe(signature, key);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The vault is unreachable, the key is gone, or the identity cannot sign. Whatever the
            // SDK called it, the invariant is the same and the process must not start.
            return StartupCheckResult.Fail($"The audit signing key could not be reached or used: {failure.Message} (doc 05 §5, doc 12 §6, ADR-PA33).");
        }
    }

    /// <summary>Fails if <paramref name="key"/> has no id or no material to verify with.</summary>
    internal static StartupCheckResult Evaluate(SigningKey key) =>
        string.IsNullOrEmpty(key.KeyId) || key.KeyMaterial.IsEmpty
            ? StartupCheckResult.Fail("Signing key is missing an id or key material (doc 05 §9, doc 12 §6).")
            : StartupCheckResult.Pass();

    /// <summary>
    /// Fails unless the probe signature was made by the version the provider calls current and
    /// verifies against it — the two halves of the signing path proving they agree.
    /// </summary>
    internal static StartupCheckResult EvaluateProbe(AuditSignature signature, SigningKey key)
    {
        if (!string.Equals(signature.KeyId, key.KeyId, StringComparison.Ordinal))
        {
            return StartupCheckResult.Fail(
                $"The signing service signed with key version '{signature.KeyId}' but the key provider's current version is '{key.KeyId}' (doc 12 §6, ADR-PA33).");
        }

        return AuditSignatureVerifier.Verify(ProbeRecordHash, signature.Signature, key)
            ? StartupCheckResult.Pass()
            : StartupCheckResult.Fail($"A probe signature made with key version '{key.KeyId}' did not verify against it (doc 12 §6, ADR-PA33).");
    }
}
