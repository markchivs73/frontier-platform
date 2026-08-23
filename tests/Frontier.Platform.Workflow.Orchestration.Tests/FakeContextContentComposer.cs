using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IContextContentComposer"/> test double for S4.2 pipeline tests.</summary>
internal sealed class FakeContextContentComposer(ComposedContext result) : IContextContentComposer
{
    /// <summary>The most recent <see cref="ContextRequest"/> passed to <see cref="ComposeAsync"/>.</summary>
    internal ContextRequest? ReceivedRequest { get; private set; }

    /// <summary>The most recent revision note passed to <see cref="ComposeAsync"/>.</summary>
    internal string? ReceivedRevisionNote { get; private set; }

    public Task<ComposedContext> ComposeAsync(ContextRequest request, string? revisionNote, CancellationToken ct)
    {
        ReceivedRequest = request;
        ReceivedRevisionNote = revisionNote;
        return Task.FromResult(result);
    }
}
