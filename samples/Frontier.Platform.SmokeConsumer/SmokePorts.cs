using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.SmokeConsumer;

// Minimal implementations of every port the interpreter requires a consumer to supply. They do
// nothing: the point is that a consumer *can* implement them from outside the assembly, which
// packing and unit tests cannot tell you.

internal sealed class SmokeAgentInvoker : IAgentInvoker
{
    public Task<AgentInvocationOutcome<TOutput>> InvokeAsync<TOutput>(AgentInvocationRequest request, CancellationToken ct)
        where TOutput : IVersionedContract => throw new NotSupportedException("Smoke test: never invoked.");
}

internal sealed class SmokeInstructionsResolver : IInstructionsResolver
{
    public Task<string> ResolveAsync(string instructionsRef, CancellationToken ct) => Task.FromResult(string.Empty);
}

internal sealed class SmokeToolCatalog : IMcpToolCatalog
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, string executionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}

internal sealed class SmokeEndpointResolver : IMcpEndpointResolver
{
    public Task<Uri> ResolveAsync(string serverName, CancellationToken ct) =>
        Task.FromResult(new Uri("https://localhost/smoke"));
}

internal sealed class SmokeWriteClassifier : IMcpWriteClassifier
{
    // Unclassified means "assume it writes" — fencing a read costs a call, letting a write
    // through defeats the sandbox (see the port's contract).
    public bool IsWrite(McpToolRef toolRef) => true;
}

internal sealed class SmokeSnapshotReader : IExecutionSnapshotReader
{
    public Task<ExecutionSnapshot?> GetLatestAsync(string executionId, string engagementId, CancellationToken cancellationToken) =>
        Task.FromResult<ExecutionSnapshot?>(null);
}

internal sealed class SmokeEntryPayloadBuilder : IEntryPayloadBuilder
{
    public string BuildEntryPayload(string dynamicContentJson) => "{}";
}
