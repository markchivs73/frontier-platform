using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.120: what the paused checkpoint's <see cref="ExecutionFailureDetail"/> is built from, and
/// what it must never carry — a stack trace, an unbounded message, or a credential.
/// </summary>
public sealed class FailureDetailSummaryTests
{
    [Fact]
    public void Describe_InBodyException_TakesItsTypeAndMessageOnly()
    {
        var exception = new InvalidOperationException("MCP server 'io.frontier.gen/repository' declares no remotes[0].url.", new ArgumentException("inner is not read"));

        var detail = FailureDetailSummary.Describe("commit", exception);

        Assert.Equal("commit", detail.NodeId);
        Assert.Equal("System.InvalidOperationException", detail.ExceptionType);
        Assert.Equal("MCP server 'io.frontier.gen/repository' declares no remotes[0].url.", detail.Message);
    }

    /// <summary>
    /// An activity failure arrives as <see cref="TaskFailedException"/>; its own type and message
    /// ("Task 'X' failed with an unhandled exception") say nothing about the step. The details chain
    /// does: the head is the activity's exception — the level the classification reads first — and
    /// the inner failures follow an arrow.
    /// </summary>
    [Fact]
    public void Describe_TaskFailedException_ReadsTheDetailsChainHeadFirst()
    {
        var inner = new TaskFailureDetails("System.Net.Http.HttpRequestException", "Response status code does not indicate success: 401 (Unauthorized).", "   at stack", null, null);
        var head = new TaskFailureDetails(typeof(ContractViolationException).FullName!, "ResearchProposal: reply failed validation.", "   at stack", inner, null);

        var detail = FailureDetailSummary.Describe("compose", new TaskFailedException("AgentTaskActivity", 1, head));

        Assert.Equal("compose", detail.NodeId);
        Assert.Equal(typeof(ContractViolationException).FullName, detail.ExceptionType);
        Assert.Equal("ResearchProposal: reply failed validation. → Response status code does not indicate success: 401 (Unauthorized).", detail.Message);
        Assert.DoesNotContain("at stack", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TaskFailedExceptionWithoutInnerFailure_TakesTheHeadMessageAlone()
    {
        var head = new TaskFailureDetails(typeof(BudgetExceededException).FullName!, "invocation budget exhausted.", null, null, null);

        var detail = FailureDetailSummary.Describe("b-ticket", new TaskFailedException("AgentTaskActivity", 1, head));

        Assert.Equal("invocation budget exhausted.", detail.Message);
    }

    [Fact]
    public void Bound_MessageWithinTheLimit_IsUnchanged()
    {
        var message = new string('m', ExecutionFailureDetail.MaxMessageLength);

        Assert.Same(message, FailureDetailSummary.Bound(message));
    }

    [Fact]
    public void Bound_MessageOverTheLimit_IsCutToTheLimitAndEndsInAnEllipsis()
    {
        var message = new string('m', ExecutionFailureDetail.MaxMessageLength + 1);

        var bounded = FailureDetailSummary.Bound(message);

        Assert.Equal(ExecutionFailureDetail.MaxMessageLength, bounded.Length);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("401 from https://agent.example/a2a with Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.payload.sig", "401 from https://agent.example/a2a with Authorization: Bearer [redacted]")]
    [InlineData("header was 'Basic dXNlcjpwYXNz' and was refused", "header was 'Basic [redacted]' and was refused")]
    [InlineData("GET https://acct.blob.core.windows.net/c/b?sv=2024-05-04&sig=AbC%2F123%3D&sp=r failed", "GET https://acct.blob.core.windows.net/c/b?sv=2024-05-04&sig=[redacted]&sp=r failed")]
    [InlineData("connection string AccountName=x;key=abc==;EndpointSuffix=core", "connection string AccountName=x;key=[redacted];EndpointSuffix=core")]
    [InlineData("api_key=sk-live-123 was rejected; api-key=sk2 too; apikey=sk3 too", "api_key=[redacted] was rejected; api-key=[redacted] too; apikey=[redacted] too")]
    [InlineData("access_token=tok1 token=tok2 secret=s1 client_secret=cs password=pw pwd=p", "access_token=[redacted] token=[redacted] secret=[redacted] client_secret=[redacted] password=[redacted] pwd=[redacted]")]
    [InlineData("BEARER ABCDEFGH1 and SIG=xyz are matched regardless of case", "BEARER [redacted] and SIG=[redacted] are matched regardless of case")]
    [InlineData("token ghp_16C7e42F292c6912E7710c838347Ae178B4a was rejected after 'Bearer ghp_16C7e42F292c6912E7710c838347Ae178B4a'", "token ghp_16C7e42F292c6912E7710c838347Ae178B4a was rejected after 'Bearer [redacted]'")]
    public void Redact_ReplacesCredentialShapedValuesAndKeepsTheirNames(string message, string expected) =>
        Assert.Equal(expected, FailureDetailSummary.Redact(message));

    /// <summary>Prose that happens to contain the scheme words is not a credential: the value after them has to look like a token.</summary>
    [Theory]
    [InlineData("ScopeSection: objectives must not be empty.")]
    [InlineData("The key 'record_state' holds 'original_image', which is never accepted.")]
    [InlineData("the bearer of this message is a monkey")]
    [InlineData("The Bearer certificate was presented, but Basic authentication is not supported.")]
    public void Redact_LeavesAnOrdinaryMessageAlone(string message) =>
        Assert.Equal(message, FailureDetailSummary.Redact(message));

    /// <summary>
    /// Redaction runs before the bound: cutting first would leave the head of a token that is then
    /// too short to be recognised, so the ordering is pinned by a credential that straddles the cut.
    /// </summary>
    [Fact]
    public void Describe_RedactsBeforeBounding_SoACredentialAtTheCutNeverSurvives()
    {
        var message = new string('x', ExecutionFailureDetail.MaxMessageLength - 12) + " Bearer eyJ" + new string('t', 200);

        var detail = FailureDetailSummary.Describe("n", new InvalidOperationException(message));

        Assert.Equal(ExecutionFailureDetail.MaxMessageLength, detail.Message.Length);
        Assert.DoesNotContain("eyJ", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tttt", detail.Message, StringComparison.Ordinal);
        // 500 + " Bearer [redacted]" is 518, so the cut lands inside the placeholder — bounding
        // first would have left "eyJ…", three characters too short to be recognised as a token.
        Assert.EndsWith(" Bearer [re…", detail.Message, StringComparison.Ordinal);
    }
}
