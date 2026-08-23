using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// The default <see cref="IContractTypeCatalog"/> reflects the <c>IVersionedContract</c> types in
/// Abstractions. S9.76 adds <see cref="IContractTypeCatalog.Names"/> for the discovery endpoint.
/// </summary>
public sealed class ReflectionContractTypeCatalogTests
{
    private readonly ReflectionContractTypeCatalog _catalog = new(TestContractSet.Instance);

    [Fact]
    public void Names_ListsKnownContracts_SortedOrdinally_AndEachResolves()
    {
        var names = _catalog.Names;

        Assert.Contains("LookupResult", names);
        Assert.Contains("MatchRequest", names);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
        Assert.All(names, n => Assert.True(_catalog.Resolves(n)));
    }

    [Fact]
    public void Resolve_KnownReturnsType_UnknownReturnsNull()
    {
        Assert.NotNull(_catalog.Resolve("LookupResult"));
        Assert.Null(_catalog.Resolve("NotARealContract"));
        Assert.False(_catalog.Resolves("NotARealContract"));
    }
}
