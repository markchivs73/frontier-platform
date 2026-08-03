namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S6.6 tests for <see cref="MappingGovernanceService"/> (doc 08 §7-8 ADR-M3).</summary>
public sealed class MappingGovernanceServiceTests
{
    [Fact]
    public async Task ProposeChangeAsync_Throws()
    {
        var service = new MappingGovernanceService(new FakeRoleMappingWriter());
        var change = new MappingChange
        {
            RoleId = "deep-reasoning",
            ProposedMapping = Phase1RoleCatalogue.DeepReasoningMappingV1,
            Reason = "test",
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ProposeChangeAsync(change, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_Throws()
    {
        var service = new MappingGovernanceService(new FakeRoleMappingWriter());

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ApproveAsync("proposal-1", "user:mark", CancellationToken.None));
    }

    [Fact]
    public async Task RollbackAsync_NullRoleId_Throws()
    {
        var service = new MappingGovernanceService(new FakeRoleMappingWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RollbackAsync(null!, 1, "reason", CancellationToken.None));
    }

    [Fact]
    public async Task RollbackAsync_NullReason_Throws()
    {
        var service = new MappingGovernanceService(new FakeRoleMappingWriter());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RollbackAsync("deep-reasoning", 1, null!, CancellationToken.None));
    }

    [Fact]
    public async Task RollbackAsync_ValidArgs_DelegatesToWriter()
    {
        var writer = new FakeRoleMappingWriter();
        var service = new MappingGovernanceService(writer);

        await service.RollbackAsync("deep-reasoning", 1, "degradation confirmed", CancellationToken.None);

        Assert.Equal("deep-reasoning", writer.LastRoleId);
        Assert.Equal(1, writer.LastToVersion);
    }

    private sealed class FakeRoleMappingWriter : IRoleMappingWriter
    {
        public string? LastRoleId { get; private set; }
        public int? LastToVersion { get; private set; }

        public Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct)
        {
            LastRoleId = roleId;
            LastToVersion = toVersion;
            return Task.CompletedTask;
        }
    }
}
