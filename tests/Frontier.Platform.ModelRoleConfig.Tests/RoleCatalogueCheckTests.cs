using System.Net;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig.Tests;

public sealed class RoleCatalogueCheckTests
{
    [Fact]
    public void Name_ReturnsRoleCatalogue()
    {
        var check = new RoleCatalogueCheck(new FakeRolesSource(), new FakeRoleRegistry());

        Assert.Equal("RoleCatalogue", check.Name);
    }

    [Fact]
    public async Task CheckAsync_SourceRolesAllMapped_ReturnsPass()
    {
        var registry = new FakeRoleRegistry();
        registry.Mappings["deep-reasoning"] = BuildMapping("deep-reasoning", RolloutRing.Fleet);
        var check = new RoleCatalogueCheck(new FakeRolesSource("deep-reasoning"), registry);

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_SourceRoleUnmapped_ReturnsFail()
    {
        var check = new RoleCatalogueCheck(new FakeRolesSource("embeddings"), new FakeRoleRegistry());

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("no active mapping", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_NoReferencedRoles_ReturnsPass()
    {
        var result = await RoleCatalogueCheck.EvaluateAsync(new HashSet<string>(StringComparer.Ordinal), new FakeRoleRegistry(), CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task EvaluateAsync_RoleHasFleetMapping_ReturnsPass()
    {
        var registry = new FakeRoleRegistry();
        registry.Mappings["deep-reasoning"] = BuildMapping("deep-reasoning", RolloutRing.Fleet);

        var result = await RoleCatalogueCheck.EvaluateAsync(new HashSet<string>(StringComparer.Ordinal) { "deep-reasoning" }, registry, CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task EvaluateAsync_RoleHasCanaryMapping_ReturnsPass()
    {
        var registry = new FakeRoleRegistry();
        registry.Mappings["deep-reasoning"] = BuildMapping("deep-reasoning", RolloutRing.Canary);

        var result = await RoleCatalogueCheck.EvaluateAsync(new HashSet<string>(StringComparer.Ordinal) { "deep-reasoning" }, registry, CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task EvaluateAsync_RoleHasShadowOnlyMapping_ReturnsFailWithRingReason()
    {
        var registry = new FakeRoleRegistry();
        registry.Mappings["fast"] = BuildMapping("fast", RolloutRing.Shadow);

        var result = await RoleCatalogueCheck.EvaluateAsync(new HashSet<string>(StringComparer.Ordinal) { "fast" }, registry, CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("not fleet or canary", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_RoleHasNoActiveMapping_ReturnsFailWithOrphanReason()
    {
        var registry = new FakeRoleRegistry();

        var result = await RoleCatalogueCheck.EvaluateAsync(new HashSet<string>(StringComparer.Ordinal) { "embeddings" }, registry, CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("no active mapping", result.FailureReason, StringComparison.Ordinal);
    }

    private static RoleMapping BuildMapping(string roleId, RolloutRing ring) => new()
    {
        RoleId = roleId,
        MappingVersion = 1,
        Chain = [],
        Ring = ring,
        CanaryPercent = 0,
        ChangeReason = "test",
        ApprovedBy = "user:test",
        EffectiveFromUtc = DateTime.UnixEpoch,
        EvaluationEvidenceRef = null,
    };

    /// <summary>Fixed-set <see cref="IReferencedRolesSource"/> double (the port a consuming solution implements).</summary>
    private sealed class FakeRolesSource(params string[] roles) : IReferencedRolesSource
    {
        public Task<IReadOnlySet<string>> GetReferencedRoleIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(roles, StringComparer.Ordinal));
    }

    private sealed class FakeRoleRegistry : IRoleRegistry
    {
        public Dictionary<string, RoleMapping> Mappings { get; } = new(StringComparer.Ordinal);

        public Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken) => Task.FromResult(Phase1RoleCatalogue.Catalogue);

        public Task<RoleMapping> GetActiveMappingAsync(string roleId, CancellationToken cancellationToken)
        {
            if (Mappings.TryGetValue(roleId, out var mapping))
            {
                return Task.FromResult(mapping);
            }

            throw new CosmosException("Not found.", HttpStatusCode.NotFound, subStatusCode: 0, activityId: string.Empty, requestCharge: 0);
        }

        public Task<RoleMapping> GetMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
