namespace Frontier.Platform.Serialization;

/// <summary>
/// The one rule the platform uses to decide that an endpoint addresses a local emulator (consumer S13.349, S13.334
/// item 3; ADR-SEC3's local-emulator profile). The audit signing-profile check and every Cosmos client factory read
/// it: it decides whether the committed dev signing key may sign and whether TLS validation is relaxed for the
/// emulator's self-signed certificate. It reads the <em>parsed</em> host, so a lookalike name
/// (<c>localhost.evil.example</c>, <c>localhostfrontier.documents.azure.com</c>, <c>127.0.0.1.nip.io</c>) or userinfo
/// (<c>https://localhost@evil.example</c>, whose host is <c>evil.example</c>) is not local. An absent endpoint is not
/// local either, so a misconfiguration fails towards "deployed".
/// </summary>
public static class LocalEndpoint
{
    /// <summary>
    /// Whether <paramref name="endpoint"/> is an absolute <c>http</c> or <c>https</c> URI whose host is exactly
    /// <c>localhost</c> (any case) or a loopback IP address, as <see cref="Uri"/> parses it, which is the host a client
    /// built from the same string dials (the parser rewrites <c>loopback</c> to <c>localhost</c>, and <c>0x7f.1</c> to
    /// <c>127.0.0.1</c>). A trailing-dot <c>localhost.</c> is not accepted, which is stricter than <see cref="Uri.IsLoopback"/>.
    /// </summary>
    public static bool IsLoopback(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && IsLoopbackHost(uri);

    /// <summary>An IP host is judged by the parsed address; a name only by an exact <c>localhost</c>.</summary>
    private static bool IsLoopbackHost(Uri uri) =>
        uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? uri.IsLoopback
            : string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
