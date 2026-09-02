using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>
/// Invariant 3's format, tested once. This suite consolidates two identical copies that had grown
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
    /// A workflow id containing the separator would move the boundary the id is read back from,
    /// producing something that still looks well-formed and parses to different values.
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
    /// is written and never read. The tests that stood here pinned the reading — ADR-PA12's
    /// "the workflow is the final segment" rule, and the deliberately-ambiguous dispatcher child
    /// id — and went with the readers they protected.
    /// <para>
    /// What replaces them is the guarantee that still matters: minting is exact for a
    /// <b>composite</b> engagement id, which is the shape production has and whose absence from
    /// the old suite is exactly what let ADR-PA12's defect live.
    /// </para>
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
    /// ADR-EX1: the run token is what lets a second run of the same engagement-workflow exist at
    /// all — without it the id collides with its predecessor's history, snapshot document and gate
    /// events. Minted against a composite engagement id, since that is the production shape.
    /// </summary>
    [Theory]
    [InlineData("E2E::Acme::Admin-Website", "wf-1", "0199f0c2", "E2E::Acme::Admin-Website::wf-1#0199f0c2")]
    [InlineData("eng-1", "wf-1", "0199f0c3", "eng-1::wf-1#0199f0c3")]
    public void MintRun_AppendsTheRunTokenAfterTheAffinityKey(string engagementId, string workflowId, string runToken, string expected)
    {
        Assert.Equal(expected, ExecutionId.MintRun(engagementId, workflowId, runToken));
    }

    /// <summary>
    /// The two-argument mint stays the <b>affinity key</b> — the derivable value the claim is taken
    /// on. `MintRun` must extend it rather than replace it, or one live run per engagement-workflow
    /// stops being enforceable.
    /// </summary>
    [Fact]
    public void MintRun_ExtendsTheAffinityKeyRatherThanReplacingIt()
    {
        var affinityKey = ExecutionId.Mint("E2E::Acme::HQ", "wf-sow");

        var runId = ExecutionId.MintRun("E2E::Acme::HQ", "wf-sow", "0199f0c4");

        Assert.StartsWith(affinityKey + ExecutionId.RunSeparator, runId, StringComparison.Ordinal);
    }

    [Fact]
    public void MintRun_TwoRuns_ProduceDistinctIds()
    {
        var first = ExecutionId.MintRun("eng-1", "wf-1", "0199f0c5");
        var second = ExecutionId.MintRun("eng-1", "wf-1", "0199f0c6");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("run::2")]
    [InlineData("run#2")]
    public void MintRun_TokenThatWouldBlurTheBoundary_Throws(string runToken)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.MintRun("eng-1", "wf-1", runToken));

        Assert.Equal("runToken", ex.ParamName);
    }

    [Fact]
    public void RunSeparator_IsNotTheSegmentSeparator()
    {
        // The run is not another level of the engagement hierarchy — S13.40's child-id ambiguity is
        // exactly what sharing one mark for both would recreate.
        Assert.NotEqual(ExecutionId.Separator, ExecutionId.RunSeparator);
    }

    [Fact]
    public void Separator_IsTheDocumentedFormat()
    {
        Assert.Equal("::", ExecutionId.Separator);
    }
}
