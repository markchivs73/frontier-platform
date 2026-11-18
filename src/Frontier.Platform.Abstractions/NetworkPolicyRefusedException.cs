namespace Frontier.Platform.Abstractions;

/// <summary>
/// Thrown by a consumer's connect-time network policy (a <c>SocketsHttpHandler.ConnectCallback</c> guard, for example)
/// when it refuses the destination a request would connect to. Per ADR-PA48's 2026-11-17 amendment this is a permanent
/// failure with reason code <c>policy_refused</c>: the same request is refused again on every attempt, and the server it
/// names was never reached, so Resilience never retries it and no breaker counts it. Throw this type itself, not a
/// wrapper: <c>SocketsHttpHandler</c> wraps a callback's exception in an <c>HttpRequestException</c>
/// with no status code, and the classifier looks one level inside that wrapper, no further.
/// </summary>
public sealed class NetworkPolicyRefusedException : Exception
{
    /// <summary>Creates an empty exception (CA1032); prefer the message constructor, naming the destination and the rule.</summary>
    public NetworkPolicyRefusedException()
    {
    }

    /// <summary>Creates the exception with a message naming the refused destination and the rule that refused it.</summary>
    public NetworkPolicyRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception (CA1032).</summary>
    public NetworkPolicyRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
