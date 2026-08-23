using Frontier.Platform.Guardrails;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IAdmissionController"/> test double for S4.2 pipeline tests.</summary>
internal sealed class FakeAdmissionController(AdmissionDecision decision) : IAdmissionController
{
    /// <summary>The most recent <see cref="InvocationCostEstimate"/> passed to <see cref="AdmitAsync"/>.</summary>
    internal InvocationCostEstimate? ReceivedEstimate { get; private set; }

    public Task<AdmissionDecision> AdmitAsync(InvocationCostEstimate estimate, CancellationToken cancellationToken)
    {
        ReceivedEstimate = estimate;
        return Task.FromResult(decision);
    }
}
