using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// Type-safe engagement identifier (immutable, distinct type for compile-time safety).
/// Serializes as a plain string in the canonical profile, preserving wire format.
/// </summary>
#pragma warning disable CA2225
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated equality and implicit operators")]
public sealed record EngagementId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Implicitly converts a string to an <see cref="EngagementId"/>.</summary>
    public static implicit operator EngagementId(string value) => new(value);

    /// <summary>Implicitly converts an <see cref="EngagementId"/> to a string.</summary>
#pragma warning disable CA1062
    public static implicit operator string(EngagementId engagementId) => engagementId.Value;
#pragma warning restore CA1062
}
#pragma warning restore CA2225
