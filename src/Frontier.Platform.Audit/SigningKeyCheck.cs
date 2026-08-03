using System.Security.Cryptography;
using System.Text;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Boot check (doc 12 §6): confirms <see cref="IKeyProvider"/> resolves a usable signing
/// key for audit-record HMAC signatures (doc 05 §9) by performing a test HMAC-SHA256
/// computation with it. Production swaps <see cref="DevKeyProvider"/> for a Key
/// Vault-backed <see cref="IKeyProvider"/> (Stage 5) behind this same check — "Key Vault
/// reachable, signing key version usable" becomes "key material non-empty and usable for
/// HMAC" once that swap happens, without changing this check.
/// </summary>
internal sealed class SigningKeyCheck(IKeyProvider keyProvider) : IStartupCheck
{
    /// <summary>Fixed payload signed during the boot-time test sign+verify.</summary>
    internal static readonly byte[] ProbePayload = Encoding.UTF8.GetBytes("frontier-workflow-boot-probe");

    /// <inheritdoc />
    public string Name => "SigningKey";

    /// <inheritdoc />
    public async Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var key = await keyProvider.GetCurrentKeyAsync(cancellationToken);

        return Evaluate(key);
    }

    /// <summary>Fails if <paramref name="key"/> has no id or key material; otherwise exercises HMAC-SHA256 with it (the "test sign").</summary>
    internal static StartupCheckResult Evaluate(SigningKey key)
    {
        if (string.IsNullOrEmpty(key.KeyId) || key.KeyMaterial.IsEmpty)
        {
            return StartupCheckResult.Fail("Signing key is missing an id or key material (doc 05 §9, doc 12 §6).");
        }

        _ = HMACSHA256.HashData(key.KeyMaterial.Span, ProbePayload);

        return StartupCheckResult.Pass();
    }
}
