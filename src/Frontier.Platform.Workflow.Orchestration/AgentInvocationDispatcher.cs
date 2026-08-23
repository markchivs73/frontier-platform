using System.Diagnostics;
using System.Reflection;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Bridges <see cref="AgentTaskActivityPipeline"/>'s runtime <c>string</c>
/// <see cref="AgentTaskNode.OutputContractType"/> to <see cref="IAgentInvoker.InvokeAsync{TOutput}"/>'s
/// compile-time generic parameter (ADR-AG1 direct-POCO binding, doc 00 §4.3 step 5):
/// resolves the output type via <see cref="IContractTypeRegistry"/>, then invokes the
/// generic method by reflection, timing the call for <see cref="AgentInvocationResult.LatencyMs"/>
/// (S5.3) and unwrapping its <see cref="AgentInvocationOutcome{TOutput}"/> into an
/// <see cref="AgentInvocationResult"/>.
/// </summary>
internal sealed class AgentInvocationDispatcher
{
    private static readonly MethodInfo InvokeAsyncMethod = typeof(IAgentInvoker)
        .GetMethod(nameof(IAgentInvoker.InvokeAsync))!;

    private readonly IAgentInvoker invoker;
    private readonly IContractTypeRegistry registry;

    /// <summary>Constructs a dispatcher over <paramref name="invoker"/>, resolving output types via <paramref name="registry"/>.</summary>
    public AgentInvocationDispatcher(IAgentInvoker invoker, IContractTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(registry);

        this.invoker = invoker;
        this.registry = registry;
    }

    /// <summary>
    /// Invokes <see cref="IAgentInvoker.InvokeAsync{TOutput}"/> with <typeparamref name="TOutput"/>
    /// bound to <paramref name="outputContractType"/>'s CLR type, timing the call for
    /// <see cref="AgentInvocationResult.LatencyMs"/> (S5.3).
    /// </summary>
    internal async Task<AgentInvocationResult> InvokeAsync(string outputContractType, AgentInvocationRequest request, CancellationToken ct)
    {
        var outputType = registry.Resolve(outputContractType);
        var typedInvoke = InvokeAsyncMethod.MakeGenericMethod(outputType);

        var stopwatch = Stopwatch.StartNew();
        var task = (Task)typedInvoke.Invoke(invoker, [request, ct])!;
        await task.ConfigureAwait(false);
        stopwatch.Stop();

        var outcome = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var outcomeType = outcome.GetType();

        return new AgentInvocationResult
        {
            Result = (IVersionedContract)outcomeType.GetProperty("Result")!.GetValue(outcome)!,
            Usage = (UsageDetails?)outcomeType.GetProperty("Usage")!.GetValue(outcome),
            ToolCalls = (IReadOnlyList<ToolCall>)outcomeType.GetProperty("ToolCalls")!.GetValue(outcome)!,
            LatencyMs = stopwatch.ElapsedMilliseconds,
        };
    }
}
