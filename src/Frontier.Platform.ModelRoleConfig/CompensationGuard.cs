using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Appends a governance decision's compensating record without letting a failure to append it replace the change's own
/// outcome (S13.328 (a2), GOV-01). The governance services call this wherever a decision record landed and its change then
/// did not happen: after a failed store write, or after losing a race that the service retries.
/// <para>
/// <b>This contains the cascade; it does not repair the trail.</b> A compensation that does not land leaves an
/// uncompensated decision record on the HMAC-chained audit (K6), which reads as a change that happened. That stays wrong
/// until a durable retry or a reconciliation exists (S13.328 (b)/(c)). Until then this Critical log is the only signal, and
/// it carries the exception: S13.330 scrubbed fault text from responses and records, not from logs.
/// </para>
/// </summary>
internal static partial class CompensationGuard
{
    /// <summary>
    /// Records the compensation of <paramref name="recordId"/> uncancellably, logging at Critical and returning if it fails.
    /// The catch takes every exception: no caller token is involved, so even an <see cref="OperationCanceledException"/>
    /// is an SDK timeout, a fault like any other.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "S13.328 (a2): a compensation that does not land must never replace the change's own outcome, whatever the recorder throws; the failure is logged at Critical with its exception.")]
    /// <param name="recorder">The decision recorder.</param>
    /// <param name="decision">The decision whose record is compensated.</param>
    /// <param name="recordId">The record being compensated.</param>
    /// <param name="reason">Why the change did not happen.</param>
    /// <param name="logger">Where a lost compensation is reported.</param>
    /// <param name="beforeRetry">True at a lost race the service retries: the log then says the retry's record will stand beside the uncompensated one.</param>
    internal static async Task TryCompensateAsync(IMappingDecisionRecorder recorder, MappingGovernanceDecision decision, string recordId, string reason, ILogger logger, bool beforeRetry = false)
    {
        try
        {
            await recorder.RecordCompensationAsync(decision, recordId, reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (beforeRetry)
                LogRaceCompensationLost(logger, ex, recordId, decision.EventType);
            else
                LogCompensationLost(logger, ex, recordId, decision.EventType);
        }
    }

    /// <summary>Source-generated log for a compensation that did not land (S13.328 (a2)); data loss that needs an operator.</summary>
    [LoggerMessage(Level = LogLevel.Critical, Message = "The compensating record for governance record {RecordId} ({EventType}) did not land; the trail now holds that record uncompensated, reading as a change that happened. The change's own outcome stands. Needs an operator until S13.328 (b)/(c) lands.")]
    internal static partial void LogCompensationLost(ILogger logger, Exception exception, string recordId, string eventType);

    /// <summary>Source-generated log for a lost compensation at a race the service retries (S13.328 (a2)).</summary>
    [LoggerMessage(Level = LogLevel.Critical, Message = "The compensating record for governance record {RecordId} ({EventType}) did not land before the decision was retried; the trail now holds that first decision record uncompensated beside the retry's record. Needs an operator until S13.328 (b)/(c) lands.")]
    internal static partial void LogRaceCompensationLost(ILogger logger, Exception exception, string recordId, string eventType);
}
