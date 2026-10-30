using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Registry contract snapshots for the engine's validation tests, hashed as registration would hash them.</summary>
internal static class RegistryContractFixtures
{
    /// <summary><c>acme/ticket/1.0</c>: a closed object with a required non-empty id and a rule on its priority.</summary>
    internal static ContractSnapshot Ticket { get; } = Snapshot(
        "acme/ticket/1.0",
        """{"type":"object","description":"A ticket.","properties":{"id":{"type":"string","minLength":1,"description":"Id."},"priority":{"type":"number","description":"Priority."}},"required":["id","priority"],"additionalProperties":false}""",
        [new ContractRule { Rule = "self.priority >= 0.0", Message = "Priority is never negative.", FieldPath = "/priority" }]);

    /// <summary>Builds a snapshot of <paramref name="schema"/> and <paramref name="rules"/> with its real hash.</summary>
    internal static ContractSnapshot Snapshot(string contractRef, string schema, IReadOnlyList<ContractRule>? rules = null)
    {
        using var document = JsonDocument.Parse(schema);
        var element = document.RootElement.Clone();
        return new ContractSnapshot { ContractRef = contractRef, Schema = element, Rules = rules ?? [], Hash = ContractHash.Compute(element, rules ?? []) };
    }
}
