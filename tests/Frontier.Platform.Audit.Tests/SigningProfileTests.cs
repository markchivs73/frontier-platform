namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.107 tests for the profile guard (ADR-PA33, ADR-SEC3): the committed dev key is allowed only
/// against local emulators, and the configured key identifier is parsed into the two parts the SDK
/// clients need.
/// </summary>
public sealed class SigningProfileTests
{
    [Fact]
    public void Evaluate_DevKeyOutsideTheLocalProfile_Fails()
    {
        // The S13.107 defect, made impossible: a deployed process with no Key Vault configured would
        // otherwise sign real evidence with a key whose material is in the repository.
        var result = SigningProfileCheck.Evaluate(usesKeyVault: false, isLocalProfile: false);

        Assert.False(result.Passed);
        Assert.Contains("AuditSigning:KeyIdentifier", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_DevKeyInTheLocalProfile_Passes()
    {
        Assert.True(SigningProfileCheck.Evaluate(usesKeyVault: false, isLocalProfile: true).Passed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_KeyVaultConfigured_PassesInEitherProfile(bool isLocalProfile)
    {
        Assert.True(SigningProfileCheck.Evaluate(usesKeyVault: true, isLocalProfile).Passed);
    }

    [Theory]
    [InlineData("https://localhost:8081", true)]
    [InlineData("https://127.0.0.1:8081", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("https://frontier.documents.azure.com:443", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLocalEndpoint_ClassifiesTheEndpoint(string? endpoint, bool expected)
    {
        // An absent endpoint is deliberately NOT local: the check must fail towards "deployed".
        Assert.Equal(expected, LocalProfile.IsLocalEndpoint(endpoint));
    }

    [Fact]
    public void UsesKeyVault_TracksWhetherAKeyIdentifierIsSet()
    {
        Assert.False(new AuditSigningOptions().UsesKeyVault);
        Assert.False(new AuditSigningOptions { KeyIdentifier = "  " }.UsesKeyVault);
        Assert.True(new AuditSigningOptions { KeyIdentifier = "https://kv.vault.azure.net/keys/audit" }.UsesKeyVault);
    }

    [Theory]
    [InlineData("https://frontier-kv.vault.azure.net/keys/audit")]
    [InlineData("https://frontier-kv.vault.azure.net/keys/audit/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void KeyVaultKeyUri_SplitsVaultAndName(string keyIdentifier)
    {
        // A versionless and a versioned identifier must name the same key: the current version is
        // resolved at signing time, so a rotation needs no configuration change.
        Assert.Equal(new Uri("https://frontier-kv.vault.azure.net"), KeyVaultKeyUri.VaultUri(keyIdentifier));
        Assert.Equal("audit", KeyVaultKeyUri.KeyName(keyIdentifier));
    }

    [Fact]
    public void KeyVaultKeyUri_NotAKeyIdentifier_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => KeyVaultKeyUri.KeyName("https://frontier-kv.vault.azure.net/secrets/audit"));

        Assert.Contains("is not a Key Vault key identifier", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningAlgorithm_SerializesAsSnakeCaseNames()
    {
        Assert.Equal("hmac_sha256", SigningAlgorithm.HmacSha256.Name);
        Assert.Equal("es256", SigningAlgorithm.Es256.Name);
        Assert.Equal(SigningAlgorithm.Es256, SigningAlgorithm.FromName("es256"));
    }

    [Fact]
    public void SigningKey_PositionalConstructor_StillMeansHmac()
    {
        // The pre-ADR-PA33 constructor keeps its exact meaning, so every stored dev-key record and
        // every existing caller resolves to an HMAC key with no change.
        Assert.Equal(SigningAlgorithm.HmacSha256, new SigningKey("dev-key/v1", "m"u8.ToArray()).Algorithm);
        Assert.Equal(SigningAlgorithm.Es256, SigningKey.ForEs256("kv/v1", "m"u8.ToArray()).Algorithm);
    }
}
