using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// S1.6 contract test suite (canonical-serialization skill, QG-1): every
/// <see cref="IVersionedContract"/> serializes to byte-identical canonical bytes across
/// cultures, matches its committed golden file, and round-trips without change.
/// </summary>
public sealed class ContractGoldenFileTests
{

    [Fact]
    public void ExecutionSnapshot_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshot(), "execution_snapshot.json");

    /// <summary>S9.45: the new <c>failure_classification</c> field's wire shape, distinct from the paused-at-gate sample above.</summary>
    [Fact]
    public void ExecutionSnapshot_PausedOnFailure_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotPausedOnFailure(), "execution_snapshot_paused_on_failure.json");

    [Fact]
    public void WorkflowDefinition_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.WorkflowDefinition(), "workflow_definition.json");

    [Fact]
    public void WorkflowDefinitionDecisionBranches_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.WorkflowDefinitionDecisionBranches(), "workflow_definition_decision_branches.json");

    [Fact]
    public void ExecutionSnapshotSkipped_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotSkipped(), "execution_snapshot_skipped.json");


    [Fact]
    public void ConsolidateAuditInput_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(ContractSamples.ConsolidateAuditInput());

    [Fact]
    public void PayloadRef_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.PayloadRef(), "payload_ref.json");

    [Fact]
    public void ExecutionSnapshotInitiated_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotInitiated(), "execution_snapshot_initiated.json");

    [Fact]
    public void ExecutionSnapshotWithHostBuild_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotWithHostBuild(), "execution_snapshot_host_build.json");

    [Fact]
    public void TypedPayloadByRef_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.TypedPayloadByRef(), "typed_payload.json");

    [Fact]
    public void TypedPayloadInline_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(ContractSamples.TypedPayloadInline());
}
