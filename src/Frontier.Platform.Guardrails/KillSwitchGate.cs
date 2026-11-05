using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Admission's read of the kill switch (doc 07 §5a, S13.187). The state is read through a cache of
/// <see cref="CacheDuration"/> (about 10 s for the shared Cosmos switch; data, set at registration), so a
/// switch thrown on the API head stops new admissions on every worker within that window without a store
/// read per invocation. A failed read follows the policy's <see cref="FailureMode"/>, as a ledger outage does
/// (doc 07 §10): <see cref="FailureMode.FailOpenWithAudit"/> admits and logs a warning, which is the alarm;
/// <see cref="FailureMode.FailClosed"/> rethrows, a transient failure the activity retry recovers from. A
/// failed read is never cached, so the next admission reads again.
/// </summary>
internal sealed partial class KillSwitchGate
{
    private readonly IKillSwitch killSwitch;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private Reading? last;

    /// <summary>Creates the gate over <paramref name="killSwitch"/>, caching each successful read for <paramref name="cacheDuration"/> (zero reads every time).</summary>
    public KillSwitchGate(IKillSwitch killSwitch, TimeSpan cacheDuration, TimeProvider timeProvider, ILogger<KillSwitchGate> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheDuration, TimeSpan.Zero);
        this.killSwitch = killSwitch ?? throw new ArgumentNullException(nameof(killSwitch));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CacheDuration = cacheDuration;
    }

    /// <summary>How long a successful read is trusted before the switch is read again.</summary>
    internal TimeSpan CacheDuration { get; }

    /// <summary>Throws <see cref="GuardrailsSuspendedException"/> (permanent, doc 10 §3) when the switch is thrown; otherwise returns.</summary>
    internal async Task EnsureNotSuspendedAsync(GuardrailPolicy policy, CancellationToken cancellationToken)
    {
        if (await IsActiveAsync(policy, cancellationToken).ConfigureAwait(false))
        {
            throw new GuardrailsSuspendedException();
        }
    }

    /// <summary>The switch's state: the cached reading while it is fresh, else a new read; on a failed read, <paramref name="policy"/>'s <see cref="FailureMode"/> decides.</summary>
    internal async Task<bool> IsActiveAsync(GuardrailPolicy policy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (Volatile.Read(ref last) is { } cached && now - cached.ReadAt < CacheDuration)
        {
            return cached.Active;
        }

        try
        {
            var active = await killSwitch.IsActiveAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref last, new Reading(active, now));
            return active;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && policy.OnInfrastructureFailure == FailureMode.FailOpenWithAudit)
        {
            LogKillSwitchUnreadable(logger, policy.PolicyId, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "guardrail_bypass: the kill switch could not be read, so admission proceeds under policy {PolicyId}'s FailOpenWithAudit (doc 07 §10)")]
    private static partial void LogKillSwitchUnreadable(ILogger logger, string policyId, Exception exception);

    /// <summary>One successful read of the switch and when it was taken.</summary>
    private sealed record Reading(bool Active, DateTimeOffset ReadAt);
}
