namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IInstructionsResolver"/> test double for S4.2 pipeline tests.</summary>
internal sealed class FakeInstructionsResolver(string instructions) : IInstructionsResolver
{
    /// <summary>The most recent <c>instructionsRef</c> passed to <see cref="ResolveAsync"/>.</summary>
    internal string? ReceivedInstructionsRef { get; private set; }

    public Task<string> ResolveAsync(string instructionsRef, CancellationToken ct)
    {
        ReceivedInstructionsRef = instructionsRef;
        return Task.FromResult(instructions);
    }
}
