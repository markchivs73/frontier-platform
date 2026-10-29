using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>A contract submitted for registration.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain input record; exercised by the registration check's tests.")]
public sealed record ContractRegistrationRequest
{
    /// <summary>The ref it is to be registered under, <c>{namespace}/{name}/{major}.{minor}</c>.</summary>
    public required string ContractRef { get; init; }

    /// <summary>The JSON Schema 2020-12 document.</summary>
    public required JsonElement Schema { get; init; }

    /// <summary>The CEL rules; empty when there are none.</summary>
    public IReadOnlyList<ContractRule> Rules { get; init; } = [];

    /// <summary>How the contract reached the registry.</summary>
    public required ContractProvenance Provenance { get; init; }
}
