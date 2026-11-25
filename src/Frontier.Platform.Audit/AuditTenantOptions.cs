using System.ComponentModel.DataAnnotations;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The deployment's tenant, stamped inside every signed audit record, execution and governance alike (consumer
/// S13.293, ADR-PA54; doc 15 §6). Bound from the <c>Audit</c> section. One deployment is one tenant (ADR-SEC2), so
/// this is a constant the consumer sets to the same value its claims transformation mints as <c>frontier:tenant</c>.
/// Unset, empty or whitespace means no stamp, and records are written exactly as before the field existed.
/// </summary>
public sealed class AuditTenantOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Audit";

    /// <summary>The tenant to stamp; null, empty or whitespace stamps nothing.</summary>
    [MaxLength(AuditTenantStamp.MaxLength)]
    public string? Tenant { get; init; }
}

/// <summary>Decides the tenant a record is signed with (S13.293): the deployment's, never a different one.</summary>
internal static class AuditTenantStamp
{
    /// <summary>The longest tenant accepted.</summary>
    internal const int MaxLength = 128;

    /// <summary>
    /// The tenant to sign with. With no deployment tenant the record's own (possibly none) is kept. With one, a record
    /// carrying none is stamped, the same value is accepted, and a different value is a permanent
    /// <see cref="ContractViolationException"/>: a record must never claim another tenant's origin.
    /// </summary>
    internal static string? Resolve(string? recordTenant, string? deploymentTenant)
    {
        var deployment = string.IsNullOrWhiteSpace(deploymentTenant) ? null : deploymentTenant;
        if (deployment is null || recordTenant is null) return recordTenant ?? deployment;
        if (string.Equals(recordTenant, deployment, StringComparison.Ordinal)) return deployment;

        throw new ContractViolationException(nameof(AuditRecord),
            [$"tenant '{recordTenant}' is not this deployment's tenant '{deployment}' (ADR-SEC2: one tenant per deployment)."]);
    }
}
