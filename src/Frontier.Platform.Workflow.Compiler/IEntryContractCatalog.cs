namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Describes how this deployment feeds a workflow's entry node, so the design agent can be told
/// the rule without the engine knowing the answer.
///
/// The entry node is the one with no incoming control edge; it is handed context at runtime
/// rather than an upstream data payload, so its input contract and the dynamic field carrying
/// that context are both facts about the deployment. They were hardcoded into the design agent's
/// system prompt until S13.12g — a coupling no type-level architecture test could see, because
/// it lived in a string literal.
///
/// <para>Whatever answers this must agree with whatever implements
/// <c>IEntryPayloadBuilder</c> on the runtime side. A design agent told to request one field
/// while the runtime reads another produces workflows that validate and then fail live.</para>
/// </summary>
public interface IEntryContractCatalog
{
    /// <summary>The entry convention the design agent should be told about.</summary>
    Task<EntryContractDescriptor> GetEntryContractAsync(CancellationToken ct);
}

/// <summary>The deployment's entry-node convention, as the design agent needs it stated.</summary>
public sealed record EntryContractDescriptor
{
    /// <summary>The contract type the entry node must declare as its input, e.g. <c>"CaseSummary"</c>.</summary>
    public required string ContractTypeName { get; init; }

    /// <summary>The dynamic-context field carrying that input, e.g. <c>"case_summary"</c>.</summary>
    public required string DynamicFieldName { get; init; }

    /// <summary>
    /// A short noun phrase for what the entry node is handed, used in prose — e.g. "the case summary". Lets the prompt read naturally without the engine naming a domain.
    /// </summary>
    public required string Description { get; init; }
}
