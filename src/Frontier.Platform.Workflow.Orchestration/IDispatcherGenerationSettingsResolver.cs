using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The consumer-owned port a dispatcher's generation boundary asks for the current governed settings (consumer
/// S13.146, ADR-PA37 and ADR-PA43 as amended 2026-11-25): the contract budgets and the rollover threshold, each with
/// the version of the record it came from. The engine declares it and deliberately does not register it, as with
/// <see cref="IDispatcherVersionResolver"/>; it is read only inside <see cref="ResolveDispatcherVersionActivity"/>,
/// never in an orchestrator body (hard invariant 2).
/// </summary>
public interface IDispatcherGenerationSettingsResolver
{
    /// <summary>The settings the next generation of <paramref name="workflowId"/>'s dispatcher for <paramref name="engagementId"/> should run with.</summary>
    /// <remarks>
    /// <b>Failure must throw; it must never be reported as empty settings.</b> A null member means "no governed value
    /// exists, carry the current one forward", so reporting a failed read as nulls would silently keep a dispatcher on
    /// stale settings forever. The boundary retries a throw under the activity's storage profile.
    /// </remarks>
    Task<DispatcherGenerationSettings> ResolveAsync(string engagementId, string workflowId, CancellationToken ct);
}

/// <summary>
/// What <see cref="IDispatcherGenerationSettingsResolver"/> returns. Each value travels with its version: a value with a
/// null version is still taken, and its version is then cleared, never left at the previous generation's (S13.147).
/// </summary>
/// <param name="ContractBudgets">The current contract budgets, or null to carry the input's forward.</param>
/// <param name="ContractBudgetsVersion">The version of <paramref name="ContractBudgets"/>.</param>
/// <param name="DispatcherRolloverThreshold">The current rollover threshold, or null to carry the input's forward.</param>
/// <param name="DispatcherSettingsVersion">The version of the dispatcher-settings record the threshold came from.</param>
[ExcludeFromCodeCoverage(Justification = "Record/POCO with assignment-only constructor")]
public sealed record DispatcherGenerationSettings(
    ContractBudgets? ContractBudgets = null,
    int? ContractBudgetsVersion = null,
    int? DispatcherRolloverThreshold = null,
    int? DispatcherSettingsVersion = null);
