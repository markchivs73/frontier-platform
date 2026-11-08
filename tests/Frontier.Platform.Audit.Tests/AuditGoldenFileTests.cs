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

    /// <summary>S13.183 (ADR-E15 D3): the worker build per invocation and the run's generation. New goldens; every older audit golden is unchanged.</summary>
    [Fact]
    public void AuditRecordWithHostBuildAndGeneration_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordWithHostBuildAndGeneration(), "audit_record_host_build_generation.json");

    [Fact]
    public void SignedAuditRecordWithHostBuildAndGeneration_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecordWithHostBuildAndGeneration(), "signed_audit_record_host_build_generation.json");

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

    /// <summary>
    /// S13.183: a record signed before the worker build and the generation reached the audit must recompute
    /// to exactly its stored bytes, or verification would read the new members as tampering.
    /// </summary>
    [Theory]
    [InlineData("signed_audit_record.json")]
    [InlineData("signed_audit_record_optional_fields.json")]
    [InlineData("signed_audit_record_mcp_tool_invocations.json")]
    public void RecordSignedBeforeHostBuildAndGeneration_RecomputesToItsStoredBytes(string goldenFileName)
    {
        var stored = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", goldenFileName));
        var record = JsonSerializer.Deserialize<SignedAuditRecord>(stored, CanonicalProfile.Options)!;

        var recomputed = AuditRecordHasher.ToSignedShape(AuditRecordHasher.ToAuditRecord(record), record.PreviousRecordHash, record.RecordHash, record.Signature, record.SigningKeyId);

        Assert.Null(record.Generation);
        Assert.All(record.AgentInvocations, invocation => Assert.Null(invocation.HostBuild));
        Assert.Equal(stored, CanonicalProfile.SerializeCanonical(recomputed));
    }

    /// <summary>S13.240 (ADR-PA46): a failed run's record carries why it failed. New goldens; every older audit golden is unchanged.</summary>
    [Fact]
    public void AuditRecordFailed_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordFailed(), "audit_record_failed.json");

    [Fact]
    public void SignedAuditRecordFailed_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecordFailed(), "signed_audit_record_failed.json");

    /// <summary>S13.240 (ADR-PA46): a terminated run's record carries who ended it, why and when.</summary>
    [Fact]
    public void AuditRecordTerminated_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.AuditRecordTerminated(), "audit_record_terminated.json");

    [Fact]
    public void SignedAuditRecordTerminated_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(AuditContractSamples.SignedAuditRecordTerminated(), "signed_audit_record_terminated.json");

    /// <summary>
    /// S13.240: a record signed before failed and terminated runs were audited must recompute to exactly its
    /// stored bytes, or verification would read the two new members as tampering.
    /// </summary>
    [Theory]
    [InlineData("signed_audit_record.json")]
    [InlineData("signed_audit_record_optional_fields.json")]
    [InlineData("signed_audit_record_mcp_tool_invocations.json")]
    [InlineData("signed_audit_record_host_build_generation.json")]
    public void RecordSignedBeforeFailureAndTermination_RecomputesToItsStoredBytes(string goldenFileName)
    {
        var stored = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", goldenFileName));
        var record = JsonSerializer.Deserialize<SignedAuditRecord>(stored, CanonicalProfile.Options)!;

        var recomputed = AuditRecordHasher.ToSignedShape(AuditRecordHasher.ToAuditRecord(record), record.PreviousRecordHash, record.RecordHash, record.Signature, record.SigningKeyId);

        Assert.Null(record.Failure);
        Assert.Null(record.Termination);
        Assert.Equal(stored, CanonicalProfile.SerializeCanonical(recomputed));
    }
}
