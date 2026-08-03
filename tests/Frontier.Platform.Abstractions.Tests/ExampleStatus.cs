using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>
/// Minimal <c>SectionStatus</c>-shaped smart enum (TD-11) used to test
/// <see cref="SmartEnum{TEnum}"/> and, via <c>Frontier.Platform.Serialization</c>,
/// <c>SmartEnumJsonConverter&lt;TEnum&gt;</c> — the pattern Stage 1 reuses for the
/// full contract set.
/// </summary>
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
