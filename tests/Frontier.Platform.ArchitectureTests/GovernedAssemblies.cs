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

    /// <summary>The workflow definition/execution model, which arrived at ADR-PA3.</summary>
    public const string WorkflowModelName = "Frontier.Platform.Workflow.Model";

    /// <summary>The workflow interpreter, which arrived at ADR-PA5.</summary>
    public const string WorkflowOrchestrationName = "Frontier.Platform.Workflow.Orchestration";

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
        WorkflowModelName,
        WorkflowOrchestrationName,
    ];

    /// <summary>
    /// The **engine tier** (ADR-PA5). These may depend on the governance tier — composing it is
    /// what an interpreter does. Nothing in governance may depend back on them, which is the
    /// property that keeps governance independently consumable.
    /// </summary>
    public static readonly IReadOnlyList<string> EngineLibraryNames =
    [
        WorkflowModelName,
        WorkflowOrchestrationName,
    ];

    /// <summary>The **governance tier**: every platform library that is not the engine.</summary>
    public static readonly IReadOnlyList<string> GovernanceLibraries =
        [.. PlatformLibraryNames.Where(name => !EngineLibraryNames.Contains(name))];

    /// <summary>Whether <paramref name="name"/> is an engine-tier assembly.</summary>
    public static bool IsEngineAssembly(string? name) =>
        name is not null && EngineLibraryNames.Contains(name);

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
