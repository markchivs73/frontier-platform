using Polly;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Sends each request through the HTTP transport pipeline of its URI's authority (ADR-PA48). It holds no pipeline state
/// itself: <see cref="ResiliencePolicyProvider.GetHttpPipeline"/> keeps one per (profile, client, authority), so every
/// handler instance for a client shares each server's breaker and bulkhead.
/// </summary>
internal sealed class ResilienceHttpHandler(ResiliencePolicyProvider policies, string profileName, string clientName) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        PipelineFor(request).ExecuteAsync(
            static (state, token) => new ValueTask<HttpResponseMessage>(state.Handler.SendInnerAsync(state.Request, token)),
            (Handler: this, Request: request),
            cancellationToken).AsTask();

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        PipelineFor(request).Execute(
            static (state, token) => state.Handler.SendInner(state.Request, token),
            (Handler: this, Request: request),
            cancellationToken);

    /// <summary>The pipeline for <paramref name="request"/>'s server.</summary>
    internal ResiliencePipeline<HttpResponseMessage> PipelineFor(HttpRequestMessage request) =>
        policies.GetHttpPipeline(profileName, clientName, AuthorityOf(request));

    /// <summary>The next handler's asynchronous send, called from inside the pipeline.</summary>
    internal Task<HttpResponseMessage> SendInnerAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        base.SendAsync(request, cancellationToken);

    /// <summary>The next handler's synchronous send, called from inside the pipeline.</summary>
    internal HttpResponseMessage SendInner(HttpRequestMessage request, CancellationToken cancellationToken) =>
        base.Send(request, cancellationToken);

    /// <summary>
    /// The server a request goes to: its absolute URI's authority (host, and the port when not the scheme's default).
    /// <c>HttpClient</c> resolves a relative URI against its base address before any handler runs, so a request without
    /// an absolute URI here was sent around the client and is refused.
    /// </summary>
    internal static string AuthorityOf(HttpRequestMessage request) =>
        request.RequestUri is { IsAbsoluteUri: true } uri
            ? uri.Authority
            : throw new InvalidOperationException("An HTTP transport request needs an absolute request URI to choose its server's pipeline (ADR-PA48).");
}
