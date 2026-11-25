using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;
using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.293 (ADR-PA54, doc 15 §6): the deployment tenant inside signed audit records, both families.</summary>
public sealed class AuditTenantStampTests
{
    private static readonly DevKeyProvider KeyProvider = new();

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "", null)]
    [InlineData(null, "  ", null)]
    [InlineData(null, "contoso", "contoso")]
    [InlineData("contoso", "contoso", "contoso")]
    [InlineData("", "contoso", "contoso")]
    [InlineData(" ", null, null)]
    public void Resolve_StampsTheDeploymentTenant_AndKeepsAnEqualOne(string? recordTenant, string? deploymentTenant, string? expected) =>
        Assert.Equal(expected, AuditTenantStamp.Resolve(recordTenant, deploymentTenant));

    /// <summary>A record may not claim a different tenant, nor any tenant when the deployment has none, nor an over-long one.</summary>
    [Theory]
    [InlineData("fabrikam", "contoso")]
    [InlineData("contoso", null)]
    [InlineData("contoso", "")]
    public void Resolve_ATenantTheDeploymentCannotVouchFor_IsAContractViolation(string recordTenant, string? deploymentTenant) =>
        Assert.Throws<ContractViolationException>(() => AuditTenantStamp.Resolve(recordTenant, deploymentTenant));

    [Fact]
    public void Resolve_OverLongTenantOnEitherSide_IsAContractViolation()
    {
        var tooLong = new string('t', AuditTenantStamp.MaxLength + 1);

        Assert.Throws<ContractViolationException>(() => AuditTenantStamp.Resolve(tooLong, null));
        Assert.Throws<ContractViolationException>(() => AuditTenantStamp.Resolve(null, tooLong));
        Assert.Equal(new string('t', AuditTenantStamp.MaxLength), AuditTenantStamp.Resolve(null, new string('t', AuditTenantStamp.MaxLength)));
    }

    /// <summary>A refused tenant is refused before the signing service is ever asked to sign.</summary>
    [Fact]
    public async Task SignAsync_ARefusedTenant_NeverReachesTheSigningService()
    {
        var store = new FakeAuditRecordStore();
        var signing = new CountingSigningService(new HmacAuditSigningService(KeyProvider));
        var signer = new AuditSigner(store, store, KeyProvider, signing,
            Options.Create(new ExecutionAuditOptions { AppendBaseDelayMs = 0, AppendMaxDelayMs = 0 }), Options.Create(new AuditTenantOptions()));

        await Assert.ThrowsAsync<ContractViolationException>(() => signer.SignAsync(AuditRecordHasherTests.Sample() with { Tenant = "fabrikam" }, CancellationToken.None));

        Assert.Equal(0, signing.Calls);
    }

    private sealed class CountingSigningService(IAuditSigningService inner) : IAuditSigningService
    {
        internal int Calls { get; private set; }

        public Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken) => inner.GetCurrentKeyIdAsync(cancellationToken);

        public Task<AuditSignature> SignAsync(string recordHash, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.SignAsync(recordHash, cancellationToken);
        }
    }

    [Fact]
    public async Task SignAsync_WithTheOption_StampsTheTenant_AndTheChainVerifies()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store, "contoso");

        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        var result = await signer.VerifyAsync(signed.ExecutionId, signed.EngagementId, CancellationToken.None);

        Assert.Equal("contoso", signed.Tenant);
        Assert.Equal(AuditChainVerdict.Valid, result.Verdict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SignAsync_WithoutTheOption_WritesNoTenant_SoTheBytesAreAsBefore(string? tenant)
    {
        var signed = await Signer(new FakeAuditRecordStore(), tenant).SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        var unstamped = await Signer(new FakeAuditRecordStore(), null).SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        Assert.Null(signed.Tenant);
        Assert.DoesNotContain("\"tenant\"", System.Text.Encoding.UTF8.GetString(Serialization.CanonicalProfile.SerializeCanonical(signed)), StringComparison.Ordinal);
        Assert.Equal(unstamped.RecordHash, signed.RecordHash);
    }

    [Fact]
    public async Task SignAsync_ARecordClaimingAnotherTenant_IsRefusedAndNothingIsStored()
    {
        var store = new FakeAuditRecordStore();

        await Assert.ThrowsAsync<ContractViolationException>(() =>
            Signer(store, "contoso").SignAsync(AuditRecordHasherTests.Sample() with { Tenant = "fabrikam" }, CancellationToken.None));

        Assert.Empty(await store.GetChainAsync(AuditRecordHasherTests.Sample().EngagementId, CancellationToken.None));
    }

    /// <summary>The tenant is inside the signed bytes: editing it after signing breaks the chain.</summary>
    [Fact]
    public async Task Verify_TenantEditedAfterSigning_IsBroken()
    {
        var signed = await Signer(new FakeAuditRecordStore(), "contoso").SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        var key = await KeyProvider.GetCurrentKeyAsync(CancellationToken.None);

        var result = AuditChainVerifier.Verify([signed with { Tenant = "fabrikam" }], signed.ExecutionId, signed.EngagementId, AuditChainVerifierTests.Keys(key));

        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
    }

    [Fact]
    public async Task GovernanceAppend_WithTheOption_StampsTheRecordNotTheEntry_SoTheRecordIdIsUnchanged()
    {
        var store = new FakeGovernanceAuditStore();
        var keys = new FakeRotatingKeyProvider();
        var service = new GovernanceAuditService(store, new SharedSigningKeyRing(keys, new HmacAuditSigningService(keys)),
            Options.Create(new GovernanceAuditOptions { AppendBaseDelayMs = 0, AppendMaxDelayMs = 0 }), Options.Create(new AuditTenantOptions { Tenant = "contoso" }));

        var record = await service.AppendAsync(Entry(), CancellationToken.None);

        Assert.Equal("contoso", record.Tenant);
        Assert.Equal(GovernanceAuditHasher.ComputeRecordId(Entry()), record.RecordId);
        Assert.True((await service.VerifyAsync(record.Scope, CancellationToken.None)).Valid);
    }

    [Fact]
    public void GovernanceRecord_TenantEditedAfterSigning_NoLongerMatchesItsHash()
    {
        var record = GovernanceAuditHasher.Prepare(Entry(), "id", 1, "prev", "key", "contoso");

        Assert.Equal(record.RecordHash, GovernanceAuditHasher.ComputeRecordHash(record));
        Assert.NotEqual(record.RecordHash, GovernanceAuditHasher.ComputeRecordHash(record with { Tenant = "fabrikam" }));
    }

    /// <summary>Built without the option (as older composition roots do), neither service stamps.</summary>
    [Fact]
    public void BuiltWithoutTheOption_NeitherServiceHasADeploymentTenant()
    {
        var store = new FakeAuditRecordStore();
        var keys = new FakeRotatingKeyProvider();

        Assert.Null(new AuditSigner(store, store, KeyProvider, new HmacAuditSigningService(KeyProvider), Options.Create(new ExecutionAuditOptions())).DeploymentTenant);
        Assert.Null(new GovernanceAuditService(new FakeGovernanceAuditStore(), new SharedSigningKeyRing(keys, new HmacAuditSigningService(keys)), Options.Create(new GovernanceAuditOptions())).DeploymentTenant);
    }

    private static AuditSigner Signer(FakeAuditRecordStore store, string? tenant) =>
        new(store, store, KeyProvider, new HmacAuditSigningService(KeyProvider),
            Options.Create(new ExecutionAuditOptions { AppendBaseDelayMs = 0, AppendMaxDelayMs = 0 }),
            Options.Create(new AuditTenantOptions { Tenant = tenant }));
}
