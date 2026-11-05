namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// An activity the platform's orchestrators schedule: its name, wire types and, for one the platform
/// implements, the generation its implementation is registered under (<see langword="null"/> when
/// unversioned, which means the current generation).
/// </summary>
internal sealed record ActivityImplementation(string Name, string ImplementedBy, Type Input, Type Output, int? Generation)
{
    /// <summary>The <see cref="ImplementedBy"/> value for an implementation shipped in a platform package.</summary>
    public const string Platform = "platform";

    /// <summary>The <see cref="ImplementedBy"/> value for an activity the platform calls but the consumer implements.</summary>
    public const string Consumer = "consumer";

    /// <summary>Whether this implementation serves <paramref name="generation"/>, given the snapshot's current one.</summary>
    public bool Serves(int generation, int current) => (Generation ?? current) == generation;

    /// <summary>This activity's shape as the snapshot records it.</summary>
    public ActivityShape ToShape() => new()
    {
        Name = Name,
        ImplementedBy = ImplementedBy,
        InputType = TypeName(Input),
        OutputType = TypeName(Output),
        Input = WireShapeReader.Read(Input),
        Output = WireShapeReader.Read(Output),
    };

    /// <summary>A readable CLR type name, generic arguments included.</summary>
    public static string TypeName(Type type) => type.IsGenericType
        ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>"
        : type.Name;
}
