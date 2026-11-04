using Frontier.Platform.Serialization;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Boot check (doc 12 §6, doc 10 §7): validates the timeout hierarchy
/// "per-attempt provider timeout &lt; total pipeline timeout &lt; DTF activity timeout"
/// holds for every compiled-in <see cref="Phase1ResilienceProfileCatalogue"/> profile.
/// HITL escalation sits above the DTF activity timeout by construction (gate timeouts
/// are orchestrator-level timers, not activity calls) and is out of scope here.
/// </summary>
internal sealed class TimeoutHierarchyCheck : IStartupCheck
{
    /// <summary>
    /// The DTF activity timeout (doc 10 §7: "10 min default") that every profile's
    /// pipeline timeout must stay under.
    /// </summary>
    internal const int DtfActivityTimeoutMs = 600_000;

    /// <inheritdoc />
    public string Name => "TimeoutHierarchy";

    /// <inheritdoc />
    public Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(Phase1ResilienceProfileCatalogue.ByProfileId.Values));

    /// <summary>
    /// Checks each profile's pipeline timeout (<see cref="PipelineTimeoutMs"/>) against
    /// <see cref="DtfActivityTimeoutMs"/>, per doc 10 §7's nesting rule. The per-attempt
    /// &lt; pipeline relationship is guaranteed by construction (<c>PipelineTimeoutMs =
    /// TimeoutMs * TotalTimeoutMultiplier</c>, a multiplier above 1) and needs no separate check.
    /// </summary>
    internal static StartupCheckResult Evaluate(IEnumerable<ResilienceProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            var pipelineTimeoutMs = PipelineTimeoutMs(profile);

            if (pipelineTimeoutMs >= DtfActivityTimeoutMs)
            {
                return StartupCheckResult.Fail(
                    $"Timeout hierarchy violation in profile '{profile.ProfileId}': " +
                    $"pipeline timeout {pipelineTimeoutMs}ms is not less than the DTF activity timeout {DtfActivityTimeoutMs}ms (doc 10 §7).");
            }
        }

        return StartupCheckResult.Pass();
    }

    /// <summary>
    /// The total pipeline timeout the inner pipeline actually applies (doc 10 §7: "~3× attempt"),
    /// read from <see cref="ResiliencePolicyProvider.TotalTimeoutMs"/> so the check and the pipeline
    /// cannot disagree.
    /// </summary>
    internal static int PipelineTimeoutMs(ResilienceProfile profile) =>
        ResiliencePolicyProvider.TotalTimeoutMs(profile);
}
