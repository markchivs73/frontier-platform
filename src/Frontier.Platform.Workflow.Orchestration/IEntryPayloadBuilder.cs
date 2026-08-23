namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Builds the payload for an entry node — one with no upstream Data-edge predecessor — from
/// the assembled dynamic context tier (S4.1/S4.2; extracted behind this port at S13.12c).
///
/// The mapping is inherently **workload-specific**: it decides which dynamic-context field
/// becomes which contract, and both of those are the workload's vocabulary. That made
/// <c>EntryContractBuilder</c> the single genuine code coupling from the engine to workload
/// contracts (every other mention across Orchestration was doc-comment prose), so it becomes
/// a consumer-owned port here — the engine asks for an entry payload and does not know what
/// shape answers (ADR-E3a: the engine must be consumable without adopting the first
/// workload's vocabulary).
/// </summary>
public interface IEntryPayloadBuilder
{
    /// <summary>
    /// Builds the entry node's canonical-JSON input payload from
    /// <paramref name="dynamicContentJson"/>. Throws
    /// <see cref="Frontier.Platform.Abstractions.ContractViolationException"/> (permanent,
    /// doc 09) when the context does not carry what the workload's entry contract requires.
    /// </summary>
    string BuildEntryPayload(string dynamicContentJson);
}
