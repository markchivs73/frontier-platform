using System.ComponentModel.DataAnnotations;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The deployment's tenant, stamped inside every signed audit record, execution and governance alike (consumer
/// S13.293, ADR-PA54; doc 15 §6). Bound from the <c>Audit</c> section. One deployment is one tenant (ADR-SEC2), so
/// this is a constant the consumer sets to the same value its claims transformation mints as <c>frontier:tenant</c>.
/// Unset, empty or whitespace means no stamp, and records are written exactly as before the field existed; a record
/// that carries a tenant of its own is then refused, since the deployment cannot vouch for it.
/// </summary>
public sealed class AuditTenantOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Audit";

    /// <summary>The tenant to stamp; null, empty or whitespace stamps nothing.</summary>
    [MaxLength(AuditTenantStamp.MaxLength)]
    public string? Tenant { get; init; }
}

/// <summary>Decides the tenant a record is signed with (S13.293): the deployment's or none, never one it cannot vouch for.</summary>
internal static class AuditTenantStamp
{
    /// <summary>The longest tenant accepted.</summary>
    internal const int MaxLength = 128;

    /// <summary>
    /// The tenant to sign with: the deployment's, or none. Null, empty and whitespace mean "no tenant" on both sides.
    /// With no deployment tenant, a record must carry none either. With one, a record carrying none is stamped and one
    /// carrying the same value is accepted. Anything else (a different tenant, a tenant the deployment does not have,
    /// or one longer than <see cref="MaxLength"/>) is a permanent <see cref="ContractViolationException"/>: a record
    /// must never claim an origin this deployment cannot vouch for.
    /// </summary>
    internal static string? Resolve(string? recordTenant, string? deploymentTenant)
    {
        var deployment = Normalise(deploymentTenant);
        var record = Normalise(recordTenant);
        if (record is null || string.Equals(record, deployment, StringComparison.Ordinal)) return deployment;

        throw new ContractViolationException(nameof(AuditRecord),
            [$"tenant '{record}' is not this deployment's tenant ('{deployment ?? "none"}'; ADR-SEC2: one tenant per deployment)."]);
    }

    /// <summary>Null for null, empty or whitespace; otherwise the value, refused when longer than <see cref="MaxLength"/>.</summary>
    internal static string? Normalise(string? tenant) =>
        string.IsNullOrWhiteSpace(tenant) ? null
        : tenant.Length <= MaxLength ? tenant
        : throw new ContractViolationException(nameof(AuditRecord), [$"tenant is longer than {MaxLength} characters."]);
}
