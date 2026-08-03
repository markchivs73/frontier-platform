using System.Reflection;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// Base type for smart enums (TD-11): domain concepts with a fixed set of named
/// values and behaviour beyond a bare discriminator. Subclasses declare their
/// values as <c>public static readonly</c> fields of type <typeparamref name="TEnum"/>.
/// <see cref="Name"/> is the canonical snake_case wire string (doc 01 ADR-C1);
/// <c>SmartEnumJsonConverter&lt;TEnum&gt;</c> in <c>Frontier.Platform.Serialization</c>
/// reads/writes it without this assembly taking a dependency on Serialization
/// (Abstractions stays at zero dependencies).
/// </summary>
public abstract class SmartEnum<TEnum> : IEquatable<SmartEnum<TEnum>>
    where TEnum : SmartEnum<TEnum>
{
    private static readonly Lazy<IReadOnlyList<TEnum>> Values = new(DiscoverValues);

    /// <summary>Creates a smart-enum value with its canonical wire name.</summary>
    protected SmartEnum(string name)
    {
        Name = name;
    }

    /// <summary>The canonical snake_case wire string for this value.</summary>
    public string Name { get; }

    /// <summary>All declared values of <typeparamref name="TEnum"/>, in declaration order.</summary>
    public static IReadOnlyList<TEnum> List => Values.Value;

    /// <summary>Resolves a value by its <see cref="Name"/>; throws for unknown wire values.</summary>
    public static TEnum FromName(string name) =>
        TryFromName(name, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(name), name, $"Unknown {typeof(TEnum).Name} value.");

    /// <summary>Attempts to resolve a value by its <see cref="Name"/>.</summary>
    public static bool TryFromName(string name, out TEnum value)
    {
        var match = List.FirstOrDefault(candidate => candidate.Name == name);
        value = match!;
        return match is not null;
    }

    /// <inheritdoc />
    public bool Equals(SmartEnum<TEnum>? other) => other is not null && Name == other.Name;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SmartEnum<TEnum>);

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>
    /// Reflects over every <c>public static readonly</c> field <typeparamref name="TEnum"/>
    /// declares — each must be of type <typeparamref name="TEnum"/> itself.
    /// </summary>
    internal static IReadOnlyList<TEnum> DiscoverValues() =>
        typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(field => (TEnum)field.GetValue(null)!)
            .ToArray();
}
