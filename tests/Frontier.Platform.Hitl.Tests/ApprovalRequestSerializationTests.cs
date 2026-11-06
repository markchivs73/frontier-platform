using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>
/// S13.149: an approval whose run was terminated is <c>withdrawn</c>, with who and why. The members are
/// additive, so these tests pin what that rests on: a request without them is byte-identical to the
/// bytes written before they existed, those bytes read back and re-serialize unchanged, and the new
/// members come after every existing one and are never <c>required</c>.
/// </summary>
public sealed class ApprovalRequestSerializationTests
{
    /// <summary>
    /// The compatibility claim in its strongest form: <c>approval_request_pending.json</c> was written by
    /// the code before this change, and the unchanged pending request still serializes to it.
    /// </summary>
    [Fact]
    public void Serialize_PendingRequest_IsByteIdenticalToThePreChangeGolden() =>
        Assert.Equal(Golden("approval_request_pending.json"), CanonicalProfile.SerializeCanonical(HitlFixtures.PendingRequest()));

    [Fact]
    public void RoundTrip_PreChangeBytes_ReadsNullAndReserializesUnchanged()
    {
        var legacy = Golden("approval_request_pending.json");

        var request = JsonSerializer.Deserialize<ApprovalRequest>(legacy, CanonicalProfile.Options)!;

        Assert.Null(request.WithdrawnBy);
        Assert.Null(request.WithdrawalReason);
        Assert.Equal(legacy, CanonicalProfile.SerializeCanonical(request));
    }

    [Fact]
    public void Serialize_WithdrawnRequest_MatchesItsGoldenAndRoundTrips()
    {
        var bytes = CanonicalProfile.SerializeCanonical(Withdrawn());

        Assert.Equal(Golden("approval_request_withdrawn.json"), bytes);
        Assert.Equal(bytes, CanonicalProfile.SerializeCanonical(JsonSerializer.Deserialize<ApprovalRequest>(bytes, CanonicalProfile.Options)!));
    }

    [Fact]
    public void Serialize_WithdrawnRequest_WritesTheStatusTokenAndAppendsTheMembersAfterTtl()
    {
        var json = JsonNode.Parse(CanonicalProfile.SerializeCanonical(Withdrawn()))!.AsObject();
        var keys = json.Select(pair => pair.Key).ToList();

        Assert.Equal("withdrawn", (string?)json["status"]);
        Assert.Equal(["ttl", "withdrawn_by", "withdrawal_reason"], keys.TakeLast(3));
    }

    [Theory]
    [InlineData(nameof(ApprovalRequest.WithdrawnBy))]
    [InlineData(nameof(ApprovalRequest.WithdrawalReason))]
    public void WithdrawalMembers_AreNotRequired(string property) =>
        Assert.Empty(typeof(ApprovalRequest).GetProperty(property)!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));

    private static ApprovalRequest Withdrawn() => HitlFixtures.PendingRequest() with
    {
        Status = ApprovalRequestStatus.Withdrawn,
        WithdrawnBy = "user:manager-1",
        WithdrawalReason = "Parked on a failure that will not be fixed; restarting from a new run.",
    };

    private static byte[] Golden(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", name));
}
