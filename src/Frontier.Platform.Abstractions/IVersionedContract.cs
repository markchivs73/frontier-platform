namespace Frontier.Platform.Abstractions;

/// <summary>
/// Implemented by every type that crosses a serialization boundary (doc 01 §2).
/// <see cref="SchemaVersion"/> is always wire property order 0, name <c>schema_version</c>,
/// in the form <c>"major.minor"</c>. <see cref="Validate"/> performs structural/semantic
/// self-checks only — required fields, ranges, internal referential integrity — and
/// throws <see cref="ContractViolationException"/> on failure. Firm-standard business
/// rules live in Check agents, never here.
/// </summary>
public interface IVersionedContract
{
    /// <summary>The contract's schema version, in the form <c>"major.minor"</c>.</summary>
    string SchemaVersion { get; }

    /// <summary>Throws <see cref="ContractViolationException"/> if this instance is not well-formed.</summary>
    void Validate();
}
