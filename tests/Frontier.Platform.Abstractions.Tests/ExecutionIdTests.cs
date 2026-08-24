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
    /// The regression this type exists to end. Every predecessor split from the left and returned
    /// ("E2E", "Acme") for this id — wrong engagement, wrong workflow — feeding the audit record's
    /// partition key and identity fields. It survived because every test used a single-segment
    /// engagement id, which production ids are not.
    /// </summary>
    [Theory]
    [InlineData("E2E::Acme::HQ::wf-sow", "E2E::Acme::HQ", "wf-sow")]
    [InlineData("E2E::Acme::Admin-Website::wf-1", "E2E::Acme::Admin-Website", "wf-1")]
    [InlineData("eng-1::wf-1", "eng-1", "wf-1")]
    public void Parse_TakesTheWorkflowFromTheFinalSegment(string executionId, string expectedEngagement, string expectedWorkflow)
    {
        var (engagementId, workflowId) = ExecutionId.Parse(executionId);

        Assert.Equal(expectedEngagement, engagementId);
        Assert.Equal(expectedWorkflow, workflowId);
    }

    [Fact]
    public void MintAndParse_RoundTrip()
    {
        var (engagementId, workflowId) = ExecutionId.Parse(ExecutionId.Mint("eng-1", "wf-1"));

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    /// <summary>
    /// A dispatcher child id is genuinely ambiguous — <c>eng-1::wf-1::item-1</c> is indistinguishable
    /// from a two-segment engagement id running <c>item-1</c>. Pinned as the documented behaviour
    /// rather than left to be discovered: <see cref="ExecutionId.Parse"/> is for top-level ids, which
    /// is what all three callers hold.
    /// </summary>
    [Fact]
    public void Parse_DispatcherChildId_ReadsTheWorkItemAsTheFinalSegment()
    {
        var (engagementId, workflowId) = ExecutionId.Parse("eng-1::wf-1::item-1");

        Assert.Equal("eng-1::wf-1", engagementId);
        Assert.Equal("item-1", workflowId);
    }

    [Theory]
    [InlineData("eng-1")]
    [InlineData("")]
    [InlineData("::wf-1")]
    [InlineData("eng-1::")]
    public void Parse_WithoutTwoUsableSegments_Throws(string executionId)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.Parse(executionId));

        Assert.Equal("executionId", ex.ParamName);
        Assert.Contains("engagementId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOrNull_WellFormedId_ReturnsTheSegments()
    {
        Assert.Equal(("eng-1", "wf-1"), ExecutionId.ParseOrNull("eng-1::wf-1"));
    }

    /// <summary>
    /// The tolerant reading exists for callers holding an identifier that may legitimately be
    /// something else, so a malformed value is a branch rather than an exception.
    /// </summary>
    [Theory]
    [InlineData("eng-1")]
    [InlineData("")]
    public void ParseOrNull_WithoutTwoUsableSegments_ReturnsNull(string executionId)
    {
        Assert.Null(ExecutionId.ParseOrNull(executionId));
    }

    [Fact]
    public void Separator_IsTheDocumentedFormat()
    {
        Assert.Equal("::", ExecutionId.Separator);
    }
}
