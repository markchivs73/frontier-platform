using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Recognises a connect-time network policy refusal (ADR-PA48, amended 2026-11-17): a
/// <see cref="NetworkPolicyRefusedException"/> thrown bare, or as the direct inner exception of the
/// <see cref="HttpRequestException"/> <c>SocketsHttpHandler</c> wraps a <c>ConnectCallback</c>'s exception in. The walk
/// stops at that one level on purpose: it is the only wrapping the BCL applies, and a deeper walk would let any
/// exception that happened to carry a refusal somewhere in its chain skip retry.
/// </summary>
internal static class PolicyRefusal
{
    /// <summary>The reason code a refusal classifies under.</summary>
    internal const string ReasonCode = "policy_refused";

    /// <summary>Whether <paramref name="exception"/> is a policy refusal, bare or as an <see cref="HttpRequestException"/>'s inner exception.</summary>
    internal static bool IsRefusal(Exception exception) =>
        exception is NetworkPolicyRefusedException or HttpRequestException { InnerException: NetworkPolicyRefusedException };
}
