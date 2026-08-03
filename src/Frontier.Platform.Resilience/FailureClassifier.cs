using System.Net;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Frontier.Platform.Resilience;

/// <summary>
/// <see cref="IFailureClassifier"/> implementing doc 10 §3's mapping table. Rows for
/// exception types that don't exist yet (<c>GuardrailsSuspendedException</c> — the
/// kill switch, deferred to S6.5; <c>RateDeferredException</c> — Guardrails' rate
/// limiter, also deferred to S6.5) are not implemented; they will be added when those
/// types land. Provider 429-with-<c>Retry-After</c> (Deferred/<c>provider_rate_limited</c>)
/// is not distinguishable from a plain <see cref="HttpRequestException"/>, which
/// carries no response headers — that row is wired once S4.2's provider client
/// surfaces a richer exception.
/// </summary>
internal sealed class FailureClassifier : IFailureClassifier
{
    /// <inheritdoc />
    public FailureClassification Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ContractViolationException => Permanent("contract_violation"),
            BudgetExceededException => Permanent("guardrail"),
            HttpRequestException http => ClassifyHttp(http),
            CosmosException cosmos => ClassifyCosmos(cosmos),
            BrokenCircuitException => Transient("circuit_open"),
            TimeoutRejectedException => Transient("network"),
            RateLimiterRejectedException => Transient("bulkhead_rejected"),
            TaskCanceledException => Transient("network"),
            TimeoutException => Transient("network"),
            _ => Permanent("unclassified"),
        };
    }

    /// <summary>Provider HTTP failures (doc 10 §3 rows 1-2, 4-5): classified by status code; no response headers survive into <see cref="HttpRequestException"/>, so 429 always falls to <c>provider_unavailable</c>.</summary>
    internal static FailureClassification ClassifyHttp(HttpRequestException exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or (HttpStatusCode)529 => Transient("provider_unavailable"),
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => Permanent("request_invalid"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Permanent("auth"),
            null => Transient("network"),
            _ => Permanent("unclassified"),
        };

    /// <summary>Cosmos SDK failures (doc 10 §3 rows 10-11): 429 carries the SDK-supplied <see cref="CosmosException.RetryAfter"/>.</summary>
    internal static FailureClassification ClassifyCosmos(CosmosException exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => Deferred("storage_throttled", exception.RetryAfter),
            HttpStatusCode.ServiceUnavailable => Transient("storage_unavailable"),
            _ => Permanent("unclassified"),
        };

    /// <summary>Builds a <see cref="FailureClass.Transient"/> classification with no known retry delay.</summary>
    internal static FailureClassification Transient(string reasonCode) => new()
    {
        Class = FailureClass.Transient,
        ReasonCode = reasonCode,
    };

    /// <summary>Builds a <see cref="FailureClass.Deferred"/> classification carrying the provider/SDK-stated retry delay.</summary>
    internal static FailureClassification Deferred(string reasonCode, TimeSpan? retryAfter) => new()
    {
        Class = FailureClass.Deferred,
        RetryAfter = retryAfter,
        ReasonCode = reasonCode,
    };

    /// <summary>Builds a <see cref="FailureClass.Permanent"/> classification — never retried.</summary>
    internal static FailureClassification Permanent(string reasonCode) => new()
    {
        Class = FailureClass.Permanent,
        ReasonCode = reasonCode,
    };
}
