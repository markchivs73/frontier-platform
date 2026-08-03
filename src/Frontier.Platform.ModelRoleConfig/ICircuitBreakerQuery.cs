namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Seam over Resilience (doc 08 §5, S6.7): allows <see cref="ModelResolver"/> to
/// skip chain entries whose provider circuit is open without taking a hard dependency
/// on the Resilience library. Phase 1 default: <see cref="AlwaysClosedCircuitBreakerQuery"/>
/// (all circuits closed — Resilience wires a real implementation at S6.7).
/// </summary>
public interface ICircuitBreakerQuery
{
    /// <summary>Returns <see langword="true"/> if the named model's circuit breaker is open.</summary>
    bool IsOpen(string provider, string modelId);
}

/// <summary>Phase 1 default: all circuits are closed (doc 08 §5; real implementation from Resilience at S6.7).</summary>
internal sealed class AlwaysClosedCircuitBreakerQuery : ICircuitBreakerQuery
{
    /// <inheritdoc />
    public bool IsOpen(string provider, string modelId) => false;
}
