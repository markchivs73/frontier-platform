using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// One CEL rule on a registry contract (ADR-E27 D4): a boolean expression over the payload, bound
/// as <c>self</c>, with the message shown when it is false. The Kubernetes
/// <c>x-kubernetes-validations</c> shape.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by the contract hash and registration tests.")]
public sealed record ContractRule
{
    /// <summary>The CEL expression. It must evaluate to a boolean; <c>true</c> means the payload passes.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("rule")]
    public required string Rule { get; init; }

    /// <summary>What a reader is told when the rule is false. Required for an authored contract (ADR-E27 D10).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>The RFC 6901 pointer a failure is reported against, when the rule is about one field.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("field_path")]
    public string? FieldPath { get; init; }
}
