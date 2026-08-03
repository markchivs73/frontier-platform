using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Abstractions.Tests.Serialization;

/// <summary>
/// Golden-file suite for the platform kernel's own contracts (S11.2, ADR-PA2): every
/// kernel <see cref="IVersionedContract"/> serializes to byte-identical canonical bytes
/// across cultures, matches its committed golden file, and round-trips without change.
/// The golden files moved here byte-identical from the subsystem suite when their types
/// entered the kernel — the wire never changes for a type move.
/// </summary>
public sealed class ContractGoldenFileTests
{
    [Fact]
    public void ContextRequest_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(SampleContextRequest(), "context_request.json");

    /// <summary>A well-formed <see cref="ContextRequest"/>, including the real-time tier (mirrors the subsystem sample it moved from).</summary>
    internal static ContextRequest SampleContextRequest() => new()
    {
        EngagementId = "eng-1",
        AgentRole = "analyst",
        BaselineComponents = ["firm-standards", "playbooks"],
        DynamicFields = ["timeline", "stakeholders"],
        RequiresRealTime = true,
        RealTimeSources = ["crm-feed"],
    };
}
