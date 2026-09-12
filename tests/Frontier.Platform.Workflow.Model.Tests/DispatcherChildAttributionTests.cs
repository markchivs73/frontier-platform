using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// ADR-PA26: a dispatcher child's run must be identifiable in the execution projection.
/// <para>
/// Two properties carry it — the run's <see cref="ExecutionSnapshot.ExecutionMode"/> and its
/// <see cref="ExecutionSnapshot.WorkItemId"/> — and both are additive under the ADR-E15
/// compatibility floor. These tests pin the three claims the change rests on: the fields are
/// omitted entirely when null (so nothing already stored moves), bytes recorded before they
/// existed read back as null rather than as a default, and two children of one engagement are
/// distinguishable, which is the failure the change exists to fix.
/// </para>
/// </summary>
public sealed class DispatcherChildAttributionTests
{
    [Fact]
    public void Serialize_NeitherFieldSet_OmitsBothKeys()
    {
        var json = Json(Snapshot());

        Assert.False(json.ContainsKey("execution_mode"));
        Assert.False(json.ContainsKey("work_item_id"));
    }

    /// <summary>
    /// The compatibility claim in its strongest form: a snapshot without these fields is
    /// byte-identical to the committed golden written before they existed. If this moves, so has
    /// every stored document, and the change is not additive at all.
    /// </summary>
    [Fact]
    public void Serialize_NeitherFieldSet_IsByteIdenticalToThePreChangeGolden()
    {
        var bytes = CanonicalProfile.SerializeCanonical(Serialization.ContractSamples.ExecutionSnapshot());

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "execution_snapshot.json");
    }

    [Fact]
    public void Serialize_DispatcherChild_WritesTheModesCanonicalNameAndTheWorkItem()
    {
        var json = Json(Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-1" });

        Assert.Equal("dispatcher", (string?)json["execution_mode"]);
        Assert.Equal("TICKET-1", (string?)json["work_item_id"]);
    }

    [Fact]
    public void Serialize_OneShotRun_WritesTheModesCanonicalName() =>
        Assert.Equal("one_shot", (string?)Json(Snapshot() with { ExecutionMode = ExecutionMode.OneShot })["execution_mode"]);

    /// <summary>ADR-E15's floor: bytes recorded before the fields existed replay as null, not as a default mode.</summary>
    [Fact]
    public void Deserialize_BytesRecordedBeforeTheFieldsExisted_ReadsBothAsNull()
    {
        var legacy = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "execution_snapshot.json"));

        var snapshot = JsonSerializer.Deserialize<ExecutionSnapshot>(legacy, CanonicalProfile.Options)!;

        Assert.Null(snapshot.ExecutionMode);
        Assert.Null(snapshot.WorkItemId);
    }

    [Fact]
    public void RoundTrip_DispatcherChild_PreservesBothFields()
    {
        var child = Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-9" };

        var round = JsonSerializer.Deserialize<ExecutionSnapshot>(CanonicalProfile.SerializeCanonical(child), CanonicalProfile.Options)!;

        Assert.Equal(ExecutionMode.Dispatcher, round.ExecutionMode);
        Assert.Equal("TICKET-9", round.WorkItemId);
    }

    /// <summary>The failure this change exists to fix: two children of one engagement were indistinguishable.</summary>
    [Fact]
    public void TwoChildrenOfOneEngagement_AreDistinguishableByWorkItem()
    {
        var first = Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-1", RunId = "run-1" };
        var second = Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-2", RunId = "run-2" };

        Assert.NotEqual(CanonicalProfile.SerializeCanonical(first), CanonicalProfile.SerializeCanonical(second));
        Assert.NotEqual(first.WorkItemId, second.WorkItemId);
    }

    /// <summary>
    /// The router and its children share a mode, because a child is handed its parent's pinned
    /// definition unaltered (ADR-PA25). The work item is what separates them — which is exactly
    /// why the mode is stored rather than reduced to a boolean "is dispatcher" flag.
    /// </summary>
    [Fact]
    public void Router_AndItsChild_ShareAModeAndDifferByWorkItem()
    {
        var router = Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher };
        var child = Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-1" };

        Assert.Equal(router.ExecutionMode, child.ExecutionMode);
        Assert.Null(router.WorkItemId);
        Assert.NotNull(child.WorkItemId);
    }

    [Fact]
    public void Validate_DispatcherChild_DoesNotThrow() =>
        (Snapshot() with { ExecutionMode = ExecutionMode.Dispatcher, WorkItemId = "TICKET-1" }).Validate();

    /// <summary>
    /// The CLR name is part of the contract, not only the wire name: the consumer binds this
    /// property by reflection, looking for a name that says which <em>mode</em> a run is in. `Mode`
    /// alone — the spelling the definition contract uses — does not say that at the boundary that
    /// reads the projection, where `execution_mode` is the established vocabulary. Renamed before
    /// v0.27.0 was tagged, so nothing had ever shipped under the old spelling.
    /// </summary>
    [Fact]
    public void ExecutionSnapshot_HasAModePropertyNamedForTheConceptItCarries()
    {
        var property = typeof(ExecutionSnapshot).GetProperty(nameof(ExecutionSnapshot.ExecutionMode));

        Assert.NotNull(property);
        Assert.Contains("ExecutionMode", property.Name, StringComparison.Ordinal);
        Assert.Equal(typeof(ExecutionMode), property.PropertyType);
    }

    private static JsonObject Json(ExecutionSnapshot snapshot) =>
        JsonNode.Parse(CanonicalProfile.SerializeCanonical(snapshot))!.AsObject();

    private static ExecutionSnapshot Snapshot() => new()
    {
        ExecutionId = "run-token-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 0,
        Status = ExecutionStatus.Running,
        Artifacts = new Dictionary<string, ArtifactStatus>(),
        CompletedSteps = [],
        Decisions = [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(),
        CheckpointedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
