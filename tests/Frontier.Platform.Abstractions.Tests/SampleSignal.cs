using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>
/// Local versioned-contract fixture mirroring the subsystem contract shape without
/// referencing any <c>Frontier.Reason.*</c> assembly — this test project's closure
/// stays platform-only (ADR-PA2). Used by <see cref="ContractMigratorTests"/>.
/// </summary>
internal sealed record SampleSignal : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The external system that raised this signal.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("source_system")]
    public required string SourceSystem { get; init; }

    /// <summary>The kind of event reported.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("event_kind")]
    public required string EventKind { get; init; }

    /// <summary>Deduplication key for this event.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("dedupe_id")]
    public required string DedupeId { get; init; }

    /// <summary>The raw event payload.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("payload")]
    public required string Payload { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceSystem))
        {
            throw new ContractViolationException(nameof(SampleSignal), ["source_system must not be empty."]);
        }
    }
}
