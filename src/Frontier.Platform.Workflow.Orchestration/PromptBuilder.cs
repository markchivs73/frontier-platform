using ContextPackageContract = Frontier.Platform.Serialization.ContextPackage;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Builds the user-turn prompt <see cref="IAgentInvoker"/> sends to the model (doc 00 §4.3
/// step 5): the assembled three-tier <see cref="ContextPackageContract"/>, followed by the
/// node's validated input contract payload. PoC-grade plain-text layout — doc 14's
/// chat-designer-authored prompt composition (Stage 8) replaces this.
/// </summary>
internal static class PromptBuilder
{
    /// <summary>Composes <paramref name="context"/>'s tiers and <paramref name="inputPayloadJson"/> (typed as <paramref name="inputContractType"/>) into one prompt string.</summary>
    internal static string Build(ContextPackageContract context, string inputContractType, string inputPayloadJson)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputContractType);
        ArgumentNullException.ThrowIfNull(inputPayloadJson);

        return $"""
            # Context

            ## Baseline
            {context.Baseline.Content}

            ## Dynamic
            {context.Dynamic.Content}

            ## Real-time
            {context.RealTime?.Content}

            # Input ({inputContractType})
            {inputPayloadJson}
            """;
    }
}
