using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// The merge rule behind <see cref="IEngagementContextStore.MergeDynamicContextAsync"/> (S13.62,
/// ADR-PA24), expressed once so every store implements the same semantics rather than three
/// read-modify-write loops that drift.
/// <para>
/// <b>Why a merge exists at all.</b> <see cref="IEngagementContextStore.UpsertDynamicContextAsync"/>
/// replaces the whole document, but an ADR-CR1 refresh is scoped — doc 18 §3 raises it for named
/// components and doc 04 §8's signal carries <c>changed_fields</c>, not a whole context. Routing a
/// scoped refresh through the replacing primitive deletes every key the refresh did not produce,
/// and <c>ContextContentFilter</c> throws <see cref="ContractViolationException"/> for a requested
/// key the document no longer holds — a <em>permanent</em> failure per hard invariant 7, with no
/// retry to recover it.
/// </para>
/// </summary>
public static class EngagementContextMerge
{
    /// <summary>
    /// Returns <paramref name="currentJson"/> with each entry of <paramref name="components"/>
    /// written under its own key, as canonical JSON. Keys the merge does not name survive
    /// byte-identically; a named key is replaced <b>wholesale</b>, never merged into recursively —
    /// the primitive's unit is the component, and a stub→enriched transition legitimately
    /// <em>removes</em> fields a recursive merge would leave behind.
    /// </summary>
    /// <param name="currentJson">The engagement's current dynamic context, or <see langword="null"/> if it holds none.</param>
    /// <param name="components">Component key → its rendered canonical JSON.</param>
    public static string Apply(string? currentJson, IReadOnlyDictionary<string, string> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CopyInto(merged, currentJson);

        foreach (var (name, content) in components)
        {
            using var rendered = JsonDocument.Parse(content);
            merged[name] = rendered.RootElement.Clone();
        }

        return JsonSerializer.Serialize(merged, CanonicalProfile.Options);
    }

    /// <summary>Copies every top-level property of <paramref name="json"/> into <paramref name="target"/>; a null document contributes nothing.</summary>
    internal static void CopyInto(Dictionary<string, JsonElement> target, string? json)
    {
        if (json is null)
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            target[property.Name] = property.Value.Clone();
        }
    }

    /// <summary>
    /// Applies <see cref="Apply"/> through <paramref name="store"/>'s own public surface and
    /// returns the epoch the document is on afterwards. Byte-identity governs the epoch
    /// (ADR-EC1, doc 04 §8): a merge that changes nothing writes nothing and reports the current
    /// epoch, so a primed provider cache stays primed. Offered publicly so an out-of-repo
    /// <see cref="IEngagementContextStore"/> can satisfy the new member in one line.
    /// </summary>
    /// <param name="store">The store to read and write through.</param>
    /// <param name="engagementId">The engagement whose dynamic context is being merged into.</param>
    /// <param name="components">Component key → its rendered canonical JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<int> ApplyThroughAsync(
        IEngagementContextStore store,
        EngagementId engagementId,
        IReadOnlyDictionary<string, string> components,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        var current = await store.GetDynamicContextSnapshotAsync(engagementId, null, ct);
        var merged = Apply(current?.Content, components);

        return current is not null && string.Equals(current.Content, merged, StringComparison.Ordinal)
            ? current.Epoch
            : await store.UpsertDynamicContextAsync(engagementId, merged, ct);
    }
}
