using Xunit;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// S13.349 (AUTHN-1) and S13.334 item 3: "local" is the parsed host, never a string prefix. The consumer's
/// <c>CredentialProfileCheckTests.IsLocalEndpoint_ReadsTheParsedHost</c> rows are copied here, so collapsing the
/// consumer's rule onto this one is a known no-op apart from the rows marked stricter.
/// </summary>
public sealed class LocalEndpointTests
{
    [Theory]
    // The rows the old prefix rule accepted.
    [InlineData("https://localhost:8081", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("https://127.0.0.1:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("http://LOCALHOST:8081", true)]
    // Loopback addresses the prefix rule missed.
    [InlineData("https://[::1]:8081", true)]
    [InlineData("https://127.4.5.6:8081", true)]
    [InlineData("https://127.0.0.2", true)]
    [InlineData("https://[::ffff:127.0.0.1]:8081", true)]
    [InlineData("https://0x7f.1:8081", true)]
    [InlineData("https://2130706433:8081", true)]
    // Lookalikes the prefix rule wrongly accepted.
    [InlineData("https://localhost.frontier.example", false)]
    [InlineData("https://localhost.evil.com", false)]
    [InlineData("HTTPS://LOCALHOST.evil.example", false)]
    [InlineData("https://localhostfrontier.documents.azure.com", false)]
    [InlineData("https://127.0.0.1.nip.io:8081", false)]
    [InlineData("https://localhost:8081@evil.example", false)]
    [InlineData("https://127.0.0.1@evil.example", false)]
    // Not loopback, or not an http(s) endpoint.
    [InlineData("https://frontier.documents.azure.com:443", false)]
    [InlineData("https://frontier.documents.azure.com/localhost", false)]
    [InlineData("https://0.0.0.0:8081", false)]
    [InlineData("localhost:8081", false)]
    [InlineData("file:///x", false)]
    [InlineData("/x", false)]
    [InlineData("ftp://localhost/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    // System.Uri rewrites the host "loopback" to "localhost", so it is the host the client dials, and local.
    [InlineData("http://loopback", true)]
    // Stricter than Uri.IsLoopback, which the consumer's rule uses today.
    [InlineData("http://localhost.", false)]
    public void IsLoopback_ReadsTheParsedHost(string? endpoint, bool expected) =>
        Assert.Equal(expected, LocalEndpoint.IsLoopback(endpoint));

    /// <summary>The numeric forms are accepted because the parser normalises them to 127.0.0.1, the host the client dials.</summary>
    [Theory]
    [InlineData("https://0x7f.1:8081")]
    [InlineData("https://2130706433:8081")]
    public void IsLoopback_NumericForms_AreTheHostTheClientDials(string endpoint) =>
        Assert.Equal("127.0.0.1", new Uri(endpoint).Host);
}
