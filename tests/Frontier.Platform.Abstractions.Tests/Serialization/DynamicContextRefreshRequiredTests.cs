using System.Text;
using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Abstractions.Tests.Serialization;

/// <summary>
/// S13.62 — the ADR-CR1 refresh signal as a canonical contract.
/// <para>
/// The Sense layer raises this event onto a live orchestration instance (doc 04 §8, doc 16 §4's
/// refresh-class branch, doc 18 §3). It crosses a serialization boundary — it is written by the
/// ingest surface, carried through DTF history, and replayed — so it is an
/// <see cref="IVersionedContract"/> under the canonical profile like any other: omit-null,
/// snake_case wire names, explicit gapless <c>[JsonPropertyOrder]</c>, ISO-8601-UTC-ms dates,
/// invariant culture (hard invariant 1).
/// </para>
/// <para>
/// <b>The event name is <c>DynamicContextRefreshRequired</c>.</b> Doc 04 §8's code block raises
/// <c>DynamicContextRefreshNeeded</c> while constructing a <c>DynamicContextRefreshRequired</c>
/// payload in the same expression — the two cannot both be right, and the doc is wrong. The
/// prose above that block, doc 16 §4's refresh-class list and doc 18 §3's wiring diagram all say
/// <c>Required</c>. Event names are contracts; this suite binds the name the three consistent
/// sources agree on. <c>GraphRefreshSignalWalkTests</c> binds the same string on the
/// orchestrator's side of the seam.
/// </para>
/// <para>
/// <b>This is the single wire contract for this event.</b> It lives in the platform kernel and
/// both halves of the seam use it: the consumer's ingest surface emits it, the orchestrator
/// consumes it. A second, consumer-side shape for the same event would be two contracts for one
/// wire — divergent bytes, divergent hashes, and nothing to fail when they drift apart. So the
/// field set below must carry everything the emitter needs: engagement id, reason, changed
/// fields, detected-at UTC, and the changed components.
/// </para>
/// <para>
/// <b>Field set (decided 2026-09-12, owner).</b> Doc 04 §8's raise carries engagement id, reason,
/// changed fields and detected-at. <c>components</c> joins them on v1.0 as an <b>optional,
/// omit-null</b> field — doc 18 §3 sketches the signal carrying it and the consumer emitter sends
/// it, and settling it now costs one golden regeneration rather than a later additive amendment
/// plus a second golden. Component names in that list are snake_case
/// (<c>engagement_profile</c>, <c>client_profile</c>), matching the dynamic document's own keys
/// rather than doc 18 §1's kebab-case tier table.
/// </para>
/// </summary>
public sealed class DynamicContextRefreshRequiredTests
{
    /// <summary>
    /// The contract's canonical bytes are stable across cultures, match the committed golden
    /// file, and round-trip unchanged — the three properties definition hashing, provider cache
    /// hits and audit signing all rest on (canonical-serialization skill).
    /// </summary>
    [Fact]
    public void DynamicContextRefreshRequired_SerializesStablyAndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Sample(), "dynamic_context_refresh_required.json");

    /// <summary>
    /// <c>detected_at_utc</c> is an instant in an evidential record, so it serializes as
    /// ISO-8601 UTC with milliseconds regardless of the <see cref="DateTimeKind"/> or local
    /// offset the producer happened to hold it in. A signal detected at one instant must hash
    /// identically whichever machine ingested it.
    /// </summary>
    [Fact]
    public void DetectedAtUtc_SerializesAsIso8601UtcMilliseconds()
    {
        var wire = Encoding.UTF8.GetString(ContractRoundTripAssertions.AssertByteStableAcrossCultures(Sample()));

        Assert.Contains("\"detected_at_utc\":\"2026-01-01T00:00:00.000Z\"", wire, StringComparison.Ordinal);
    }

    /// <summary>
    /// The components the signal names are carried under snake_case names, matching the keys of
    /// the dynamic-context document the refresh will merge into. Doc 18 §1's tier table spells
    /// the same components in kebab-case; the wire follows hard invariant 1, not the table.
    /// </summary>
    [Fact]
    public void Components_AreCarriedAsSnakeCaseComponentKeys()
    {
        var wire = Encoding.UTF8.GetString(ContractRoundTripAssertions.AssertByteStableAcrossCultures(Sample()));

        Assert.Contains("\"components\":[\"engagement_profile\",\"client_profile\"]", wire, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>components</c> is optional: a signal that names no components omits the key entirely
    /// rather than emitting <c>null</c> or an empty array. Omit-null is what keeps the bytes —
    /// and therefore the hash — identical for signals that genuinely carry the same information.
    /// </summary>
    [Fact]
    public void Components_WhenNotSupplied_IsOmittedFromTheWire()
    {
        var signal = Sample() with { Components = null };

        var wire = Encoding.UTF8.GetString(ContractRoundTripAssertions.AssertByteStableAcrossCultures(signal));

        Assert.DoesNotContain("components", wire, StringComparison.Ordinal);
    }

    /// <summary>A signal carrying no components still round-trips — the optional field must not be load-bearing for deserialization.</summary>
    [Fact]
    public void Components_WhenOmitted_StillRoundTrips()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(Sample() with { Components = null });

        ContractRoundTripAssertions.AssertRoundTrips<DynamicContextRefreshRequired>(bytes);
    }

    /// <summary>
    /// The engagement id is what routes the signal to a live instance (doc 16 §4 step 3: the
    /// engagement must already exist; resolution never creates one on this branch). A signal
    /// that cannot name its engagement is unroutable, so it is refused at the contract boundary
    /// rather than dead-lettered later.
    /// </summary>
    [Fact]
    public void Validate_BlankEngagementId_Throws()
    {
        var signal = Sample() with { EngagementId = "   " };

        Assert.Throws<ContractViolationException>(signal.Validate);
    }

    /// <summary>
    /// ADR-CR1 makes the reason a required property of every refresh — "each refresh carries a
    /// <c>Reason</c> for audit and observability", and <c>DynamicContextRefresher</c> already
    /// tags its OTEL counter with it. An unreasoned refresh is an unattributable cache
    /// invalidation, so the contract refuses one.
    /// </summary>
    [Fact]
    public void Validate_BlankReason_Throws()
    {
        var signal = Sample() with { Reason = "" };

        Assert.Throws<ContractViolationException>(signal.Validate);
    }

    /// <summary>A well-formed signal is accepted — the guards above must be rules, not a blanket refusal.</summary>
    [Fact]
    public void Validate_WellFormedSignal_DoesNotThrow() => Sample().Validate();

    /// <summary>A signal naming no components is still well-formed: <c>components</c> is optional, so validation must not require it.</summary>
    [Fact]
    public void Validate_WithoutComponents_DoesNotThrow() => (Sample() with { Components = null }).Validate();

    /// <summary>
    /// Doc 04 §8's own raise — engagement, reason, the two changed fields, the detection instant
    /// — plus the two doc 18 §3 dynamic components the enrichment path names.
    /// </summary>
    internal static DynamicContextRefreshRequired Sample() => new()
    {
        EngagementId = "ENGAGEMENT-12345",
        Reason = "CRM_pricing_changed",
        ChangedFields = ["commercial_terms", "client_budget"],
        DetectedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Components = ["engagement_profile", "client_profile"],
    };
}
