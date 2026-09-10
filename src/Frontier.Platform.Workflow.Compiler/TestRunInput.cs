using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Turns a <see cref="TestRunRequest.SampleInputs"/> object into the dynamic-context JSON the
/// executor writes before scheduling (doc 13 §5; ADR-PA17's correction: input is context, not
/// payload). An empty object is "nothing supplied" — the HTTP surface sends <c>{}</c> when the
/// caller leaves the field blank — and becomes <see langword="null"/> so the executor can tell
/// "no input" from "an input that happens to be empty".
/// </summary>
internal static class TestRunInput
{
    /// <summary>The canonical wire form of an empty object.</summary>
    internal const string EmptyObject = "{}";

    /// <summary>Canonical JSON of <paramref name="sampleInputs"/>, or <see langword="null"/> when it is null or an empty object.</summary>
    internal static string? ToDynamicContextJson(object? sampleInputs)
    {
        if (sampleInputs is null)
            return null;
        var json = JsonSerializer.Serialize(sampleInputs, CanonicalProfile.Options);
        return json == EmptyObject ? null : json;
    }
}
