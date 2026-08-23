using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Maps the wire type names carried by <see cref="AgentTaskNode.InputContractType"/> and
/// <see cref="AgentTaskNode.OutputContractType"/> to their CLR <see cref="IVersionedContract"/>
/// types (doc 00 §4.3): <see cref="AgentInvocationDispatcher"/> uses <see cref="Resolve"/> to
/// pick <see cref="IAgentInvoker.InvokeAsync{TOutput}"/>'s generic argument, and
/// <see cref="AgentTaskActivityPipeline"/> uses <see cref="DeserializeAndValidate"/> for the
/// "validate input contract" step.
/// </summary>
internal interface IContractTypeRegistry
{
    /// <summary>Resolves <paramref name="contractTypeName"/> (e.g. <c>"ScopeSection"</c>) to its CLR type.</summary>
    Type Resolve(string contractTypeName);

    /// <summary>
    /// Deserializes <paramref name="json"/> as the contract type named by
    /// <paramref name="contractTypeName"/> and calls its <see cref="IVersionedContract.Validate"/>.
    /// Throws <see cref="ContractViolationException"/> (permanent, doc 09) if
    /// <paramref name="json"/> is not valid JSON for that type or fails validation.
    /// </summary>
    IVersionedContract DeserializeAndValidate(string contractTypeName, string json);
}
