namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>A fixed worker build stamp, so tests can assert the pipelines record what the port supplies (S13.182).</summary>
internal sealed class TestHostBuildStamp : IHostBuildStamp
{
    /// <summary>The stamp every test pipeline records.</summary>
    internal const string Value = "9.8.7+worker1234";

    /// <summary>The shared instance.</summary>
    internal static TestHostBuildStamp Instance { get; } = new();

    /// <inheritdoc />
    public string Version => Value;
}
