using System.Net;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>The innermost handler in HTTP transport tests: answers from a delegate and counts the requests that reached it, so no test touches the network.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int sends;

    /// <summary>How many requests reached this handler.</summary>
    public int Sends => Volatile.Read(ref sends);

    /// <summary>A stub that answers every request with <paramref name="status"/>.</summary>
    public static StubHttpHandler Responding(HttpStatusCode status) => new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref sends);
        return respond(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();
}
