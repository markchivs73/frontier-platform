using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Audit;

/// <summary>
/// A versioned signing key for audit-record HMAC signatures (doc 05 §9). The
/// <see cref="KeyId"/> is recorded on each signed record as <c>SigningKeyId</c> so a
/// later key rotation never invalidates existing signatures.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Record/POCO with assignment-only constructor")]
public sealed record SigningKey(string KeyId, ReadOnlyMemory<byte> KeyMaterial);
