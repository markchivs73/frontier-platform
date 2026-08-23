using System.Text.Json.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors CascadeLogic's <c>CascadeActivityInput</c> wire shape (doc 03 §4) for the
/// <see cref="WorkflowActivityNames.EvaluateCascadeActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. See <see cref="CascadeEvalRequestPayload"/> for
/// why this is duplicated rather than referenced.
/// </summary>
public sealed record CascadeActivityRequest
{
    /// <summary>The pinned workflow definition for this execution.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("definition")]
    public required WorkflowDefinition Definition { get; init; }

    /// <summary>The cascade evaluation request.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("request")]
    public required CascadeEvalRequestPayload Request { get; init; }
}
