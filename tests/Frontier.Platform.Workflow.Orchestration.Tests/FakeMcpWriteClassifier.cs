namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Test double for <see cref="IMcpWriteClassifier"/>. Honours the port's purity contract: the
/// answer is a total function of the tool reference, fixed at construction, so a replayed
/// orchestration sees the same classification every time.
/// </summary>
internal sealed class FakeMcpWriteClassifier(params string[] writeToolWireRefs) : IMcpWriteClassifier
{
    private readonly HashSet<string> _writes = new(writeToolWireRefs, StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsWrite(McpToolRef toolRef) => _writes.Contains(toolRef.ToWireReference());
}
