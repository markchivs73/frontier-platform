using Frontier.TestSupport;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// Golden-file suite for the audit contract family (S11.5, ADR-PA2): moved with the types
/// from the subsystem suite when the contracts entered Platform.Audit. The golden files
/// moved byte-identical — the wire never changes for a type move.
/// </summary>
public sealed class AuditGoldenFileTests
{
    [Fact]
    public void AuditRecord_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecord(), "audit_record.json");

    [Fact]
    public void SignedAuditRecord_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecord(), "signed_audit_record.json");
}
