using System.Reflection;
using System.Xml.Linq;

namespace Frontier.Platform.Workflow.Compiler.Schema;

/// <summary>
/// Reads XML-doc <c>&lt;summary&gt;</c> text from a compiled assembly's documentation file so the
/// schema generator can surface node/field intent as descriptions (doc 14 §7). Missing or
/// unreadable doc files degrade gracefully to no descriptions — descriptions are advisory.
/// </summary>
internal sealed class XmlDocReader
{
    private readonly IReadOnlyDictionary<string, string> _summaries;

    internal XmlDocReader(IReadOnlyDictionary<string, string> summaries) => _summaries = summaries;

    /// <summary>Builds a reader from the documentation file sitting beside the given assembly.</summary>
    internal static XmlDocReader ForAssembly(Assembly assembly) =>
        new(Parse(Path.ChangeExtension(assembly.Location, ".xml")));

    /// <summary>Parses a doc file into a member-name → normalised-summary map; empty if the file is absent.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Dictionary<string, string>();

        return XDocument.Load(path).Descendants("member")
            .Where(m => m.Attribute("name") is not null && m.Element("summary") is not null)
            .ToDictionary(m => m.Attribute("name")!.Value, m => Normalize(m.Element("summary")!.Value));
    }

    /// <summary>Collapses runs of whitespace (including newlines and indentation) to single spaces.</summary>
    internal static string Normalize(string raw) =>
        string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The summary for a type, or <c>null</c> if undocumented.</summary>
    internal string? TypeSummary(Type type) => Lookup($"T:{type.FullName}");

    /// <summary>The summary for a property on its declaring type, or <c>null</c> if undocumented.</summary>
    internal string? PropertySummary(Type declaringType, string name) =>
        Lookup($"P:{declaringType.FullName}.{name}");

    /// <summary>Returns the normalised summary for a doc member key, or <c>null</c> if absent.</summary>
    internal string? Lookup(string key) => _summaries.TryGetValue(key, out var value) ? value : null;
}
