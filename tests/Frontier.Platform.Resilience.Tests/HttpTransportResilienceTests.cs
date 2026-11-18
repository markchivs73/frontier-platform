using System.Net;
using Frontier.Platform.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>
/// ADR-PA48 (consumer S13.246) tests for the HTTP transport handler: one attempt, a breaker and a bulkhead per server,
/// timeouts, and outcomes the classifier names. Every request goes to a <see cref="StubHttpHandler"/>, never the network.
/// </summary>
public sealed class HttpTransportResilienceTests
{
    private const string Client = "mcp-runtime";

    private readonly CircuitStateProvider circuits = new(TimeProvider.System);
    private readonly FailureClassifier classifier = new();
    private readonly ResiliencePolicyProvider policies;
    private readonly HttpResilienceHandlerFactory factory;

    public HttpTransportResilienceTests()
    {
        policies = new ResiliencePolicyProvider(classifier, circuits, TimeProvider.System);
        factory = new HttpResilienceHandlerFactory(policies);
    }

    [Fact]
    public void CreateHandler_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.CreateHandler("does-not-exist", Client));
    }

    /// <summary>A node profile, which retries, is refused at wiring: the HTTP layer never runs a retry (ADR-PA48).</summary>
    [Theory]
    [InlineData("mcp-read")]
    [InlineData("none")]
    public void CreateHandler_ANodeProfile_IsRefused(string profileName)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => factory.CreateHandler(profileName, Client));

        Assert.Contains("HTTP transport profile", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>ADR-PA48: a transport profile is not a node profile, so every node-facing lookup refuses it.</summary>
    [Theory]
    [InlineData("mcp-transport")]
    [InlineData("a2a-transport")]
    public void NodeFacingLookups_RefuseATransportProfile(string profileName)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => policies.GetPipeline(profileName));
        Assert.Throws<ArgumentOutOfRangeException>(() => policies.GetPipeline(profileName, "anthropic", "claude-fable-5"));
        Assert.Throws<ArgumentOutOfRangeException>(() => policies.GetTaskOptions(profileName));
        Assert.False(Phase1ResilienceProfileCatalogue.ByProfileId.ContainsKey(profileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateHandler_BlankClientName_Throws(string clientName)
    {
        Assert.Throws<ArgumentException>(() => factory.CreateHandler("mcp-transport", clientName));
    }

    [Fact]
    public void CreateHandler_NullClientName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => factory.CreateHandler("mcp-transport", null!));
    }

    /// <summary>A transient status is handed back to the caller after one send; nothing retries it.</summary>
    [Fact]
    public async Task SendAsync_TransientStatus_IsReturnedAfterOneSend()
    {
        var stub = StubHttpHandler.Responding(HttpStatusCode.ServiceUnavailable);
        using var client = ClientOver(stub);

        using var response = await client.GetAsync(new Uri("https://a.example/mcp"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, stub.Sends);
    }

    /// <summary>A transient exception propagates after one send; nothing retries it.</summary>
    [Fact]
    public async Task SendAsync_TransientException_PropagatesAfterOneSend()
    {
        var stub = new StubHttpHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var client = ClientOver(stub);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://a.example/mcp")));

        Assert.Equal(1, stub.Sends);
    }

    /// <summary>
    /// mcp-read's breaker (ratio 0.5, minimum 10) opens after ten returned 503s, each of which reached the caller; the
    /// eleventh request is refused before it is sent, with an exception the classifier calls transient <c>circuit_open</c>.
    /// </summary>
    [Fact]
    public async Task SendAsync_TenReturnedTransientStatuses_OpenTheBreaker()
    {
        var stub = StubHttpHandler.Responding(HttpStatusCode.ServiceUnavailable);
        using var client = ClientOver(stub);

        await SendAsync(client, "https://a.example/mcp", times: 10);
        var refused = await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(new Uri("https://a.example/mcp")));

        Assert.Equal(10, stub.Sends);
        Assert.Equal(CircuitState.Open, circuits.GetState(Client, "a.example"));
        AssertTransient(refused, "circuit_open");
    }

    /// <summary>Consumer S13.301: a returned 504 or 408 is transient, so ten of them open the breaker like ten 503s.</summary>
    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task SendAsync_TenReturned504sOr408s_OpenTheBreaker(HttpStatusCode status)
    {
        var stub = StubHttpHandler.Responding(status);
        using var client = ClientOver(stub);

        await SendAsync(client, "https://a.example/mcp", times: 10);

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(new Uri("https://a.example/mcp")));
        Assert.Equal(10, stub.Sends);
    }

    /// <summary>
    /// Consumer S13.300: a real <see cref="SocketsHttpHandler"/> whose <c>ConnectCallback</c> refuses by policy. Each refusal
    /// reaches the caller after one connect attempt, permanent <c>policy_refused</c>, and twenty of them leave the server's
    /// breaker closed — before this, ten opened it. No socket is opened: the callback refuses before connecting.
    /// </summary>
    [Fact]
    public async Task SendAsync_TwentyPolicyRefusals_AreNotRetriedAndLeaveTheBreakerClosed()
    {
        var connects = 0;
        using var sockets = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => throw new NetworkPolicyRefusedException($"refused by policy ({Interlocked.Increment(ref connects)})"),
        };
        using var client = ClientOver(sockets);

        for (var i = 0; i < 20; i++)
        {
            var refused = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://refused.example/mcp")));
            AssertClassified(refused, FailureClass.Permanent, "policy_refused");
        }

        Assert.Equal(20, connects);
        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "refused.example"));
    }

    [Fact]
    public void IsFailure_PolicyRefusal_DoesNotCount()
    {
        var wrapped = new HttpRequestException("refused", new NetworkPolicyRefusedException("refused"));

        Assert.False(HttpTransportOutcome.IsFailure(Outcome.FromException<HttpResponseMessage>(wrapped), classifier, CancellationToken.None));
    }

    /// <summary>A permanent status is the caller's to handle and does not count toward the breaker.</summary>
    [Fact]
    public async Task SendAsync_PermanentStatuses_DoNotOpenTheBreaker()
    {
        var stub = StubHttpHandler.Responding(HttpStatusCode.BadRequest);
        using var client = ClientOver(stub);

        await SendAsync(client, "https://a.example/mcp", times: 11);

        Assert.Equal(11, stub.Sends);
        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "a.example"));
    }

    /// <summary>Each server has its own breaker: one that is failing does not stop requests to another.</summary>
    [Fact]
    public async Task SendAsync_AnOpenBreaker_DoesNotStopAnotherAuthority()
    {
        var stub = new StubHttpHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host == "a.example" ? HttpStatusCode.BadGateway : HttpStatusCode.OK)));
        using var client = ClientOver(stub);
        await SendAsync(client, "https://a.example/mcp", times: 10);

        using var other = await client.GetAsync(new Uri("https://b.example/mcp"));

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(CircuitState.Open, circuits.GetState(Client, "a.example"));
        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "b.example"));
    }

    /// <summary>The breaker's transitions reach the circuit-state stream as (client name, authority).</summary>
    [Fact]
    public async Task SendAsync_BreakerOpening_PublishesTheTransitionUnderTheClientAndAuthority()
    {
        var transitions = new List<CircuitTransition>();
        using var subscription = circuits.Subscribe(transitions.Add);
        using var client = ClientOver(StubHttpHandler.Responding(HttpStatusCode.ServiceUnavailable));

        await SendAsync(client, "https://a.example:8443/mcp", times: 10);

        var opened = Assert.Single(transitions);
        Assert.Equal((Client, "a.example:8443", CircuitState.Closed, CircuitState.Open), (opened.Provider, opened.ModelId, opened.From, opened.To));
    }

    /// <summary>The synchronous send runs through the same pipeline, so it cannot bypass the breaker or retry.</summary>
    [Fact]
    public void Send_Synchronous_CountsTowardTheSameBreaker()
    {
        var stub = StubHttpHandler.Responding(HttpStatusCode.ServiceUnavailable);
        using var client = ClientOver(stub);

        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://a.example/mcp"));
            using var response = client.Send(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        using var refusedRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("https://a.example/mcp"));
        Assert.Throws<BrokenCircuitException>(() => client.Send(refusedRequest));
        Assert.Equal(10, stub.Sends);
    }

    /// <summary>Handlers share pipelines: a fresh handler (an IHttpClientFactory rotation) sees the breaker the last one opened.</summary>
    [Fact]
    public async Task CreateHandler_ANewHandler_SharesTheServersBreaker()
    {
        using (var first = ClientOver(StubHttpHandler.Responding(HttpStatusCode.ServiceUnavailable)))
        {
            await SendAsync(first, "https://a.example/mcp", times: 10);
        }

        using var second = ClientOver(StubHttpHandler.Responding(HttpStatusCode.OK));

        await Assert.ThrowsAsync<BrokenCircuitException>(() => second.GetAsync(new Uri("https://a.example/mcp")));
    }

    /// <summary>A full bulkhead refuses the request with an exception the classifier calls transient <c>bulkhead_rejected</c>.</summary>
    [Fact]
    public async Task HttpPipeline_FullBulkhead_RejectsAsBulkheadRejected()
    {
        var pipeline = policies.BuildHttpPipeline(TransportProfile(timeoutMs: 5_000, maxConcurrent: 1), Client, "a.example");
        var release = new TaskCompletionSource<HttpResponseMessage>();
        var holding = pipeline.ExecuteAsync(async _ => await release.Task, CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<RateLimiterRejectedException>(async () =>
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None));

        AssertTransient(rejected, "bulkhead_rejected");
        using var held = new HttpResponseMessage(HttpStatusCode.OK);
        release.SetResult(held);
        Assert.Same(held, await holding);
    }

    /// <summary>One pipeline, so one breaker and one bulkhead, per (profile, client, authority), kept across calls.</summary>
    [Fact]
    public void GetHttpPipeline_KeepsOnePipelinePerProfileClientAndAuthority()
    {
        var first = policies.GetHttpPipeline("mcp-transport", Client, "a.example");

        Assert.Same(first, policies.GetHttpPipeline("mcp-transport", Client, "a.example"));
        Assert.NotSame(first, policies.GetHttpPipeline("mcp-transport", Client, "b.example"));
        Assert.NotSame(first, policies.GetHttpPipeline("mcp-transport", "a2a-agent", "a.example"));
        Assert.NotSame(first, policies.GetHttpPipeline("a2a-transport", Client, "a.example"));
    }

    /// <summary>An attempt that outlives the per-attempt timeout ends as a timeout the classifier calls transient, and counts.</summary>
    [Fact]
    public async Task HttpPipeline_SlowServer_TimesOutAsTransientNetwork()
    {
        var pipeline = policies.BuildHttpPipeline(TransportProfile(timeoutMs: 50, maxConcurrent: 4), Client, "slow.example");

        var timedOut = await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await pipeline.ExecuteAsync(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }, CancellationToken.None));

        AssertTransient(timedOut, "network");
    }

    /// <summary>The bulkhead sits outside the breaker (doc 10 §4), so saturation rejections never open it.</summary>
    [Fact]
    public async Task HttpPipeline_BulkheadRejections_LeaveTheBreakerClosed()
    {
        var pipeline = policies.BuildHttpPipeline(TransportProfile(timeoutMs: 5_000, maxConcurrent: 1), Client, "busy.example");
        var release = new TaskCompletionSource<HttpResponseMessage>();
        var holding = pipeline.ExecuteAsync(async _ => await release.Task, CancellationToken.None);

        for (var i = 0; i < 20; i++)
        {
            await Assert.ThrowsAsync<RateLimiterRejectedException>(async () =>
                await pipeline.ExecuteAsync(_ => ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None));
        }

        using var held = new HttpResponseMessage(HttpStatusCode.OK);
        release.SetResult(held);
        await holding;
        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "busy.example"));
    }

    /// <summary>A request waiting in the bulkhead's queue is bounded by the total timeout (3x per attempt), not left waiting for a permit.</summary>
    [Fact]
    public async Task HttpPipeline_QueuedRequest_IsBoundedByTheTotalTimeout()
    {
        var profile = TransportProfile(timeoutMs: 50, maxConcurrent: 1) with { Bulkhead = new BulkheadSpec { Scope = "authority", MaxConcurrent = 1, MaxQueue = 1 } };
        var pipeline = policies.BuildHttpPipeline(profile, Client, "queued.example");
        var release = new TaskCompletionSource<HttpResponseMessage>();
        var holding = pipeline.ExecuteAsync(async _ => await release.Task, CancellationToken.None);
        var sent = false;

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await pipeline.ExecuteAsync(_ =>
            {
                sent = true;
                return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }, CancellationToken.None));

        Assert.False(sent);
        using var held = new HttpResponseMessage(HttpStatusCode.OK);
        release.SetResult(held);
        await holding;
    }

    /// <summary>Ten attempt timeouts open mcp-read's breaker (minimum 10, ratio 0.5): a timed-out attempt is a server failure.</summary>
    [Fact]
    public async Task HttpPipeline_AttemptTimeouts_CountTowardTheBreaker()
    {
        var pipeline = policies.BuildHttpPipeline(TransportProfile(timeoutMs: 20, maxConcurrent: 4), Client, "slow.example");

        for (var i = 0; i < 10; i++)
        {
            await Assert.ThrowsAsync<TimeoutRejectedException>(async () => await pipeline.ExecuteAsync(SlowAsync, CancellationToken.None));
        }

        Assert.Equal(CircuitState.Open, circuits.GetState(Client, "slow.example"));
        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await pipeline.ExecuteAsync(SlowAsync, CancellationToken.None));
    }

    /// <summary>Concurrent first requests to one server build one pipeline and track one breaker.</summary>
    [Fact]
    public async Task GetHttpPipeline_ConcurrentFirstUse_BuildsOnePipelineAndTracksOneBreaker()
    {
        using var start = new ManualResetEventSlim();
        var racers = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return policies.GetHttpPipeline("mcp-transport", Client, "race.example");
        })).ToArray();

        start.Set();
        var pipelines = await Task.WhenAll(racers);

        Assert.Single(pipelines.Distinct());
        Assert.Equal(1, circuits.TrackedCount(Client, "race.example"));
    }

    /// <summary>
    /// Why the README tells consumers to set <c>HttpClient.Timeout</c> to infinite: HttpClient's own timeout cancels the
    /// token the handler was given, so it reaches the breaker as the caller's cancellation and never counts.
    /// </summary>
    [Fact]
    public async Task SendAsync_HttpClientTimeout_DoesNotCountTowardTheBreaker()
    {
        var stub = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = ClientOver(stub);
        client.Timeout = TimeSpan.FromMilliseconds(10);

        for (var i = 0; i < 10; i++)
        {
            await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync(new Uri("https://a.example/mcp")));
        }

        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "a.example"));
    }

    [Fact]
    public void AuthorityOf_AbsoluteUri_IsHostAndNonDefaultPort()
    {
        using var defaultPort = new HttpRequestMessage(HttpMethod.Get, new Uri("https://A.Example:443/mcp"));
        using var otherPort = new HttpRequestMessage(HttpMethod.Get, new Uri("http://a.example:8080/mcp"));

        Assert.Equal("a.example", ResilienceHttpHandler.AuthorityOf(defaultPort));
        Assert.Equal("a.example:8080", ResilienceHttpHandler.AuthorityOf(otherPort));
    }

    [Fact]
    public void AuthorityOf_RelativeOrMissingUri_IsRefused()
    {
        using var relative = new HttpRequestMessage(HttpMethod.Get, new Uri("/mcp", UriKind.Relative));
        using var missing = new HttpRequestMessage();

        Assert.Throws<InvalidOperationException>(() => ResilienceHttpHandler.AuthorityOf(relative));
        Assert.Throws<InvalidOperationException>(() => ResilienceHttpHandler.AuthorityOf(missing));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData((HttpStatusCode)529, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotImplemented, false)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported, false)]
    public void IsTransientHttpStatus_MatchesDoc10Row2And504And408(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, FailureClassifier.IsTransientHttpStatus(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsFailure_ReturnedResponse_CountsOnlyATransientStatus(HttpStatusCode status, bool expected)
    {
        using var response = new HttpResponseMessage(status);

        Assert.Equal(expected, HttpTransportOutcome.IsFailure(Outcome.FromResult(response), classifier, CancellationToken.None));
    }

    [Fact]
    public void IsFailure_Exception_CountsOnlyARetryableOne()
    {
        Assert.True(HttpTransportOutcome.IsFailure(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused")), classifier, CancellationToken.None));
        Assert.False(HttpTransportOutcome.IsFailure(Outcome.FromException<HttpResponseMessage>(new InvalidOperationException()), classifier, CancellationToken.None));
    }

    /// <summary>A cancellation the caller asked for is not the server's failure; one the caller did not ask for (a connect timeout) is.</summary>
    [Fact]
    public void IsFailure_Cancellation_CountsOnlyWhenTheCallerDidNotCancel()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var outcome = Outcome.FromException<HttpResponseMessage>(new TaskCanceledException());

        Assert.False(HttpTransportOutcome.IsFailure(outcome, classifier, cancelled.Token));
        Assert.True(HttpTransportOutcome.IsFailure(outcome, classifier, CancellationToken.None));
    }

    [Fact]
    public void IsFailure_NullResult_DoesNotCount()
    {
        Assert.False(HttpTransportOutcome.IsFailure(Outcome.FromResult<HttpResponseMessage>(null!), classifier, CancellationToken.None));
    }

    /// <summary>The caller's own cancellation reaches the breaker as a cancellation and does not count toward it.</summary>
    [Fact]
    public async Task SendAsync_CallerCancels_DoesNotCountTowardTheBreaker()
    {
        var stub = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = ClientOver(stub);

        for (var i = 0; i < 10; i++)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(new Uri("https://a.example/mcp"), cancel.Token));
        }

        Assert.Equal(CircuitState.Closed, circuits.GetState(Client, "a.example"));
    }

    private HttpClient ClientOver(HttpMessageHandler stub)
    {
        var handler = factory.CreateHandler("mcp-transport", Client);
        handler.InnerHandler = stub;
        return new HttpClient(handler);
    }

    private static async ValueTask<HttpResponseMessage> SlowAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static async Task SendAsync(HttpClient client, string uri, int times)
    {
        for (var i = 0; i < times; i++)
        {
            using var response = await client.GetAsync(new Uri(uri));
        }
    }

    private void AssertTransient(Exception exception, string reasonCode) => AssertClassified(exception, FailureClass.Transient, reasonCode);

    private void AssertClassified(Exception exception, FailureClass expected, string reasonCode)
    {
        var classification = classifier.Classify(exception);
        Assert.Equal(expected, classification.Class);
        Assert.Equal(reasonCode, classification.ReasonCode);
    }

    /// <summary>mcp-transport's shape with a test timeout and a small bulkhead with no queue.</summary>
    private static ResilienceProfile TransportProfile(int timeoutMs, int maxConcurrent) =>
        Phase1ResilienceProfileCatalogue.McpTransport with
        {
            TimeoutMs = timeoutMs,
            Bulkhead = new BulkheadSpec { Scope = "authority", MaxConcurrent = maxConcurrent, MaxQueue = 0 },
        };
}
