using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// S13.120: a paused-on-failure snapshot says <em>which</em> failure paused it, not only what
/// kind. The member is additive under the ADR-E15 floor, so these tests pin the claims that
/// rests on — omitted when null, byte-identical to the pre-change golden, read as null from
/// bytes that predate it, never <c>required</c> — and the detail's own validation.
/// </summary>
public sealed class ExecutionFailureDetailTests
{
    [Fact]
    public void Serialize_NoDetail_OmitsTheKey()
    {
        var json = Json(Snapshot());

        Assert.False(json.ContainsKey("failure_detail"));
    }

    /// <summary>The compatibility claim in its strongest form: the unchanged paused-on-failure sample still matches the golden written before the member existed.</summary>
    [Fact]
    public void Serialize_NoDetail_IsByteIdenticalToThePreChangeGolden()
    {
        var bytes = CanonicalProfile.SerializeCanonical(Serialization.ContractSamples.ExecutionSnapshotPausedOnFailure());

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "execution_snapshot_paused_on_failure.json");
    }

    [Fact]
    public void Serialize_WithDetail_WritesTheThreeFieldsInOrder()
    {
        var json = Json(Snapshot() with { FailureDetail = Detail() });

        var detail = Assert.IsType<JsonObject>(json["failure_detail"]);
        Assert.Equal(["node_id", "exception_type", "message"], detail.Select(pair => pair.Key));
        Assert.Equal("commit", (string?)detail["node_id"]);
        Assert.Equal("System.InvalidOperationException", (string?)detail["exception_type"]);
        Assert.Equal("MCP server 'io.frontier.gen/repository' declares no remotes[0].url.", (string?)detail["message"]);
    }

    /// <summary>ADR-E15's floor: bytes recorded before the member existed read back as null, never as an empty detail.</summary>
    [Fact]
    public void Deserialize_BytesRecordedBeforeTheMemberExisted_ReadsNull()
    {
        var legacy = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "execution_snapshot_paused_on_failure.json"));

        var snapshot = JsonSerializer.Deserialize<ExecutionSnapshot>(legacy, CanonicalProfile.Options)!;

        Assert.Null(snapshot.FailureDetail);
        Assert.Equal("contract_violation", snapshot.FailureClassification);
    }

    [Fact]
    public void RoundTrip_WithDetail_PreservesIt()
    {
        var snapshot = Snapshot() with { FailureDetail = Detail() };

        var round = JsonSerializer.Deserialize<ExecutionSnapshot>(CanonicalProfile.SerializeCanonical(snapshot), CanonicalProfile.Options)!;

        Assert.Equal(Detail(), round.FailureDetail);
    }

    [Fact]
    public void FailureDetail_IsNotRequired() =>
        Assert.Empty(typeof(ExecutionSnapshot).GetProperty(nameof(ExecutionSnapshot.FailureDetail))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));

    [Fact]
    public void Validate_WellFormedDetail_DoesNotThrow() =>
        (Snapshot() with { FailureDetail = Detail() }).Validate();

    [Fact]
    public void Validate_MessageAtTheBound_DoesNotThrow() =>
        (Snapshot() with { FailureDetail = Detail() with { Message = new string('m', ExecutionFailureDetail.MaxMessageLength) } }).Validate();

    [Fact]
    public void Validate_BlankNodeId_Throws()
    {
        var snapshot = Snapshot() with { FailureDetail = Detail() with { NodeId = " " } };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("failure_detail.node_id must not be blank.", exception.Violations);
    }

    [Fact]
    public void Validate_BlankExceptionType_Throws()
    {
        var snapshot = Snapshot() with { FailureDetail = Detail() with { ExceptionType = "" } };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("failure_detail.exception_type must not be blank.", exception.Violations);
    }

    [Fact]
    public void Validate_MessageOverTheBound_Throws()
    {
        var snapshot = Snapshot() with { FailureDetail = Detail() with { Message = new string('m', ExecutionFailureDetail.MaxMessageLength + 1) } };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("failure_detail.message must be at most 512 characters.", exception.Violations);
    }

    private static JsonObject Json(ExecutionSnapshot snapshot) =>
        JsonNode.Parse(CanonicalProfile.SerializeCanonical(snapshot))!.AsObject();

    private static ExecutionFailureDetail Detail() => new()
    {
        NodeId = "commit",
        ExceptionType = "System.InvalidOperationException",
        Message = "MCP server 'io.frontier.gen/repository' declares no remotes[0].url.",
    };

    private static ExecutionSnapshot Snapshot() => new()
    {
        ExecutionId = "run-token-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 2,
        Status = ExecutionStatus.PausedOnFailure,
        CurrentNodeId = "commit",
        FailureClassification = "unclassified",
        Artifacts = new Dictionary<string, ArtifactStatus>(),
        CompletedSteps = [],
        Decisions = [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(),
        CheckpointedAtUtc = new DateTime(2026, 10, 21, 21, 11, 43, DateTimeKind.Utc),
    };
}
