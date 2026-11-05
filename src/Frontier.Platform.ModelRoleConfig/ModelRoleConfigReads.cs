namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The decisions the <c>model-role-config</c> stores make over what they read (ADR-PA32, ADR-PA40): which row of a
/// query answers a lookup, and what a pointer to a missing version means. Kept apart from the coverage-excluded
/// stores (S13.193) so the unit gate measures them.
/// </summary>
internal static class ModelRoleConfigReads
{
    /// <summary>
    /// The first row of <paramref name="rows"/> that <paramref name="resolve"/> turns into a value; a row it resolves to
    /// <see langword="null"/> (a proposal deleted between the query and its point read) is skipped. <see langword="null"/>
    /// when no row resolves.
    /// </summary>
    internal static async Task<TResult?> FirstResolvedAsync<TRow, TResult>(
        IAsyncEnumerable<TRow> rows, Func<TRow, Task<TResult?>> resolve, CancellationToken cancellationToken)
        where TResult : class
    {
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            if (await resolve(row) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// The definition a role's <c>current</c> pointer names: <see langword="null"/> when there is no pointer
    /// (<paramref name="pointerRef"/> is <see langword="null"/>), else what <paramref name="readVersion"/> returns for it.
    /// A pointer to a version that is not stored is corruption, not absence, so it throws a
    /// <see cref="MappingGovernanceException"/> rather than answering "no definition".
    /// </summary>
    internal static async Task<RoleDefinition?> FollowDefinitionPointerAsync(
        string roleId, string? pointerRef, Func<string, Task<RoleDefinition?>> readVersion) =>
        pointerRef is null
            ? null
            : await readVersion(pointerRef)
                ?? throw new MappingGovernanceException($"Role '{roleId}' has a definition pointer to '{pointerRef}', which is not stored.");
}
