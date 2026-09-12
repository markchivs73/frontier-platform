using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// The ADR-CR1 refresh signal: the Sense layer detected that an engagement's dynamic context has
/// changed and raises this onto the engagement's live orchestration instance (doc 04 §8, doc 16 §4's
/// refresh-class branch, doc 18 §3).
/// <para>
/// <b>One wire contract for one event.</b> It lives in the platform kernel because both halves of
/// the seam use it: the consumer's ingest surface emits it, <c>GraphOrchestrator</c> consumes it.
/// A second consumer-side shape for the same event would be two contracts for one wire — divergent
/// bytes, divergent hashes, and nothing to fail when they drift apart (ADR-PA2/K10, ADR-PA24).
/// </para>
/// <para>
/// <b>The name is <c>DynamicContextRefreshRequired</c>.</b> Doc 04 §8's code block raises
/// <c>DynamicContextRefreshNeeded</c> while constructing a <c>DynamicContextRefreshRequired</c>
/// payload on the next line; the prose above it, doc 16 §4 and doc 18 §3 all say <c>Required</c>.
/// Event names are contracts, so the three consistent sources win.
/// </para>
/// </summary>
public sealed record DynamicContextRefreshRequired : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    /// The engagement whose context changed. This is what routes the signal to a live instance
    /// (doc 16 §4 step 3: the engagement must already exist; the refresh branch never creates one).
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>
    /// Why the refresh was raised, e.g. <c>"CRM_pricing_changed"</c>. ADR-CR1 makes this a required
    /// property of every refresh — an unreasoned refresh is an unattributable cache invalidation.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>The engagement fields the Sense layer saw change, for audit and observability.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("changed_fields")]
    public required IReadOnlyList<string> ChangedFields { get; init; }

    /// <summary>When the change was detected, ISO-8601 UTC with milliseconds under the canonical profile.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("detected_at_utc")]
    public required DateTime DetectedAtUtc { get; init; }

    /// <summary>
    /// The dynamic-context components this refresh should re-render, under their <b>snake_case</b>
    /// document keys (<c>engagement_profile</c>, <c>client_profile</c>) — the keys of the dynamic
    /// document the refresh merges into, not doc 18 §1's kebab-case tier-table spelling. Optional
    /// and omit-null: a signal naming no components omits the key entirely.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("components")]
    public IReadOnlyList<string>? Components { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(EngagementId))
        {
            violations.Add("engagement_id must not be blank; an unrouted refresh signal cannot reach an instance.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            violations.Add("reason must not be blank (ADR-CR1: every refresh carries an explicit reason).");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(DynamicContextRefreshRequired), violations);
        }
    }
}
