using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// The committed activity-shape snapshot (S13.185, ADR-E15 amendment 2026-11-03 decision 5): every
/// DTF activity the platform's orchestrators call, with its input and output wire shapes, grouped by
/// the compatibility generation whose implementations produce them.
/// </summary>
internal sealed record ActivityShapeSnapshot
{
    /// <summary>The generations, oldest first; the last is the current one.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("generations")]
    public required IReadOnlyList<GenerationShapes> Generations { get; init; }

    /// <summary>The current generation: the newest in the snapshot.</summary>
    [JsonIgnore]
    public GenerationShapes Current => Generations[^1];

    /// <summary>The generation numbered <paramref name="generation"/>, if the snapshot holds it.</summary>
    public GenerationShapes? Find(int generation) => Generations.SingleOrDefault(g => g.Generation == generation);
}

/// <summary>The activity shapes one compatibility generation's implementations accept and return.</summary>
internal sealed record GenerationShapes
{
    /// <summary>The generation number (the DTS orchestration version the Host factory stamps).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("generation")]
    public required int Generation { get; init; }

    /// <summary>The activities, ordered by name.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("activities")]
    public required IReadOnlyList<ActivityShape> Activities { get; init; }
}

/// <summary>One activity's name and wire shapes.</summary>
internal sealed record ActivityShape
{
    /// <summary>The DTF activity name the orchestrator schedules.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary><c>platform</c> when the implementation ships in a platform package, <c>consumer</c> when the platform only calls it.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("implemented_by")]
    public required string ImplementedBy { get; init; }

    /// <summary>The CLR input type, for a reader; a change is reported as additive, never as breaking.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("input_type")]
    public required string InputType { get; init; }

    /// <summary>The CLR output type, for a reader; a change is reported as additive, never as breaking.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("output_type")]
    public required string OutputType { get; init; }

    /// <summary>The input's wire shape, path to token (see <see cref="WireShapeReader"/>).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("input")]
    public required IReadOnlyDictionary<string, string> Input { get; init; }

    /// <summary>The output's wire shape, path to token.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("output")]
    public required IReadOnlyDictionary<string, string> Output { get; init; }
}
