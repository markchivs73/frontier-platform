namespace Frontier.Platform.Serialization;

/// <summary>The outcome of an <see cref="IStartupCheck"/> (doc 12 §6).</summary>
public sealed record StartupCheckResult
{
    /// <summary>Whether the invariant held.</summary>
    public required bool Passed { get; init; }

    /// <summary>A human-readable explanation of the failure; <see langword="null"/> when <see cref="Passed"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Creates a passing result.</summary>
    public static StartupCheckResult Pass() => new() { Passed = true };

    /// <summary>Creates a failing result with <paramref name="reason"/>.</summary>
    public static StartupCheckResult Fail(string reason) => new() { Passed = false, FailureReason = reason };
}
