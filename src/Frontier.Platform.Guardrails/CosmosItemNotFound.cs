using System.Net;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Tells a missing item from a missing container. Both are a Cosmos 404; only substatus 0 is an absent item.
/// A missing container (substatus 1003) must surface as a failure, never as "no record", so a switch thrown
/// into an unprovisioned container cannot read as "not thrown" (the consumer's S13.140 lesson).
/// </summary>
internal static class CosmosItemNotFound
{
    /// <summary>Whether a Cosmos failure with <paramref name="statusCode"/> and <paramref name="subStatusCode"/> means only that the item does not exist.</summary>
    internal static bool IsMissingItem(HttpStatusCode statusCode, int subStatusCode) =>
        statusCode == HttpStatusCode.NotFound && subStatusCode == 0;
}
