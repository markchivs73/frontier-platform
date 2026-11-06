namespace Frontier.Platform.Hitl.Tests;

/// <summary>
/// An in-memory approval document whose version bumps on every read. A scripted number of replaces lose
/// their version check, and a losing replace can install <see cref="WinnerOnLoss"/> — the competing
/// writer's record — so a test can drive a race to either side.
/// </summary>
internal sealed class ScriptedApprovalDocuments(ApprovalRequest? stored) : IApprovalDocuments
{
    private int reads;

    /// <summary>The request as stored now.</summary>
    internal ApprovalRequest? Stored { get; private set; } = stored;

    /// <summary>How many replaces lose their version check before one may win.</summary>
    internal int LosingReplaces { get; init; }

    /// <summary>The record a competing writer stored when a replace lost, if any.</summary>
    internal ApprovalRequest? WinnerOnLoss { get; init; }

    /// <summary>The version each replace was attempted at.</summary>
    internal List<string> ReplaceVersions { get; } = [];

    /// <inheritdoc />
    public Task<VersionedApproval?> ReadAsync(string engagementId, string approvalId, CancellationToken cancellationToken)
    {
        reads++;
        return Task.FromResult(Stored is null ? null : new VersionedApproval(Stored, $"v{reads}"));
    }

    /// <inheritdoc />
    public Task<bool> TryReplaceAsync(ApprovalRequest request, string expectedVersion, CancellationToken cancellationToken)
    {
        ReplaceVersions.Add(expectedVersion);
        if (ReplaceVersions.Count <= LosingReplaces)
        {
            Stored = WinnerOnLoss ?? Stored;
            return Task.FromResult(false);
        }

        Stored = request;
        return Task.FromResult(true);
    }
}
