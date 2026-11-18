using System.Net;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4 tests for <see cref="FailureClassifier"/> against doc 10 §3's taxonomy table.</summary>
public sealed class FailureClassifierTests
{
    private readonly FailureClassifier classifier = new();

    [Fact]
    public void Classify_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
    }

    [Fact]
    public void Classify_ContractViolationException_IsPermanentContractViolation()
    {
        var result = classifier.Classify(new ContractViolationException("ScopeSection", ["bad"]));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("contract_violation", result.ReasonCode);
    }

    [Fact]
    public void Classify_BudgetExceededException_IsPermanentGuardrail()
    {
        var result = classifier.Classify(new BudgetExceededException("advisory-sow-default", "invocation token budget exhausted"));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("guardrail", result.ReasonCode);
    }

    [Fact]
    public void Classify_GuardrailsSuspendedException_IsPermanentGuardrail()
    {
        var result = classifier.Classify(new GuardrailsSuspendedException());

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("guardrail", result.ReasonCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)529)]
    public void Classify_HttpProviderUnavailableStatus_IsTransientProviderUnavailable(HttpStatusCode statusCode)
    {
        var result = classifier.Classify(new HttpRequestException("boom", null, statusCode));

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("provider_unavailable", result.ReasonCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public void Classify_HttpRequestInvalidStatus_IsPermanentRequestInvalid(HttpStatusCode statusCode)
    {
        var result = classifier.Classify(new HttpRequestException("boom", null, statusCode));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("request_invalid", result.ReasonCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_HttpAuthStatus_IsPermanentAuth(HttpStatusCode statusCode)
    {
        var result = classifier.Classify(new HttpRequestException("boom", null, statusCode));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("auth", result.ReasonCode);
    }

    [Fact]
    public void Classify_HttpRequestExceptionWithNoStatusCode_IsTransientNetwork()
    {
        var result = classifier.Classify(new HttpRequestException("boom"));

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    /// <summary>Consumer S13.300: a network policy's refusal thrown bare is permanent <c>policy_refused</c>.</summary>
    [Fact]
    public void Classify_NetworkPolicyRefusedException_IsPermanentPolicyRefused()
    {
        var result = classifier.Classify(new NetworkPolicyRefusedException("10.0.0.1 is private"));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("policy_refused", result.ReasonCode);
    }

    /// <summary>
    /// Consumer S13.300: <c>SocketsHttpHandler</c> wraps a <c>ConnectCallback</c>'s exception in an
    /// <see cref="HttpRequestException"/> with no status code, which alone would be transient <c>network</c>; the refusal
    /// inside makes it permanent <c>policy_refused</c>.
    /// </summary>
    [Fact]
    public void Classify_HttpRequestExceptionWrappingAPolicyRefusal_IsPermanentPolicyRefused()
    {
        var wrapped = new HttpRequestException(HttpRequestError.ConnectionError, "refused (a.example:443)", new NetworkPolicyRefusedException("refused"));

        var result = classifier.Classify(wrapped);

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("policy_refused", result.ReasonCode);
    }

    /// <summary>The classifier looks one level inside the wrapper, no deeper: a refusal inside the consumer's own exception is not recognised.</summary>
    [Fact]
    public void Classify_PolicyRefusalTwoLevelsDown_IsTransientNetwork()
    {
        var ownWrapper = new InvalidOperationException("card fetch failed", new NetworkPolicyRefusedException("refused"));

        var result = classifier.Classify(new HttpRequestException("refused", ownWrapper));

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    /// <summary>An ordinary connection failure, whose inner exception is not a refusal, stays transient <c>network</c>.</summary>
    [Fact]
    public void Classify_HttpRequestExceptionWrappingASocketError_IsTransientNetwork()
    {
        var wrapped = new HttpRequestException(HttpRequestError.ConnectionError, "connection refused", new System.Net.Sockets.SocketException(61));

        var result = classifier.Classify(wrapped);

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    [Fact]
    public void Classify_HttpRequestExceptionWithUnmappedStatusCode_IsPermanentUnclassified()
    {
        var result = classifier.Classify(new HttpRequestException("boom", null, HttpStatusCode.NotFound));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("unclassified", result.ReasonCode);
    }

    [Fact]
    public void Classify_CosmosTooManyRequests_IsDeferredStorageThrottled()
    {
        var result = classifier.Classify(new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, "activity-1", 0));

        Assert.Equal(FailureClass.Deferred, result.Class);
        Assert.Equal("storage_throttled", result.ReasonCode);
    }

    [Fact]
    public void Classify_CosmosServiceUnavailable_IsTransientStorageUnavailable()
    {
        var result = classifier.Classify(new CosmosException("unavailable", HttpStatusCode.ServiceUnavailable, 0, "activity-1", 0));

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("storage_unavailable", result.ReasonCode);
    }

    [Fact]
    public void Classify_CosmosUnmappedStatus_IsPermanentUnclassified()
    {
        var result = classifier.Classify(new CosmosException("conflict", HttpStatusCode.Conflict, 0, "activity-1", 0));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("unclassified", result.ReasonCode);
    }

    [Fact]
    public void Classify_CosmosPreconditionFailed_IsPermanentUnclassified()
    {
        var result = classifier.Classify(new CosmosException("etag stale", HttpStatusCode.PreconditionFailed, 0, "activity-1", 0));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("unclassified", result.ReasonCode);
    }

    [Fact]
    public void Classify_BrokenCircuitException_IsTransientCircuitOpen()
    {
        var result = classifier.Classify(new BrokenCircuitException());

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("circuit_open", result.ReasonCode);
    }

    [Fact]
    public void Classify_TimeoutRejectedException_IsTransientNetwork()
    {
        var result = classifier.Classify(new TimeoutRejectedException());

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    [Fact]
    public void Classify_RateLimiterRejectedException_IsTransientBulkheadRejected()
    {
        var result = classifier.Classify(new RateLimiterRejectedException());

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("bulkhead_rejected", result.ReasonCode);
    }

    [Fact]
    public void Classify_TaskCanceledException_IsTransientNetwork()
    {
        var result = classifier.Classify(new TaskCanceledException());

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    [Fact]
    public void Classify_TimeoutException_IsTransientNetwork()
    {
        var result = classifier.Classify(new TimeoutException());

        Assert.Equal(FailureClass.Transient, result.Class);
        Assert.Equal("network", result.ReasonCode);
    }

    [Fact]
    public void Classify_UnmappedException_IsPermanentUnclassified()
    {
        var result = classifier.Classify(new InvalidOperationException("unmapped"));

        Assert.Equal(FailureClass.Permanent, result.Class);
        Assert.Equal("unclassified", result.ReasonCode);
    }
}
