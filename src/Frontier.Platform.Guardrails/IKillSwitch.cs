namespace Frontier.Platform.Guardrails;

/// <summary>
/// Global emergency brake for admissions (doc 07 §7, ADR-G1). When activated,
/// all invocations are denied at the platform level, independent of budgets.
/// Deactivation restores normal budget-based admission control.
/// </summary>
public interface IKillSwitch
{
    /// <summary>Returns <c>true</c> if the kill switch is currently active (all admissions denied).</summary>
    Task<bool> IsActiveAsync(CancellationToken cancellationToken);

    /// <summary>Activates the kill switch, denying all future invocations.</summary>
    Task ActivateAsync(string reason, CancellationToken cancellationToken);

    /// <summary>Deactivates the kill switch, restoring budget-based admission control.</summary>
    Task DeactivateAsync(CancellationToken cancellationToken);
}
