using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Not built: ADR-DC4's evidence-based retirement candidate query is deferred (S13.223, Mark's
/// decision 2026-11-05), and <see cref="GetCandidatesAsync"/> throws rather than report that no
/// version is a candidate. See <see cref="IRetirementMonitor"/> for the trigger that brings it back.
/// </summary>
[Obsolete(RetirementMonitorDeferral.Message)]
public sealed class RetirementMonitor : IRetirementMonitor
{
    /// <summary>Kept so the shipped constructor still binds; the container is not read.</summary>
    public RetirementMonitor(Container executionSnapshotsContainer) =>
        ArgumentNullException.ThrowIfNull(executionSnapshotsContainer);

    /// <summary>Always throws <see cref="NotSupportedException"/>: the candidate query is not built.</summary>
    public Task<IReadOnlyList<RetirementCandidate>> GetCandidatesAsync(CancellationToken ct) =>
        throw new NotSupportedException(RetirementMonitorDeferral.Message);
}

/// <summary>The one statement of the retirement monitor's deferral, shared by its attributes and its exception.</summary>
internal static class RetirementMonitorDeferral
{
    /// <summary>Why the monitor is obsolete and when it returns.</summary>
    internal const string Message =
        "Deprecated in v0.51.0: ADR-DC4's retirement candidate query is not built (deferred at S13.223, 2026-11-05); "
        + "it returned no candidates whatever the evidence, and nothing calls it. No replacement: retirement stays a "
        + "workflow-admin decision through IDefinitionLifecycleService.RetireAsync. Removed at the next phase boundary "
        + "unless the governance dashboard's retirement-candidate surface (doc 13 §8 step 2) is scheduled first, which "
        + "is the trigger to build it.";
}
