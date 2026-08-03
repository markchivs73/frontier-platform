namespace Frontier.Platform.Serialization;

/// <summary>
/// A boot-time invariant check (doc 12 §6): registered by the library that owns the
/// invariant, run by the host's startup-check hosted service before the process
/// reports ready. A failing check means the process refuses to start rather than
/// failing on first invocation.
/// </summary>
public interface IStartupCheck
{
    /// <summary>The check's name, used in startup logs and failure reports.</summary>
    string Name { get; }

    /// <summary>Evaluates the invariant, returning a pass/fail result with a human-readable reason on failure.</summary>
    Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken);
}
