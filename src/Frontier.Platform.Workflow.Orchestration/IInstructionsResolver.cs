namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Resolves <see cref="Abstractions.AgentTaskNode.InstructionsRef"/> to the agent
/// instructions content for <see cref="AgentInvocationRequest.Instructions"/> (doc 00
/// §4.3 step 5). Doc 14 (chat-designer-authored instructions, Stage 8) replaces the
/// PoC-grade <see cref="FileInstructionsResolver"/> implementation with a Cosmos-backed
/// store; this interface is the seam. It is called only for a node that names a ref: an
/// agent-backed node may name none, and then nothing is resolved (ADR-PA40).
/// </summary>
public interface IInstructionsResolver
{
    /// <summary>Returns the instructions content referenced by <paramref name="instructionsRef"/>.</summary>
    Task<string> ResolveAsync(string instructionsRef, CancellationToken ct);
}
