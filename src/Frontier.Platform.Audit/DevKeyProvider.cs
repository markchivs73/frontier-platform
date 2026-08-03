using System.Text;

namespace Frontier.Platform.Audit;

/// <summary>
/// Local-development <see cref="IKeyProvider"/> returning a fixed, well-known key so
/// audit signing (doc 05 §9) runs the same code path locally as it will against Key
/// Vault — Stage 5 adds the Key Vault-backed implementation behind this same interface.
/// </summary>
internal sealed class DevKeyProvider : IKeyProvider
{
    private static readonly byte[] DevKeyMaterial = Encoding.UTF8.GetBytes("frontier-workflow-dev-signing-key");

    public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SigningKey("dev-key/v1", DevKeyMaterial));
}
