using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// <see cref="ModelRoleConfigReads"/> — which row answers a lookup and what a dangling definition pointer means
/// (ADR-PA32, ADR-PA40). Both sat inside the coverage-excluded stores until S13.193.
/// </summary>
public sealed class ModelRoleConfigReadsTests
{
    [Fact]
    public async Task FirstResolvedAsync_SkipsRowsThatNoLongerResolve()
    {
        var resolved = new List<string>();

        var result = await ModelRoleConfigReads.FirstResolvedAsync(
            Rows("gone", "kept", "later"),
            row => { resolved.Add(row); return Task.FromResult(row == "gone" ? null : $"stored:{row}"); },
            CancellationToken.None);

        Assert.Equal("stored:kept", result);
        Assert.Equal(["gone", "kept"], resolved);
    }

    [Fact]
    public async Task FirstResolvedAsync_NoRowResolves_IsNull() =>
        Assert.Null(await ModelRoleConfigReads.FirstResolvedAsync(Rows("a", "b"), _ => Task.FromResult<string?>(null), CancellationToken.None));

    [Fact]
    public async Task FirstResolvedAsync_NoRows_IsNull() =>
        Assert.Null(await ModelRoleConfigReads.FirstResolvedAsync(Rows(), row => Task.FromResult<string?>(row), CancellationToken.None));

    [Fact]
    public async Task FollowDefinitionPointerAsync_NoPointer_IsNullWithoutReading() =>
        Assert.Null(await ModelRoleConfigReads.FollowDefinitionPointerAsync(
            "role", null, _ => throw new InvalidOperationException("read a version with no pointer")));

    [Fact]
    public async Task FollowDefinitionPointerAsync_StoredVersion_ReturnsIt()
    {
        var definition = AgentBacked();
        string? read = null;

        var result = await ModelRoleConfigReads.FollowDefinitionPointerAsync(
            definition.RoleId, "role:definition:v2", id => { read = id; return Task.FromResult<RoleDefinition?>(definition); });

        Assert.Same(definition, result);
        Assert.Equal("role:definition:v2", read);
    }

    [Fact]
    public async Task FollowDefinitionPointerAsync_DanglingPointer_ThrowsNamingTheRef()
    {
        var ex = await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            ModelRoleConfigReads.FollowDefinitionPointerAsync("role", "role:definition:v9", _ => Task.FromResult<RoleDefinition?>(null)));

        Assert.Equal("Role 'role' has a definition pointer to 'role:definition:v9', which is not stored.", ex.Message);
    }

    /// <summary>Stands in for a drained feed: each row arrives asynchronously.</summary>
    private static async IAsyncEnumerable<string> Rows(params string[] rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }
}
