using Frontier.Platform.Workflow.Compiler.Storage;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// GOV-01, GOV-14: a store that has not implemented the retirement or supersession transitions
/// must fail loudly, never report a transition it did not store.
/// </summary>
public sealed class DefinitionStoreDefaultTransitionTests
{
    private readonly IDefinitionStore _store = new InMemoryDefinitionStore();

    [Fact]
    public async Task RetireVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "user:admin", Reason = "r" };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.RetireVersionAsync("wf", 1, retirement, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.RetireVersionAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnretireVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.UnretireVersionAsync("wf", 1, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.UnretireVersionAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupersedeVersionAsync_DefaultImplementation_ThrowsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.SupersedeVersionAsync("wf", 1, 2, CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.SupersedeVersionAsync), ex.Message, StringComparison.Ordinal);
    }
}
