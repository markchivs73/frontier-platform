using Frontier.Platform.Workflow.Compiler.Rules;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.34, added alongside the specification suite: the rule's argument guard, which the
/// specification tests do not exercise. Nothing here relaxes anything they pin.
/// </summary>
public sealed class SchemaVersionSupportedRuleGuardTests
{
    [Fact]
    public async Task NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new SchemaVersionSupportedRule().EvaluateAsync(null!, CancellationToken.None));
}
