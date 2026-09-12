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

    /// <summary>
    /// ADR-PA26: a dispatcher child's snapshot carries its mode and its work item. New golden —
    /// every existing snapshot golden above is untouched, which is the compatibility claim.
    /// </summary>
    [Fact]
    public void ExecutionSnapshotDispatcherChild_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotDispatcherChild(), "execution_snapshot_dispatcher_child.json");

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
    public void ExecutionSnapshotWithContextPin_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotWithContextPin(), "execution_snapshot_context_pin.json");

    [Fact]
    public void ExecutionSnapshotWithHostBuild_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.ExecutionSnapshotWithHostBuild(), "execution_snapshot_host_build.json");

    [Fact]
    public void TypedPayloadByRef_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.TypedPayloadByRef(), "typed_payload.json");

    [Fact]
    public void TypedPayloadInline_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(ContractSamples.TypedPayloadInline());

    /// <summary>
    /// S13.22 — <see cref="WorkItem"/> is a <b>live wire contract</b> and gets the full treatment.
    /// <para>
    /// It carried <c>required object Payload</c>, which is not a contract at all: <c>object</c>
    /// deserializes as a <c>JsonElement</c>, so every consumer re-inspects an untyped blob and the
    /// ADR-E1 tonnage path (a large payload staged by reference) has nowhere to live. That is K4
    /// erosion in the one place it matters most — this is <em>external</em> input crossing into DTF
    /// history, where the bytes are evidential and permanent. ADR-E2's <see cref="TypedPayload"/>
    /// is the engine's one generic carriage for exactly this, inline or by reference, naming the
    /// schema its content conforms to.
    /// </para>
    /// <para>
    /// A golden file is therefore not ceremony here: a work item's bytes sit in the durable history
    /// of an eternal instance and are replayed for as long as it lives.
    /// </para>
    /// </summary>
    [Fact]
    public void WorkItem_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(ContractSamples.WorkItem(), "work_item.json");

    /// <summary>The inline-envelope, no-directing-human variant: <c>directed_by</c> is omitted, not null.</summary>
    [Fact]
    public void WorkItemInline_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(ContractSamples.WorkItemInline());
}
