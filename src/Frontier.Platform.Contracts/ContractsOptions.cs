using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The validator's limits: governance data (K7), bound from the <c>Contracts</c> configuration
/// section. Every default is safe to run with.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Options POCO; its bounds are exercised by the registration extension's tests.")]
public sealed class ContractsOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "Contracts";

    /// <summary>The deepest nesting a payload may have; deeper is a violation, never a crash.</summary>
    [Range(1, 256)]
    public int MaxPayloadDepth { get; set; } = 64;

    /// <summary>The most violations reported for one payload. Validation stops counting there.</summary>
    [Range(1, 1000)]
    public int MaxViolations { get; set; } = 20;

    /// <summary>
    /// How long one schema <c>pattern</c> match may run, in milliseconds. A timeout is a permanent
    /// violation (ADR-E27 D3). It is the one wall-clock limit, and it only ever refuses a payload.
    /// </summary>
    [Range(1, 10_000)]
    public int RegexMatchTimeoutMilliseconds { get; set; } = 1_000;
}
