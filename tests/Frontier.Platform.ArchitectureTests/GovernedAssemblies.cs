using System.Reflection;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// Names of the platform library assemblies governed by the doc 00 §5 ownership
/// map / library-boundaries rules, plus helpers to load them by name via the test
/// project's ProjectReference closure.
/// </summary>
internal static class GovernedAssemblies
{
    public const string PlatformAbstractionsName = "Frontier.Platform.Abstractions";
    public const string SerializationName = "Frontier.Platform.Serialization";

    public static readonly IReadOnlyList<string> PlatformLibraryNames =
    [
        PlatformAbstractionsName,
        "Frontier.Platform.Audit",
        "Frontier.Platform.ContextAssembly",
        "Frontier.Platform.Guardrails",
        "Frontier.Platform.Hitl",
        "Frontier.Platform.ModelRoleConfig",
        "Frontier.Platform.Observability",
        "Frontier.Platform.Resilience",
        SerializationName,
        "Frontier.Platform.Workflow.Model",
    ];

    public static AssemblyName[] ReferencedAssemblyNames(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies();

    public static bool IsFrameworkAssembly(string? name) =>
        name is not null && (name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib");

    public static bool IsFrontierAssembly(string? name) =>
        name?.StartsWith("Frontier.", StringComparison.Ordinal) == true;

    public static bool IsForbiddenPlatformReference(string? name) =>
        name?.StartsWith("Frontier.Platform.", StringComparison.Ordinal) == true && name != SerializationName;

    /// <summary>ADR-PA2 (S11.7): any <c>Frontier.Reason.*</c> assembly — forbidden in every platform library's reference graph.</summary>
    public static bool IsReasonWorkflowAssembly(string? name) =>
        name?.StartsWith("Frontier.Reason.", StringComparison.Ordinal) == true;
}
