using System.ComponentModel.DataAnnotations;

namespace Frontier.Platform.Audit;

/// <summary>
/// Audit signing settings, bound from the <c>AuditSigning</c> section (ADR-PA33).
///
/// <para>
/// There is exactly one setting and it is a <em>location</em>, never a credential: the vault is
/// reached with <c>DefaultAzureCredential</c> (ADR-SEC3), so no key, secret or connection string
/// appears in configuration, environment variables or App Configuration (ADR-SEC5, doc 15 §8).
/// Leaving <see cref="KeyIdentifier"/> unset selects the local HMAC dev key, which
/// <see cref="SigningProfileCheck"/> then refuses outside the local profile.
/// </para>
/// </summary>
public sealed class AuditSigningOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "AuditSigning";

    /// <summary>
    /// The versionless Key Vault key identifier to sign with, for example
    /// <c>https://frontier-kv.vault.azure.net/keys/frontier-audit-signing</c>. Versionless by
    /// design: the current version is resolved at signing time, so a rotation needs no
    /// configuration change and no redeploy.
    /// </summary>
    /// <remarks>
    /// Named for Key Vault's own term ("key identifier") rather than <c>KeyUri</c>, which would
    /// oblige the property to be a <see cref="Uri"/> (CA1056) — configuration binding wants the
    /// string, and parsing it is <see cref="KeyVaultKeyUri"/>'s job.
    /// </remarks>
    [Url]
    public string? KeyIdentifier { get; init; }

    /// <summary>Whether a Key Vault signing key is configured at all.</summary>
    public bool UsesKeyVault => !string.IsNullOrWhiteSpace(KeyIdentifier);
}
