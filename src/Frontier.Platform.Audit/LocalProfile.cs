namespace Frontier.Platform.Audit;

/// <summary>
/// Whether this process is running against local emulators (ADR-SEC3's "local-emulator profile").
///
/// <para>
/// The discriminator is the configured data-plane endpoint, not an environment-variable name:
/// endpoints are what actually differ between a laptop and a deployment, and an
/// <c>ASPNETCORE_ENVIRONMENT</c> a deployment can set to <c>Development</c> would let the dev
/// signing key through the very check that exists to stop it. An <em>absent</em> endpoint is
/// deliberately not local — this fails towards "deployed", so a misconfiguration refuses to boot
/// rather than quietly signing evidence with a committed key.
/// </para>
/// </summary>
internal static class LocalProfile
{
    /// <summary>Whether <paramref name="endpoint"/> addresses a loopback emulator.</summary>
    internal static bool IsLocalEndpoint(string? endpoint) =>
        endpoint is not null &&
        (endpoint.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
         endpoint.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
         endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase));
}
