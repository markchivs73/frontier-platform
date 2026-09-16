using Frontier.Platform.Serialization;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// Boot check (doc 12 §6, ADR-PA33): the committed development signing key may be used only in the
/// local-emulator profile.
///
/// <para>
/// This is the check that closes S13.107. The defect it guards was not that a Key Vault provider was
/// missing — it was that a deployment with no Key Vault configured would silently sign real evidence
/// with <c>dev-key/v1</c>, a key whose material is in the repository and therefore known to anyone
/// who can read it. Every such record would verify perfectly and prove nothing. A deployed process
/// that reaches this state refuses to start.
/// </para>
/// </summary>
internal sealed class SigningProfileCheck(IOptions<AuditSigningOptions> signingOptions, IOptions<CosmosOptions> cosmosOptions) : IStartupCheck
{
    /// <inheritdoc />
    public string Name => "SigningProfile";

    /// <inheritdoc />
    public Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(signingOptions.Value.UsesKeyVault, LocalProfile.IsLocalEndpoint(cosmosOptions.Value.Endpoint)));

    /// <summary>Fails when no Key Vault signing key is configured and the profile is not local.</summary>
    internal static StartupCheckResult Evaluate(bool usesKeyVault, bool isLocalProfile) =>
        usesKeyVault || isLocalProfile
            ? StartupCheckResult.Pass()
            : StartupCheckResult.Fail(
                "Audit signing is configured to use the development HMAC key outside the local-emulator profile. " +
                "Set 'AuditSigning:KeyIdentifier' to a Key Vault key identifier (doc 05 §5, ADR-SEC3, ADR-PA33); " +
                "the dev key's material is in source control and signatures made with it are not evidence.");
}
