namespace Frontier.Platform.Audit;

/// <summary>
/// Resolves the key material needed to <em>verify</em> audit-record signatures, by key version
/// (doc 05 §5), and backs the boot-time <c>SigningKeyCheck</c> (doc 12 §6). Deployed environments
/// get <see cref="KeyVaultKeyProvider"/>, which resolves ES256 public key versions from Azure Key
/// Vault; the local-emulator profile gets <see cref="DevKeyProvider"/>'s HMAC key
/// (<see cref="AuditServiceCollectionExtensions.AddFrontierAudit"/> chooses, and
/// <see cref="SigningProfileCheck"/> refuses the dev key anywhere else).
///
/// <para>
/// Signing is a separate capability under <see cref="IAuditSigningService"/> (ADR-PA33). This
/// interface never returns anything that can sign in a deployed environment: what it hands back
/// there is the key version's <em>public</em> part.
/// </para>
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
