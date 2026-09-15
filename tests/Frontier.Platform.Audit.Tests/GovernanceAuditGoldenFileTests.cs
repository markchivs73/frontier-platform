using Frontier.TestSupport;
using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.103 byte-stability suite for the governance audit contracts (ADR-PA30). Three goldens: the
/// entry and the signed record as the canonical profile writes them, and the RFC 8785 bytes the
/// record hash is computed over. A change to any of them moves every stored record's hash.
/// </summary>
public sealed class GovernanceAuditGoldenFileTests
{
    [Fact]
    public void GovernanceAuditEntry_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Entry(), "governance_audit_entry.json");

    [Fact]
    public void SignedGovernanceAuditRecord_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Sample(), "signed_governance_audit_record.json");

    [Fact]
    public void SignedGovernanceAuditRecord_HashedJcsBytes_AreStableAcrossCultures()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(Sample());
        var hashed = GovernanceAuditHasher.GetCanonicalBytes(Sample());

        ContractRoundTripAssertions.AssertMatchesGoldenFile(hashed, "signed_governance_audit_record.hashed.jcs.json");
        Assert.NotEqual(bytes, hashed);
    }

    /// <summary>The golden record: a compensating entry at sequence 2, so every optional field is present.</summary>
    private static SignedGovernanceAuditRecord Sample() =>
        Seal(Entry() with { CompensatesRecordId = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08" }, 2, Genesis, FakeRotatingKeyProvider.V1);
}
