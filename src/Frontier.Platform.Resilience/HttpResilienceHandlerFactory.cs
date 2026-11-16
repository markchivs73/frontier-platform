namespace Frontier.Platform.Resilience;

/// <summary>
/// <see cref="IHttpResilienceHandlerFactory"/> over <see cref="ResiliencePolicyProvider"/>'s cached HTTP pipelines. The
/// profile is checked here, when the client is built, so a profile that is not an HTTP transport profile fails at wiring
/// rather than at the first request.
/// </summary>
internal sealed class HttpResilienceHandlerFactory(ResiliencePolicyProvider policies) : IHttpResilienceHandlerFactory
{
    /// <inheritdoc />
    public DelegatingHandler CreateHandler(string profileName, string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _ = ResiliencePolicyProvider.ResolveHttpTransportProfile(profileName);
        return new ResilienceHttpHandler(policies, profileName, clientName);
    }
}
