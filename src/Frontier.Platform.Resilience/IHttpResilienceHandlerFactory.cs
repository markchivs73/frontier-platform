namespace Frontier.Platform.Resilience;

/// <summary>
/// Puts an HTTP client's requests through a transport resilience profile (ADR-PA48): a per-attempt and a total timeout,
/// a circuit breaker and a bulkhead for each server the client talks to, and no retry. Retrying belongs to the loops
/// above the call (the agent loop and the activity retry), because the HTTP layer cannot tell a write from a read.
/// </summary>
public interface IHttpResilienceHandlerFactory
{
    /// <summary>
    /// A new handler that runs every request through <paramref name="profileName"/>'s pipeline for the request URI's
    /// authority. Pipelines are kept by the factory, not the handler, so a handler an <c>IHttpClientFactory</c> rotates
    /// does not reset any breaker. Each server's breaker is reported through <see cref="ICircuitStateProvider"/> with
    /// <paramref name="clientName"/> as the provider and the authority as the model id. An open breaker surfaces as
    /// <c>BrokenCircuitException</c> and a full bulkhead as <c>RateLimiterRejectedException</c>, which
    /// <see cref="IFailureClassifier"/> classifies transient (<c>circuit_open</c>, <c>bulkhead_rejected</c>); a transient
    /// status the server returns is handed back to the caller and only counts toward the breaker.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="profileName"/> is not an HTTP transport profile
    /// (<see cref="Phase1ResilienceProfileCatalogue.HttpTransportByProfileId"/>); node profiles are refused.</exception>
    DelegatingHandler CreateHandler(string profileName, string clientName);
}
