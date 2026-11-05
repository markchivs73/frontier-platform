using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// <see cref="McpToolInvocationPipeline.InvokeWithTimeoutAsync"/> — doc 10 §7's innermost, per-invocation timeout. Its
/// coverage exclusion claimed the race needed a live slow connector; a fake <see cref="AIFunction"/> that waits on its
/// token shows it does not (S13.193).
/// </summary>
public sealed class McpToolInvocationTimeoutTests
{
    [Fact]
    public async Task InvokeWithTimeoutAsync_ToolAnswersInTime_ReturnsItsResult()
    {
        var tool = AIFunctionFactory.Create(() => "ticket", "get_ticket");

        var result = await McpToolInvocationPipeline.InvokeWithTimeoutAsync(tool, new AIFunctionArguments(), 30, CancellationToken.None);

        Assert.Equal("ticket", Assert.IsType<System.Text.Json.JsonElement>(result).GetString());
    }

    [Fact]
    public async Task InvokeWithTimeoutAsync_ToolOutlivesTheTimeout_IsCancelledAtTheTimeout()
    {
        var tool = AIFunctionFactory.Create(async (CancellationToken ct) => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, "slow_tool");
        var started = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McpToolInvocationPipeline.InvokeWithTimeoutAsync(tool, new AIFunctionArguments(), 1, CancellationToken.None));

        Assert.InRange(started.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task InvokeWithTimeoutAsync_CallerCancels_CancelsTheToolBeforeTheTimeout()
    {
        var tool = AIFunctionFactory.Create(async (CancellationToken ct) => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, "slow_tool");
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McpToolInvocationPipeline.InvokeWithTimeoutAsync(tool, new AIFunctionArguments(), 300, caller.Token));
    }
}
