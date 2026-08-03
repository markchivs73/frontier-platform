using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The pre-call cost estimate for one agent invocation (doc 07 §4), built by the
/// invocation pipeline (S4.2) once context assembly has fixed <see cref="PromptTokens"/>
/// and Model-Role Config has resolved <see cref="ResolvedModel"/> and its cost-per-token.
/// Passed to <see cref="IAdmissionController.AdmitAsync"/>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by AdmissionController tests.")]
public sealed record InvocationCostEstimate(
    string CorrelationId,
    string ExecutionId,
    string EngagementId,
    string NodeId,
    string AgentRole,
    string ResolvedModel,
    long PromptTokens,
    long MaxOutputTokens,
    decimal EstimatedCostGbp);
