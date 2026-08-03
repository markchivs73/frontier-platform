using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// The outcome of a dynamic context refresh (S6.2b, ADR-CR1 primitive).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by DynamicContextRefresherTests.")]
public sealed record DynamicContextRefreshResult(
    bool Refreshed,
    int Epoch,
    string ContentHash)
{
    /// <summary>Whether the content actually changed (false = no refresh, same epoch).</summary>
    public bool Refreshed { get; } = Refreshed;

    /// <summary>The epoch after the operation (same as before if Refreshed is false).</summary>
    public int Epoch { get; } = Epoch;

    /// <summary>The canonical hash of the current/new content.</summary>
    public string ContentHash { get; } = ContentHash;
}
