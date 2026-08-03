using System.Reflection;
using NetArchTest.Rules;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// Type-level architecture rules (doc 12 §3, TD-4) for the platform sub-graph —
/// the belt-and-braces companion to <see cref="PlatformReferenceRulesTests"/>,
/// catching leakage the reference-graph rules cannot see (e.g. a compile-linked
/// source file carrying a <c>Frontier.Reason</c> type).
/// </summary>
public sealed class PlatformTypeRulesTests
{
    /// <summary>
    /// ADR-PA2 (S11.7): no type in any platform assembly may depend on a
    /// <c>Frontier.Reason</c> type.
    /// </summary>
    [Fact]
    public void PlatformAssemblyTypes_DoNotDependOnReasonWorkflowTypes()
    {
        foreach (var libraryName in GovernedAssemblies.PlatformLibraryNames)
        {
            var result = Types.InAssembly(Assembly.Load(libraryName))
                .ShouldNot().HaveDependencyOn("Frontier.Reason")
                .GetResult();

            Assert.True(result.IsSuccessful,
                $"{libraryName} types must not depend on Frontier.Reason.* types (ADR-PA2). " +
                $"Failing types: {Describe(result.FailingTypeNames)}");
        }
    }

    private static string Describe(IEnumerable<string>? failingTypeNames) =>
        failingTypeNames is null ? "(none)" : string.Join(", ", failingTypeNames);
}
