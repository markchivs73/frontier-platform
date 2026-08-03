namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// Enforces the doc 00 §5 / library-boundaries reference-graph rules for the
/// platform sub-graph at the assembly-reference level (doc 12, TD-4). Ported from
/// the frontier-workflow repo's <c>ProjectReferenceRulesTests</c> at extraction so
/// the ADR-PA2 severability lock-in is enforced by this repo's own CI, independently
/// of the consuming solution.
/// </summary>
public sealed class PlatformReferenceRulesTests
{
    [Fact]
    public void PlatformAbstractions_HasNoFrontierOrThirdPartyDependencies()
    {
        // ADR-PA1 (S9.22): Platform.Abstractions is the zero-dependency root of the
        // platform graph — the only Frontier assembly Serialization may reference.
        var referenced = GovernedAssemblies.ReferencedAssemblyNames(GovernedAssemblies.PlatformAbstractionsName);

        Assert.All(referenced, name => Assert.True(
            GovernedAssemblies.IsFrameworkAssembly(name.Name),
            $"Platform.Abstractions must stay dependency-free (ADR-PA1), but references {name.Name}"));
    }

    [Fact]
    public void Serialization_OnlyReferencesPlatformAbstractions()
    {
        var referenced = GovernedAssemblies.ReferencedAssemblyNames(GovernedAssemblies.SerializationName);

        Assert.All(referenced, name => Assert.True(
            !GovernedAssemblies.IsFrontierAssembly(name.Name) || name.Name == GovernedAssemblies.PlatformAbstractionsName,
            $"Serialization should only reference Platform.Abstractions, not {name.Name}"));
    }

    [Theory]
    [MemberData(nameof(NonSerializationPlatformLibraryNames))]
    public void PlatformLibrary_OnlyReferencesAbstractionsAndSerializationAmongPlatformLibraries(string libraryName)
    {
        var referenced = GovernedAssemblies.ReferencedAssemblyNames(libraryName);

        Assert.All(referenced, name => Assert.True(
            !GovernedAssemblies.IsForbiddenPlatformReference(name.Name) ||
            name.Name == GovernedAssemblies.PlatformAbstractionsName,
            $"{libraryName} should only reference Platform.Abstractions and Serialization among platform libraries, not {name.Name}"));
    }

    [Theory]
    [MemberData(nameof(AllPlatformLibraryNames))]
    public void PlatformLibrary_DoesNotReferenceReasonWorkflowAssemblies(string libraryName)
    {
        // ADR-PA2 (S11.7, the Stage 11 lock-in): the platform is a severable sub-graph —
        // no platform assembly may reference any Frontier.Reason.* assembly, ever. In this
        // repo the rule is also structural (no Reason.* project exists to reference), but
        // it is kept explicit so a stray PackageReference to the consuming solution's
        // published assemblies would still fail.
        var referenced = GovernedAssemblies.ReferencedAssemblyNames(libraryName);

        Assert.All(referenced, name => Assert.False(
            GovernedAssemblies.IsReasonWorkflowAssembly(name.Name),
            $"{libraryName} must never reference a Frontier.Reason.* assembly (ADR-PA2), but references {name.Name}"));
    }

    public static TheoryData<string> AllPlatformLibraryNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in GovernedAssemblies.PlatformLibraryNames)
        {
            data.Add(name);
        }

        return data;
    }

    public static TheoryData<string> NonSerializationPlatformLibraryNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in GovernedAssemblies.PlatformLibraryNames.Where(n => n != GovernedAssemblies.SerializationName))
        {
            data.Add(name);
        }

        return data;
    }
}
