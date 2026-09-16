namespace Frontier.Platform.Audit;

/// <summary>
/// Splits a configured Key Vault key identifier into the two parts the SDK clients need
/// (ADR-PA33): the vault's base address and the key's name. Extracted from the SDK adapter
/// precisely because it is the one part of that adapter that makes a decision, and a decision
/// belongs where a test can reach it.
/// </summary>
internal static class KeyVaultKeyUri
{
    /// <summary>
    /// The vault base address of <paramref name="keyUri"/>, for example
    /// <c>https://frontier-kv.vault.azure.net/</c>.
    /// </summary>
    internal static Uri VaultUri(string keyUri) =>
        new(new Uri(keyUri, UriKind.Absolute).GetLeftPart(UriPartial.Authority), UriKind.Absolute);

    /// <summary>
    /// The key name in <paramref name="keyUri"/> — the segment after <c>/keys/</c>, with any
    /// trailing version ignored so a versionless and a versioned identifier both name the same key.
    /// </summary>
    internal static string KeyName(string keyUri)
    {
        var segments = new Uri(keyUri, UriKind.Absolute).Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => segment.Length > 0)
            .ToArray();

        var keysIndex = Array.IndexOf(segments, "keys");

        return keysIndex >= 0 && keysIndex + 1 < segments.Length
            ? segments[keysIndex + 1]
            : throw new InvalidOperationException(
                $"'{keyUri}' is not a Key Vault key identifier: expected 'https://{{vault}}.vault.azure.net/keys/{{name}}' (ADR-PA33).");
    }
}
