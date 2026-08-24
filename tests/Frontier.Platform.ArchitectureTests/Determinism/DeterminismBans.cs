using System.Reflection;

namespace Frontier.Platform.ArchitectureTests.Determinism;

/// <summary>
/// What an orchestrator body may not call, and why. Each entry is a way to read state that is
/// not in the durable history: on replay the recorded decisions and the recomputed ones diverge,
/// and nothing announces it.
/// <para>
/// The clock is the sharp case. <c>DateTime.UtcNow</c> is banned; <c>TaskOrchestrationContext</c>'s
/// <c>CurrentUtcDateTime</c> is the sanctioned form of the same question, because its answer is
/// recorded and replayed. They are one keystroke apart in source and unmistakable here.
/// </para>
/// </summary>
internal static class DeterminismBans
{
    /// <summary>Namespaces whose members reach outside the replayable world entirely.</summary>
    internal static readonly IReadOnlyList<string> ForbiddenNamespaces =
    [
        "System.IO",
        "System.Net",
        "System.Data",
        "Azure",
        "Microsoft.Azure",
        "Microsoft.Extensions.Configuration",
    ];

    /// <summary>Individual members that read ambient, unrecorded state.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> ForbiddenMembers =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["System.DateTime"] = ["get_Now", "get_UtcNow", "get_Today"],
            ["System.DateTimeOffset"] = ["get_Now", "get_UtcNow"],
            ["System.Guid"] = ["NewGuid"],
            ["System.Environment"] = ["get_TickCount", "get_TickCount64"],
            ["System.Threading.Thread"] = ["Sleep"],
            ["System.Threading.Tasks.Task"] = ["Delay", "Run"],
        };

    /// <summary>Types whose every member is non-deterministic by construction.</summary>
    internal static readonly IReadOnlyList<string> ForbiddenTypes =
    [
        "System.Random",
        "System.Diagnostics.Stopwatch",
    ];

    /// <summary>Why <paramref name="method"/> may not appear in an orchestrator body, or null if it may.</summary>
    internal static string? ReasonToRefuse(MethodBase method)
    {
        var declaring = method.DeclaringType;
        if (declaring?.FullName is not { } typeName)
        {
            return null;
        }

        return NamespaceReason(declaring.Namespace) ?? TypeReason(typeName) ?? MemberReason(typeName, method.Name);
    }

    internal static string? NamespaceReason(string? ns) =>
        ns is not null && ForbiddenNamespaces.Any(f => ns == f || ns.StartsWith(f + ".", StringComparison.Ordinal))
            ? $"reaches I/O or configuration ({ns}) — that work belongs in an activity"
            : null;

    internal static string? TypeReason(string typeName) =>
        ForbiddenTypes.Contains(typeName) ? $"{typeName} is non-deterministic by construction" : null;

    internal static string? MemberReason(string typeName, string memberName) =>
        ForbiddenMembers.TryGetValue(typeName, out var members) && members.Contains(memberName)
            ? $"{typeName}.{Readable(memberName)} reads ambient state that replay cannot reproduce"
            : null;

    internal static string Readable(string memberName) =>
        memberName.StartsWith("get_", StringComparison.Ordinal) ? memberName[4..] : memberName;
}
