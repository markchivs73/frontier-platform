using System.Text.Json.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Where a baseline tier's bytes came from (doc 04 §4 step 2): the stored baseline catalogue
/// release that was read and the components selected from it, in the order rendered. Passed to
/// <see cref="IContextAssembler.AssembleWithProvenanceAsync"/>
/// so <see cref="Frontier.Platform.Serialization.BaselineTier"/> cites the release and component
/// set instead of placeholders (S13.218), and an audit can say which baseline produced a run.
/// </summary>
/// <param name="BaselineVersion">The baseline catalogue release read — the store's catalogue id (doc 04 §6 keys releases by <c>/version</c>).</param>
/// <param name="Components">The baseline components selected, in the order the content renders them.</param>
public sealed record BaselineTierProvenance(
    [property: JsonPropertyName("baseline_version"), JsonPropertyOrder(0)]
    string BaselineVersion,

    [property: JsonPropertyName("components"), JsonPropertyOrder(1)]
    IReadOnlyList<string> Components);
