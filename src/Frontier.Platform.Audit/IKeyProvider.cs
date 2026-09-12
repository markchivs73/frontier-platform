namespace Frontier.Platform.Audit;

/// <summary>
/// Resolves the current signing key for audit-record HMAC signatures (doc 05 §9) and
/// backs the boot-time <c>SigningKeyCheck</c> (doc 12 §6). Production deployments
/// resolve <see cref="SigningKey"/> from Azure Key Vault (Stage 5); until then
/// <see cref="DevKeyProvider"/> is the local-dev implementation registered by
/// <see cref="AuditServiceCollectionExtensions.AddFrontierAudit"/>.
/// </summary>
public interface IKeyProvider
{
    /// <summary>Returns the signing key currently in use.</summary>
    Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the signing key version identified by <paramref name="keyId"/>, or
    /// <see langword="null"/> if it cannot be resolved (a destroyed version, or an id this
    /// provider never issued). Verification resolves each record's key version through this
    /// method so records signed before a rotation keep verifying (doc 05 §5); old versions are
    /// disabled for signing but retained for verify.
    /// </summary>
    /// <remarks>
    /// Deliberately has <em>no</em> default interface implementation. The only default available
    /// would be to fall back to the current key, which is precisely the defect this method
    /// exists to remove: every implementation must answer by version or admit it cannot.
    /// </remarks>
    Task<SigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken);
}
