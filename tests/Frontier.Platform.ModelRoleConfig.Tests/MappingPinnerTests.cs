using System.Net;
using System.Text;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.102 / ADR-PA29: the pin records the version actually served at execution start, and
/// resolution under a pin reads exactly that version however the current pointer moves.
/// </summary>
public sealed class MappingPinnerTests
{
    private const string Role = "deep-reasoning";

    private static readonly ModelEntry Primary = Model("claude-opus-4-8");
    private static readonly ModelEntry Fallback = Model("claude-fable-5");

    // ─── Pinning: served-version semantics ───────────────────────────────────

    [Fact]
    public async Task PinAsync_CanaryBucketEngagement_PinsTheCanaryVersion()
    {
        var registry = new MovableRoleRegistry(Fleet(1), Canary(2, predecessor: 1));
        registry.Point(Role, 2);

        var pins = await new MappingPinner(registry).PinAsync(EngagementInCanary(50), [Role], CancellationToken.None);

        Assert.Equal(new ModelRolePin { RoleId = Role, MappingVersion = 2, Ring = RolloutRing.Canary }, Assert.Single(pins));
    }

    [Fact]
    public async Task PinAsync_EngagementOutsideCanary_PinsThePredecessorFleetVersion()
    {
        var registry = new MovableRoleRegistry(Fleet(1), Canary(2, predecessor: 1));
        registry.Point(Role, 2);

        var pins = await new MappingPinner(registry).PinAsync(EngagementOutsideCanary(50), [Role], CancellationToken.None);

        Assert.Equal(new ModelRolePin { RoleId = Role, MappingVersion = 1, Ring = RolloutRing.Fleet }, Assert.Single(pins));
    }

    [Fact]
    public async Task PinAsync_DuplicateAndUnorderedRoles_ReturnsOneEntryPerRoleOrdinallySorted()
    {
        var registry = new MovableRoleRegistry(Fleet(1), Fleet(4, "analyst"), Fleet(7, "Zeta"));

        var pins = await new MappingPinner(registry).PinAsync("eng-1", [Role, "analyst", "Zeta", Role, "analyst"], CancellationToken.None);

        Assert.Equal(["Zeta", "analyst", Role], pins.Select(pin => pin.RoleId));
        Assert.Equal([7, 4, 1], pins.Select(pin => pin.MappingVersion));
    }

    [Fact]
    public async Task PinAsync_UnmappedRole_ThrowsPermanentContractViolationNamingTheRole()
    {
        var registry = new MovableRoleRegistry(Fleet(1));

        var ex = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            new MappingPinner(registry).PinAsync("eng-1", [Role, "unmapped-role"], CancellationToken.None));

