using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// Minimal <c>SectionStatus</c>-shaped smart enum (TD-11) demonstrating the
/// canonical snake_case converter pattern Stage 1 reuses for the full contract set.
/// </summary>
[JsonConverter(typeof(SmartEnumJsonConverter<ExampleStatus>))]
internal sealed class ExampleStatus : SmartEnum<ExampleStatus>
{
    public static readonly ExampleStatus Draft = new("draft");
    public static readonly ExampleStatus InProgress = new("in_progress");
    public static readonly ExampleStatus Approved = new("approved");

    private ExampleStatus(string name)
        : base(name)
    {
    }
}
