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
    [MemberData(nameof(NonSerializationGovernanceLibraryNames))]
    public void GovernanceLibrary_OnlyReferencesAbstractionsAndSerializationAmongPlatformLibraries(string libraryName)
    {
        // ADR-PA5 narrowed this from "every platform library" to "every governance library".
        // Governance stays flat, so each library is independently consumable and adding one
        // never drags in a sibling. The engine tier is exempt because composing governance
        // through its interfaces is precisely what an interpreter does — the direction test
        // below is what keeps the tiering meaningful.
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

    [Theory]
    [MemberData(nameof(GovernanceLibraryNames))]
    public void GovernanceLibrary_DoesNotDependOnTheEngine(string libraryName)
    {
        // ADR-PA5, and the half that carries the weight. A two-tier graph is only worth having
        // if the dependency runs one way: governance must stay consumable by a solution that
        // wants audit or HITL and no interpreter at all. A single reference in this direction
        // would collapse the tiers back into one graph — and it would compile perfectly.
        var referenced = GovernedAssemblies.ReferencedAssemblyNames(libraryName);

        Assert.All(referenced, name => Assert.False(
            GovernedAssemblies.IsEngineAssembly(name.Name),
            $"{libraryName} is governance-tier and must not depend on the engine tier (ADR-PA5), but references {name.Name}. "
            + "If the engine needs something from here it takes it through an interface; governance never reaches forward."));
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

    public static TheoryData<string> NonSerializationGovernanceLibraryNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in GovernedAssemblies.GovernanceLibraries.Where(n => n != GovernedAssemblies.SerializationName))
        {
            data.Add(name);
        }

        return data;
    }

    public static TheoryData<string> GovernanceLibraryNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in GovernedAssemblies.GovernanceLibraries)
        {
            data.Add(name);
        }

        return data;
    }
}
