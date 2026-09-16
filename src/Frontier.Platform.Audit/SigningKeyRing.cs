using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>What a signing key is used for (ADR-PA30). Serializes as a snake_case string.</summary>
public sealed class SigningKeyPurpose : SmartEnum<SigningKeyPurpose>
{
    /// <summary>Signing execution audit records (doc 05 §5).</summary>
    public static readonly SigningKeyPurpose ExecutionAudit = new("execution_audit");

    /// <summary>Signing governance audit records.</summary>
    public static readonly SigningKeyPurpose GovernanceAudit = new("governance_audit");

    private SigningKeyPurpose(string name)
        : base(name)
    {
    }
}

/// <summary>
/// Chooses the signing and verification services for a <see cref="SigningKeyPurpose"/> (ADR-PA30).
/// The registered ring gives every purpose the one versioned audit key; a deployment that wants a
/// separate governance key registers its own ring, with no change to any other surface.
///
/// <para>
/// ADR-PA33 split the ring's answer in two. Signing and verifying stopped being the same capability
/// once the private key moved inside Key Vault: <see cref="GetSigningService"/> performs an
/// operation there with the current version, while <see cref="GetProvider"/> resolves the public
/// material needed to verify any past version locally.
/// </para>
/// </summary>
public interface ISigningKeyRing
{
    /// <summary>The verification-key provider for <paramref name="purpose"/>.</summary>
    IKeyProvider GetProvider(SigningKeyPurpose purpose);

    /// <summary>The signing service for <paramref name="purpose"/>.</summary>
    IAuditSigningService GetSigningService(SigningKeyPurpose purpose);
}

/// <summary>The phase 1 <see cref="ISigningKeyRing"/>: one key for every purpose.</summary>
internal sealed class SharedSigningKeyRing(IKeyProvider keyProvider, IAuditSigningService signingService) : ISigningKeyRing
{
    /// <inheritdoc />
    public IKeyProvider GetProvider(SigningKeyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        return keyProvider;
    }

    /// <inheritdoc />
    public IAuditSigningService GetSigningService(SigningKeyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        return signingService;
    }
}
