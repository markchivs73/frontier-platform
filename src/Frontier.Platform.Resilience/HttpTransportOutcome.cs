using Polly;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Which HTTP transport outcomes count toward a server's breaker (ADR-PA48, doc 10 §3). One taxonomy decides: a thrown
/// exception counts when <see cref="IFailureClassifier"/> calls it retryable, and a returned response counts when its
/// status is one <see cref="FailureClassifier.IsTransientHttpStatus"/> lists. A cancellation the caller asked for is not
/// the server's failure and never counts.
/// </summary>
internal static class HttpTransportOutcome
{
    /// <summary>Whether <paramref name="outcome"/> is a server failure; <paramref name="callerToken"/> is the token the breaker saw the call under.</summary>
    internal static bool IsFailure(Outcome<HttpResponseMessage> outcome, IFailureClassifier classifier, CancellationToken callerToken) => outcome switch
    {
        { Exception: OperationCanceledException } when callerToken.IsCancellationRequested => false,
        { Exception: { } exception } => classifier.Classify(exception).Class.IsRetryable,
        { Result: { } response } => FailureClassifier.IsTransientHttpStatus(response.StatusCode),
        _ => false,
    };
}
