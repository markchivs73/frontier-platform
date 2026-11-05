using System.Text;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>S13.185: the snapshot file's format.</summary>
public sealed class ActivityShapeStoreTests
{
    [Fact]
    public void Serialize_IsIndentedReadableAndRoundTrips()
    {
        var snapshot = ActivityShapeStore.Load(ActivityShapeStore.CommittedPath);

        var bytes = ActivityShapeStore.Serialize(snapshot);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.Contains("\n  \"generations\": [", text, StringComparison.Ordinal);
        Assert.Contains("<agent_task>", text, StringComparison.Ordinal);
        Assert.Equal(bytes, ActivityShapeStore.Serialize(ActivityShapeStore.Deserialize(bytes)));
    }

    [Fact]
    public void CommittedFile_IsInTheWrittenFormat() =>
        Assert.Equal(
            File.ReadAllBytes(ActivityShapeStore.CommittedPath),
            ActivityShapeStore.Serialize(ActivityShapeStore.Load(ActivityShapeStore.CommittedPath)));

    [Fact]
    public void Deserialize_AnEmptyDocument_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ActivityShapeStore.Deserialize("null"u8.ToArray()));

    [Fact]
    public void WriteSource_WritesTheSnapshotBesideTheSolutionAboveTheStart()
    {
        var root = Directory.CreateTempSubdirectory("activity-shapes-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "FrontierPlatform.slnx"), "<Solution />");
            var start = Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "bin"));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root.FullName, ActivityShapeStore.RepositoryPath))!);
            var snapshot = ActivityShapeStore.Load(ActivityShapeStore.CommittedPath);

            var path = ActivityShapeStore.WriteSource(snapshot, start.FullName);

            Assert.Equal(Path.Combine(root.FullName, ActivityShapeStore.RepositoryPath), path);
            Assert.Equal(ActivityShapeStore.Serialize(snapshot), File.ReadAllBytes(path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void WriteSource_WithNoSolutionAbove_Throws()
    {
        var root = Directory.CreateTempSubdirectory("activity-shapes-");
        try
        {
            var snapshot = ActivityShapeStore.Load(ActivityShapeStore.CommittedPath);

            Assert.Throws<InvalidOperationException>(() => ActivityShapeStore.WriteSource(snapshot, root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
