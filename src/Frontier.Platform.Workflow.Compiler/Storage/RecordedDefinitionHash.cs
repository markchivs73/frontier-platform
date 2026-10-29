using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// Fills the definition hash on versions published before publish stamped it (S13.138). Their
/// inline definition kept the draft's empty <c>definition_hash</c>, so a run started from one
/// carried no hash and its audit record said nothing about which definition ran. The version
/// document has always recorded the computed hash; a read copies it onto the definition. The
/// stored document is never rewritten (doc 01 §5, adapt on read).
/// </summary>
internal static class RecordedDefinitionHash
{
    /// <summary>
    /// <paramref name="version"/>, with its definition carrying the version's recorded hash if the
    /// definition's own field is empty. A definition that names a hash is left exactly as stored.
    /// </summary>
    internal static DefinitionVersionDocument Fill(DefinitionVersionDocument version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return string.IsNullOrEmpty(version.Definition.DefinitionHash)
            ? version with { Definition = version.Definition with { DefinitionHash = version.DefinitionHash } }
            : version;
    }
}
