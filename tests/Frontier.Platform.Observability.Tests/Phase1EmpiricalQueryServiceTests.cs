namespace Frontier.Platform.Observability.Tests;

/// <summary>S6.8 tests for <see cref="Phase1EmpiricalQueryService"/> (doc 11 §2/§5).</summary>
public sealed class Phase1EmpiricalQueryServiceTests
{
    private static readonly Phase1EmpiricalQueryService Service = new();
    private static readonly EmpiricalScope Scope = new();

    [Fact]
    public async Task GetCacheEconomicsAsync_ReturnsEmptyResult()
    {
        var result = await Service.GetCacheEconomicsAsync(Scope, CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Equal(0m, result.TotalCostSavedGbp);
        Assert.Equal(0, result.TotalExecutions);
    }

    [Fact]
    public async Task GetRetryDistributionAsync_ReturnsEmptyResult()
    {
        var result = await Service.GetRetryDistributionAsync(Scope, CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.TotalRetries);
        Assert.Equal(0, result.TotalInvocations);
    }

    [Fact]
    public async Task GetValidatorOutcomesAsync_ReturnsEmptyResult()
    {
        var result = await Service.GetValidatorOutcomesAsync(Scope, CancellationToken.None);

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task GetGateEvidenceAsync_ReturnsEmptyResult()
    {
        var result = await Service.GetGateEvidenceAsync(Scope, CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Equal(0m, result.MeanTimeToDecisionMs);
    }

    [Fact]
    public async Task GetCanvasOverlayAsync_ReturnsEmptyOverlayWithCorrectWorkflowId()
    {
        var result = await Service.GetCanvasOverlayAsync("wf-advisory-sow", 3, CancellationToken.None);

        Assert.Equal("wf-advisory-sow", result.WorkflowId);
        Assert.Equal(3, result.DefinitionVersion);
        Assert.Equal(0, result.ExecutionsInWindow);
        Assert.Empty(result.Nodes);
    }
}
