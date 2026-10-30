using System.Text.Json;
using Frontier.Platform.Serialization;
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

    /// <summary>ADR-PA41: MCP steps and their contract hashes. New goldens; every older audit golden is unchanged.</summary>
    [Fact]
    public void AuditRecordWithMcpToolInvocations_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordWithMcpToolInvocations(), "audit_record_mcp_tool_invocations.json");

    [Fact]
    public void SignedAuditRecordWithMcpToolInvocations_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecordWithMcpToolInvocations(), "signed_audit_record_mcp_tool_invocations.json");

    /// <summary>
    /// ADR-PA41: verification rehydrates a stored record and re-serializes it. A record signed before MCP
    /// steps were audited must come back to exactly its stored bytes through both copies the hasher makes,
    /// or its signature would fail and read as tampering.
    /// </summary>
    [Theory]
    [InlineData("signed_audit_record.json")]
    [InlineData("signed_audit_record_optional_fields.json")]
    public void RecordSignedBeforeMcpSteps_RecomputesToItsStoredBytes(string goldenFileName)
    {
        var stored = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", goldenFileName));
        var record = JsonSerializer.Deserialize<SignedAuditRecord>(stored, CanonicalProfile.Options)!;

        var recomputed = AuditRecordHasher.ToSignedShape(AuditRecordHasher.ToAuditRecord(record), record.PreviousRecordHash, record.RecordHash, record.Signature, record.SigningKeyId);

        Assert.Null(record.McpToolInvocations);
        Assert.Equal(stored, CanonicalProfile.SerializeCanonical(recomputed));
    }
}
