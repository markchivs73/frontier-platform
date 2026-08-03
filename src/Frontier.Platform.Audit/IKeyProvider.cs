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
}