        Assert.Contains("unmapped-role", Assert.Single(ex.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PinAsync_OtherStoreFailure_PropagatesUnchanged()
    {
        var registry = new MovableRoleRegistry(Fleet(1)) { Failure = new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, "a", 0) };

        await Assert.ThrowsAsync<CosmosException>(() => new MappingPinner(registry).PinAsync("eng-1", [Role], CancellationToken.None));
    }

    [Fact]
    public async Task PinAsync_NullArguments_Throw()
    {
        var pinner = new MappingPinner(new MovableRoleRegistry(Fleet(1)));

        await Assert.ThrowsAsync<ArgumentNullException>(() => pinner.PinAsync(null!, [Role], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => pinner.PinAsync("eng-1", null!, CancellationToken.None));
    }

    // ─── Resolution under a pin ──────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_WithPin_ReturnsThePinnedVersionAfterTheCurrentPointerMoves()
    {
        var registry = new MovableRoleRegistry(Fleet(1), Fleet(2));
        var pin = Assert.Single(await new MappingPinner(registry).PinAsync("eng-1", [Role], CancellationToken.None));
        registry.Point(Role, 1);

        var resolved = await new ModelResolver(registry, new OpenCircuits()).ResolveAsync(Request(pin), CancellationToken.None);

        Assert.Equal(2, pin.MappingVersion);
        Assert.Equal(2, resolved.MappingVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithCanaryPin_DoesNotReEvaluateTheRing()
    {
        // An engagement outside the bucket would be sent to v1 by the ring rules; the pin says v2 was served.
        var registry = new MovableRoleRegistry(Fleet(1), Canary(2, predecessor: 1));
        var pin = new ModelRolePin { RoleId = Role, MappingVersion = 2, Ring = RolloutRing.Canary };

        var resolved = await new ModelResolver(registry, new OpenCircuits()).ResolveAsync(Request(pin, EngagementOutsideCanary(50)), CancellationToken.None);

        Assert.Equal(2, resolved.MappingVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithPinAndOpenPrimaryCircuit_WalksTheChainWithinThePinnedVersion()
    {
        var registry = new MovableRoleRegistry(Fleet(1), Fleet(2));
        var pin = new ModelRolePin { RoleId = Role, MappingVersion = 1, Ring = RolloutRing.Fleet };

        var resolved = await new ModelResolver(registry, new OpenCircuits(Primary.ModelId)).ResolveAsync(Request(pin), CancellationToken.None);

        Assert.Equal(1, resolved.MappingVersion);
        Assert.Equal(1, resolved.ChainPosition);
        Assert.Equal(Fallback.ModelId, resolved.ModelId);
    }

    // ─── The contract ────────────────────────────────────────────────────────

    [Fact]
    public void ModelRolePin_SerializesCanonicallyAndRoundTrips()
    {
        var pin = new ModelRolePin { RoleId = Role, MappingVersion = 2, Ring = RolloutRing.Canary };

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(pin));

        Assert.Equal("""{"role_id":"deep-reasoning","mapping_version":2,"ring":"canary"}""", json);
        Assert.Equal(pin, System.Text.Json.JsonSerializer.Deserialize<ModelRolePin>(json, CanonicalProfile.Options));
    }

    [Fact]
    public void ResolutionRequest_Pin_IsOptional()
    {
        var property = typeof(ResolutionRequest).GetProperty(nameof(ResolutionRequest.Pin))!;

        Assert.Empty(property.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false));
        Assert.Null(new ResolutionRequest { RoleId = Role, EngagementId = "eng-1" }.Pin);
    }

    private static ResolutionRequest Request(ModelRolePin pin, string engagementId = "eng-1") =>
        new() { RoleId = Role, EngagementId = engagementId, Pin = pin };

    private static string EngagementInCanary(int percent) =>
        Enumerable.Range(0, 1000).Select(i => $"eng-{i}").First(id => ServedMappingSelector.IsInCanary(id, percent));

    private static string EngagementOutsideCanary(int percent) =>
        Enumerable.Range(0, 1000).Select(i => $"eng-{i}").First(id => !ServedMappingSelector.IsInCanary(id, percent));

    private static ModelEntry Model(string modelId) => new()
    {
        Provider = "anthropic",
        ModelId = modelId,
        InputCostPer1k = 0.03m,
        OutputCostPer1k = 0.15m,
        CacheReadCostPer1k = 0.003m,
        Currency = "USD",
        ContextWindow = 200_000,
        MaxOutputTokens = 16_000,
    };

    private static RoleMapping Fleet(int version, string roleId = Role) => new()
    {
        RoleId = roleId,
        MappingVersion = version,
        Chain = [Primary, Fallback],
        Ring = RolloutRing.Fleet,
        CanaryPercent = 0,
        ChangeReason = "fleet",
        ApprovedBy = "user:mark",
        EffectiveFromUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static RoleMapping Canary(int version, int predecessor) => Fleet(version) with
    {
        Ring = RolloutRing.Canary,
        CanaryPercent = 50,
        PredecessorFleetVersion = predecessor,
    };

    /// <summary>A registry whose <c>current</c> pointer a test can move; the latest version per role is current until moved. An unknown role reads as Cosmos 404.</summary>
    private sealed class MovableRoleRegistry : IRoleRegistry
    {
        private readonly Dictionary<string, List<RoleMapping>> versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> current = new(StringComparer.Ordinal);

        public MovableRoleRegistry(params RoleMapping[] mappings)
        {
            foreach (var mapping in mappings)
            {
                if (!versions.TryGetValue(mapping.RoleId, out var list))
                    versions[mapping.RoleId] = list = [];
                list.Add(mapping);
                current[mapping.RoleId] = mapping.MappingVersion;
            }
        }

        public CosmosException? Failure { get; init; }

        public void Point(string roleId, int version) => current[roleId] = version;

        public Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken) => Task.FromResult(Phase1RoleCatalogue.Catalogue);

        public Task<RoleMapping> GetActiveMappingAsync(string roleId, CancellationToken cancellationToken) =>
            Failure is not null ? throw Failure
            : current.TryGetValue(roleId, out var version) ? GetMappingVersionAsync(roleId, version, cancellationToken)
            : throw new CosmosException("not found", HttpStatusCode.NotFound, 0, "a", 0);

        public Task<RoleMapping> GetMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken) =>
            Task.FromResult(versions[roleId].Single(mapping => mapping.MappingVersion == version));
    }

    private sealed class OpenCircuits(params string[] open) : ICircuitBreakerQuery
    {
        public bool IsOpen(string provider, string modelId) => open.Contains(modelId, StringComparer.Ordinal);
    }
}
