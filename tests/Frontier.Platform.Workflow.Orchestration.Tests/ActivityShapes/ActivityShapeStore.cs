using System.Text.Encodings.Web;
using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// Reads and writes the committed snapshot. Bytes go through the canonical profile and are then
/// indented, so the file diffs line by line in review; the relaxed encoder keeps the
/// <c>&lt;variant&gt;</c> path segments readable in a file nothing embeds in HTML.
/// </summary>
internal static class ActivityShapeStore
{
    /// <summary>The snapshot's path from the repository root.</summary>
    public const string RepositoryPath = "tests/Frontier.Platform.Workflow.Orchestration.Tests/ActivityShapes/activity-shapes.json";

    /// <summary>The environment variable CI sets to the base branch's snapshot on a pull request.</summary>
    public const string BaselineVariable = "FRONTIER_ACTIVITY_SHAPES_BASELINE";

    /// <summary>The environment variable that asks the snapshot test to rewrite the file: <c>update</c> or <c>bump</c>.</summary>
    public const string ModeVariable = "FRONTIER_ACTIVITY_SHAPES";

    /// <summary>The committed snapshot as copied beside the test assembly.</summary>
    public static string CommittedPath => Path.Combine(AppContext.BaseDirectory, "ActivityShapes", "activity-shapes.json");

    /// <summary>Loads the snapshot at <paramref name="path"/>.</summary>
    public static ActivityShapeSnapshot Load(string path) => Deserialize(File.ReadAllBytes(path));

    /// <summary>Parses snapshot bytes.</summary>
    public static ActivityShapeSnapshot Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<ActivityShapeSnapshot>(bytes, CanonicalProfile.Options)
        ?? throw new InvalidOperationException("The activity-shape snapshot is empty.");

    /// <summary>The snapshot's file bytes: canonical, indented, newline-terminated.</summary>
    public static byte[] Serialize(ActivityShapeSnapshot snapshot)
    {
        using var document = JsonDocument.Parse(CanonicalProfile.SerializeCanonical(snapshot));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            document.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>
    /// Writes <paramref name="snapshot"/> over the source file, found by walking up from
    /// <paramref name="from"/> to the solution, and returns the path written.
    /// </summary>
    public static string WriteSource(ActivityShapeSnapshot snapshot, string from)
    {
        var directory = new DirectoryInfo(from);
        while (!File.Exists(Path.Combine(directory.FullName, "FrontierPlatform.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("FrontierPlatform.slnx not found above the test assembly.");
        }

        var path = Path.Combine(directory.FullName, RepositoryPath);
        File.WriteAllBytes(path, Serialize(snapshot));
        return path;
    }
}
