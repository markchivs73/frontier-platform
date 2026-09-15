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
/// Chooses the <see cref="IKeyProvider"/> for a <see cref="SigningKeyPurpose"/> (ADR-PA30). The
/// registered ring gives every purpose the one versioned audit key; a deployment that wants a
/// separate governance key registers its own ring, with no change to any other surface.
/// </summary>
public interface ISigningKeyRing
{
    /// <summary>The key provider for <paramref name="purpose"/>.</summary>
    IKeyProvider GetProvider(SigningKeyPurpose purpose);
}

/// <summary>The phase 1 <see cref="ISigningKeyRing"/>: one provider for every purpose.</summary>
internal sealed class SharedSigningKeyRing(IKeyProvider keyProvider) : ISigningKeyRing
{
    /// <inheritdoc />
    public IKeyProvider GetProvider(SigningKeyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        return keyProvider;
    }
}
