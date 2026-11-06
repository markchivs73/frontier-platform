using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// S13.149: a run ended through the governed terminate says who ended it, why and when. The three
/// members are additive under the ADR-E15 floor, so these tests pin the claims that rests on —
/// omitted when null, every snapshot golden written before them reads them as null and re-serializes
/// byte-identically, never <c>required</c> — and <see cref="ExecutionTerminationRule"/>.
/// </summary>
public sealed class ExecutionTerminationTests
{
    private static readonly string[] TerminationKeys = ["terminated_by", "termination_reason", "terminated_at_utc"];

    [Fact]
    public void Serialize_NotTerminated_OmitsTheKeys()
    {
        var json = Json(Serialization.ContractSamples.ExecutionSnapshotPausedOnFailure());

        Assert.All(TerminationKeys, key => Assert.False(json.ContainsKey(key)));
    }

    [Fact]
    public void Serialize_Terminated_WritesTheStatusTokenAndAppendsTheMembersLast()
    {
        var json = Json(Serialization.ContractSamples.ExecutionSnapshotTerminated());

        Assert.Equal("terminated", (string?)json["status"]);
        Assert.Equal(TerminationKeys, json.Select(pair => pair.Key).TakeLast(3));
        Assert.Equal("2026-01-01T02:00:00.000Z", (string?)json["terminated_at_utc"]);
    }

    /// <summary>ADR-E15's floor, over every current-schema snapshot golden written before the members existed.</summary>
    [Theory]
    [InlineData("execution_snapshot.json")]
    [InlineData("execution_snapshot_context_pin.json")]
    [InlineData("execution_snapshot_contract_hashes.json")]
    [InlineData("execution_snapshot_dispatcher_child.json")]
    [InlineData("execution_snapshot_failure_detail.json")]
    [InlineData("execution_snapshot_host_build.json")]
    [InlineData("execution_snapshot_initiated.json")]
    [InlineData("execution_snapshot_mcp_step.json")]
    [InlineData("execution_snapshot_paused_on_failure.json")]
    [InlineData("execution_snapshot_skipped.json")]
    public void RoundTrip_GoldenWrittenBeforeTheMembers_ReadsNullAndReserializesUnchanged(string golden)
    {
        var legacy = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", golden));

        var snapshot = JsonSerializer.Deserialize<ExecutionSnapshot>(legacy, CanonicalProfile.Options)!;

        Assert.Null(snapshot.TerminatedBy);
        Assert.Null(snapshot.TerminationReason);
        Assert.Null(snapshot.TerminatedAtUtc);
        Assert.Equal(legacy, CanonicalProfile.SerializeCanonical(snapshot));
    }

    [Theory]
    [InlineData(nameof(ExecutionSnapshot.TerminatedBy))]
    [InlineData(nameof(ExecutionSnapshot.TerminationReason))]
    [InlineData(nameof(ExecutionSnapshot.TerminatedAtUtc))]
    public void TerminationMembers_AreNotRequired(string property) =>
        Assert.Empty(typeof(ExecutionSnapshot).GetProperty(property)!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));

    [Fact]
    public void Validate_TerminatedWithActorReasonAndTime_DoesNotThrow() =>
        Serialization.ContractSamples.ExecutionSnapshotTerminated().Validate();

    /// <summary>A machine-originated terminate names an explicit <c>system:*</c> actor (ADR-E8), and that validates.</summary>
    [Fact]
    public void Validate_TerminatedByASystemActor_DoesNotThrow() =>
        (Serialization.ContractSamples.ExecutionSnapshotTerminated() with { TerminatedBy = "system:recovery" }).Validate();

    [Fact]
    public void Validate_TerminatedWithNoneOfThem_NamesAllThree()
    {
        var snapshot = Serialization.ContractSamples.ExecutionSnapshotTerminated() with { TerminatedBy = null, TerminationReason = null, TerminatedAtUtc = null };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Equal(
            [
                "terminated_by is required when status is terminated.",
                "termination_reason is required when status is terminated.",
                "terminated_at_utc is required when status is terminated.",
            ],
            exception.Violations);
    }

    [Fact]
    public void Validate_TerminatedWithBlankActorAndReason_NamesThem()
    {
        var snapshot = Serialization.ContractSamples.ExecutionSnapshotTerminated() with { TerminatedBy = " ", TerminationReason = "" };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Equal(["terminated_by is required when status is terminated.", "termination_reason is required when status is terminated."], exception.Violations);
    }

    /// <summary>Only a terminated snapshot is checked: the members are optional everywhere else.</summary>
    [Fact]
    public void Validate_NotTerminatedWithoutTheMembers_DoesNotThrow() =>
        Serialization.ContractSamples.ExecutionSnapshot().Validate();

    private static JsonObject Json(ExecutionSnapshot snapshot) =>
        JsonNode.Parse(CanonicalProfile.SerializeCanonical(snapshot))!.AsObject();
}
