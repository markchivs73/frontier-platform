using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// Byte-stability and round-trip coverage (canonical-serialization skill) for the plain
/// contract records introduced by the audit domain model (doc 05 §3) that are not
/// themselves <see cref="IVersionedContract"/>s — they are exercised inline, inside
/// <see cref="AuditRecord"/>/<see cref="SignedAuditRecord"/>'s golden files, but each
/// also gets its own stability/round-trip check.
/// </summary>
public sealed class AuditNestedContractTests
{
    [Fact]
    public void ToolCall_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.ToolCall());

    [Fact]
    public void WorkflowEvent_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.WorkflowEvent());

    [Fact]
    public void ValidatorOutcome_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.ValidatorOutcome());

    [Fact]
    public void HumanDecisionRecord_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.HumanDecisionRecord());

    [Fact]
    public void AgentInvocation_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.AgentInvocation());

    [Fact]
    public void CacheMetrics_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.CacheMetrics());

    [Fact]
    public void AuditTelemetryRecord_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.AuditTelemetryRecord());

    [Fact]
    public void AuditQuery_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.AuditQuery());

    [Fact]
    public void AuditSummary_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.AuditSummary());

    [Fact]
    public void VerificationResult_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.VerificationResult());

    [Fact]
    public void ResolvedModelSummary_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(AuditContractSamples.ResolvedModelSummary());

}
