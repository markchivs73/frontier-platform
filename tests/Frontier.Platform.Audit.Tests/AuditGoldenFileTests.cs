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
    public void AuditRecordWithContextPin_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordWithContextPin(), "audit_record_context_pin.json");

    [Fact]
    public void SignedAuditRecord_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecord(), "signed_audit_record.json");

    [Fact]
    public void SignedAuditRecordWithOptionalFields_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecordWithOptionalFields(), "signed_audit_record_optional_fields.json");

    /// <summary>ADR-E27 D15: the per-invocation contract hashes. New golden; every older audit golden is unchanged.</summary>
    [Fact]
    public void AuditRecordWithContractHashes_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordWithContractHashes(), "audit_record_contract_hashes.json");
}
