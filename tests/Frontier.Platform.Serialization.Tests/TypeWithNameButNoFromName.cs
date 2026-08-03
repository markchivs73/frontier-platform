namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// Smart-enum-shaped except for a missing static <c>FromName(string)</c> resolver —
/// exercises the shape-validation failure path of <see cref="SmartEnumJsonConverter{TEnum}"/>
/// and <see cref="SmartEnumJsonConverterFactory"/>.
/// </summary>
internal sealed class TypeWithNameButNoFromName
{
    public string Name { get; } = "x";
}
