using Frontier.Platform.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.107 tests for the signing-profile registration (ADR-PA33): configuration alone decides
/// whether the process signs with Key Vault or the local dev key, and the boot checks that police
/// that choice are always registered.
/// </summary>
public sealed class AuditSigningRegistrationTests
{
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    private const string KeyIdentifier = "https://frontier-kv.vault.azure.net/keys/frontier-audit-signing";

    [Fact]
    public void AddFrontierAudit_NoKeyIdentifier_RegistersTheLocalHmacProfile()
    {
        using var provider = Provider(keyIdentifier: null);

        Assert.IsType<DevKeyProvider>(provider.GetRequiredService<IKeyProvider>());
        Assert.IsType<HmacAuditSigningService>(provider.GetRequiredService<IAuditSigningService>());
    }

    [Fact]
    public void AddFrontierAudit_KeyIdentifierSet_RegistersTheKeyVaultProfile()
    {
        // Constructing the credential and clients must not require a live vault or a sign-in —
        // resolution is lazy, so this registers and resolves with no Azure contact.
        using var provider = Provider(KeyIdentifier);

        Assert.IsType<KeyVaultKeyProvider>(provider.GetRequiredService<IKeyProvider>());
        Assert.IsType<KeyVaultAuditSigningService>(provider.GetRequiredService<IAuditSigningService>());
        Assert.IsType<AzureKeyVaultSigningClient>(provider.GetRequiredService<IKeyVaultSigningClient>());
    }

    [Fact]
    public void AddFrontierAudit_RegistersBothSigningBootChecks()
    {
        using var provider = Provider(keyIdentifier: null);

        var checks = provider.GetServices<IStartupCheck>().ToArray();

        Assert.Contains(checks, check => check is SigningKeyCheck);
        Assert.Contains(checks, check => check is SigningProfileCheck);
    }

    [Fact]
    public async Task SigningProfileCheck_DevKeyAgainstARemoteCosmosAccount_RefusesToBoot()
    {
        // The end-to-end shape of the S13.107 defect: a deployment that simply forgot to configure a
        // signing key. It must not start.
        using var provider = Provider(keyIdentifier: null, cosmosEndpoint: "https://frontier.documents.azure.com:443");
        var check = provider.GetServices<IStartupCheck>().OfType<SigningProfileCheck>().Single();

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("not evidence", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SigningProfileCheck_DevKeyAgainstTheEmulator_Passes()
    {
        using var provider = Provider(keyIdentifier: null);
        var check = provider.GetServices<IStartupCheck>().OfType<SigningProfileCheck>().Single();

        Assert.True((await check.CheckAsync(CancellationToken.None)).Passed);
    }

    [Fact]
    public async Task SigningProfileCheck_KeyVaultAgainstARemoteCosmosAccount_Passes()
    {
        using var provider = Provider(KeyIdentifier, cosmosEndpoint: "https://frontier.documents.azure.com:443");
        var check = provider.GetServices<IStartupCheck>().OfType<SigningProfileCheck>().Single();

        Assert.True((await check.CheckAsync(CancellationToken.None)).Passed);
    }

    [Fact]
    public void SigningProfileCheck_Name_IsSigningProfile()
    {
        using var provider = Provider(keyIdentifier: null);

        Assert.Equal("SigningProfile", provider.GetServices<IStartupCheck>().OfType<SigningProfileCheck>().Single().Name);
    }

    [Fact]
    public void AddFrontierAudit_MalformedKeyIdentifier_OptionsValidationThrows()
    {
        using var provider = Provider("not-a-url");

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AuditSigningOptions>>().Value);
    }

    private static ServiceProvider Provider(string? keyIdentifier, string cosmosEndpoint = "https://localhost:8081")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = cosmosEndpoint,
            ["Cosmos:Database"] = "frontier-workflow",
            ["Cosmos:Key"] = EmulatorKey,
        };

        if (keyIdentifier is not null)
        {
            settings[$"{AuditSigningOptions.SectionName}:KeyIdentifier"] = keyIdentifier;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection().AddFrontierAudit(configuration).BuildServiceProvider();
    }
}
