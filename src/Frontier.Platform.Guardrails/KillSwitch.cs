namespace Frontier.Platform.Guardrails;

/// <summary>
/// In-memory <see cref="IKillSwitch"/> implementation for PoC (S6.5).
/// Thread-safe emergency brake for platform-wide admission control.
/// </summary>
internal sealed class KillSwitch : IKillSwitch
{
    private readonly object _lock = new object();
    private bool _isActive;
    private string? _reason;

    /// <inheritdoc />
    public Task<bool> IsActiveAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_isActive);
        }
    }

    /// <inheritdoc />
    public Task ActivateAsync(string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);

        lock (_lock)
        {
            _isActive = true;
            _reason = reason;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _isActive = false;
            _reason = null;
        }

        return Task.CompletedTask;
    }
}
