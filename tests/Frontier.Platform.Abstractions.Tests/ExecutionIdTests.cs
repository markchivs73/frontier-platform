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

    [Fact]
    public void Separator_IsTheDocumentedFormat()
    {
        Assert.Equal("::", ExecutionId.Separator);
    }
}
