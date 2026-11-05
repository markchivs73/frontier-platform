using System.Text.Json.Serialization;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The stored shape of the fleet kill switch (doc 07 §5a, doc 02 §3): one document in the consumer's
/// <c>platform-config</c> container (partition key <c>/config_type</c>), config type <see cref="ConfigTypeValue"/>,
/// id <see cref="FleetId"/>. Replaced whole on every activate and resume; a missing document means the switch
/// has never been thrown.
/// </summary>
internal sealed record KillSwitchDocument
{
    /// <summary>The partition (<c>config_type</c>) the kill switch lives in.</summary>
    internal const string ConfigTypeValue = "kill-switch";

    /// <summary>The fleet-scope document's id; a per-engagement scope would take its own id (doc 07 §2, a later task).</summary>
    internal const string FleetId = ConfigTypeValue + ":fleet";

    /// <summary>The document id, always <see cref="FleetId"/> in Phase 1.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public string Id { get; init; } = FleetId;

    /// <summary>The partition key, always <see cref="ConfigTypeValue"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("config_type")]
    public string ConfigType { get; init; } = ConfigTypeValue;

    /// <summary>Whether the switch is thrown: <see langword="true"/> denies every new admission.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    /// <summary>The operator's reason for throwing the switch; absent once it is resumed.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>When the switch last changed, UTC.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("changed_at")]
    public required DateTime ChangedAt { get; init; }

    /// <summary>The document for a switch thrown at <paramref name="changedAt"/> for <paramref name="reason"/>.</summary>
    internal static KillSwitchDocument Activated(string reason, DateTime changedAt) => new() { Active = true, Reason = reason, ChangedAt = changedAt };

    /// <summary>The document for a switch resumed at <paramref name="changedAt"/>.</summary>
    internal static KillSwitchDocument Resumed(DateTime changedAt) => new() { Active = false, ChangedAt = changedAt };

    /// <summary>Whether <paramref name="document"/> denies admission; no document means the switch was never thrown.</summary>
    internal static bool IsActive(KillSwitchDocument? document) => document?.Active == true;
}
