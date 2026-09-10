using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>
/// Invariant 3's two keys, tested once. This suite consolidates two identical copies that had grown
/// in <c>Audit.Tests</c> and <c>Workflow.Orchestration.Tests</c> — the same split that produced the
/// duplicate helpers they covered.
/// </summary>
public sealed class ExecutionIdTests
{
    [Fact]
    public void Mint_JoinsTheSegmentsWithTheSeparator()
    {
        Assert.Equal("eng-1::wf-1", ExecutionId.Mint("eng-1", "wf-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mint_WithAMissingSegment_Throws(string? engagementId)
    {
        // ThrowsAny: null yields ArgumentNullException, blank yields ArgumentException.
        var ex = Assert.ThrowsAny<ArgumentException>(() => ExecutionId.Mint(engagementId!, "wf-1"));

        Assert.Equal("engagementId", ex.ParamName);
    }

    /// <summary>
    /// A workflow id containing the separator would move the boundary of the affinity key, so two
    /// different engagement-workflows could claim the same key.
    /// </summary>
    [Fact]
    public void Mint_WithAWorkflowIdContainingTheSeparator_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.Mint("eng-1", "wf::1"));

        Assert.Equal("workflowId", ex.ParamName);
        Assert.Contains("separator", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The engagement id is composite by design (doc 16 ADR-E2: <c>{type}::{client}::{site}</c>), so
    /// containing the separator is normal and must not be refused.
    /// </summary>
    [Fact]
    public void Mint_WithACompositeEngagementId_IsAccepted()
    {
        Assert.Equal("E2E::Acme::HQ::wf-sow", ExecutionId.Mint("E2E::Acme::HQ", "wf-sow"));
    }

    /// <summary>
    /// ADR-PA15 removed <c>Parse</c>/<c>ParseOrNull</c>: an execution id is an addressing key that
    /// is written and never read. What remains to guarantee is that the affinity key is exact for a
    /// <b>composite</b> engagement id, which is the shape production has and whose absence from the
    /// old suite is exactly what let ADR-PA12's defect live.
    /// </summary>
    [Theory]
    [InlineData("E2E::Acme::HQ", "wf-sow", "E2E::Acme::HQ::wf-sow")]
    [InlineData("E2E::Acme::Admin-Website", "wf-1", "E2E::Acme::Admin-Website::wf-1")]
    [InlineData("eng-1", "wf-1", "eng-1::wf-1")]
    public void Mint_CompositeEngagementId_AppendsTheWorkflowAsTheFinalSegment(string engagementId, string workflowId, string expected)
    {
        Assert.Equal(expected, ExecutionId.Mint(engagementId, workflowId));
    }

    /// <summary>
    /// ADR-PA20: the instance id is the run token and nothing else. The affinity key does not ride
    /// in it — that is what made the id overflow DTS's cap for any caller-shaped pair of ids.
    /// </summary>
    [Fact]
    public void ForRun_IsTheTokenItself()
    {
        Assert.Equal("0199f0c2e4a17b3c9d5e6f708192a3b4", ExecutionId.ForRun("0199f0c2e4a17b3c9d5e6f708192a3b4"));
    }

    [Fact]
    public void ForRun_DoesNotContainTheAffinityKey()
    {
        var instanceId = ExecutionId.ForRun("0199f0c3");

        Assert.DoesNotContain(ExecutionId.Separator, instanceId, StringComparison.Ordinal);
        Assert.DoesNotContain(ExecutionId.Mint("eng-1", "wf-1"), instanceId, StringComparison.Ordinal);
    }

    /// <summary>
    /// The limit that found the defect, pinned: a 32-character token is comfortably inside it, a
    /// 101-character one is refused here rather than by the scheduler's gRPC client at start.
    /// </summary>
    [Fact]
    public void ForRun_TokenLongerThanTheSchedulerAllows_Throws()
    {
        var tooLong = new string('a', ExecutionId.MaxInstanceIdLength + 1);

        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.ForRun(tooLong));

        Assert.Equal("runToken", ex.ParamName);
        Assert.Contains("100", ex.Message, StringComparison.Ordinal);
        Assert.Equal(ExecutionId.MaxInstanceIdLength, ExecutionId.ForRun(new string('a', ExecutionId.MaxInstanceIdLength)).Length);
    }

    /// <summary>A token that reads as an affinity key would blur the one boundary that still matters.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("eng-1::wf-1")]
    public void ForRun_TokenThatIsNotASingleSegment_Throws(string runToken)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.ForRun(runToken));

        Assert.Equal("runToken", ex.ParamName);
    }

    [Fact]
    public void MaxInstanceIdLength_IsTheSchedulersCap()
    {
        Assert.Equal(100, ExecutionId.MaxInstanceIdLength);
    }

    [Fact]
    public void Separator_IsTheDocumentedFormat()
    {
        Assert.Equal("::", ExecutionId.Separator);
    }
}
