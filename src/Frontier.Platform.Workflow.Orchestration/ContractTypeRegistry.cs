using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <see cref="IContractTypeRegistry"/> over the workload contract set supplied at
/// composition (<see cref="IContractTypeSet"/>, E16 option 2 — S13.12c).
///
/// Originally a hardcoded four-entry dictionary, then (S9.28) a blanket reflection sweep of
/// <c>the workload's contract set (IContractTypeSet)</c> so a new contract needed no registry edit.
/// The convenience is preserved — the Host still discovers by reflection — but the *anchor*
/// moved to the composition root: an engine package cannot know which assembly holds its
/// consumer's contracts (ADR-E3a; the coupling E3b's interpreter move must break).
/// </summary>
internal sealed class ContractTypeRegistry : IContractTypeRegistry
{
    private readonly IContractTypeSet contractTypes;

    /// <summary>Constructs the registry over the composition-supplied contract set.</summary>
    public ContractTypeRegistry(IContractTypeSet contractTypes)
    {
        ArgumentNullException.ThrowIfNull(contractTypes);
        this.contractTypes = contractTypes;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A name the workload's contract set does not hold is a <see cref="ContractViolationException"/>
    /// (S13.274): the definition names a type this Host cannot bind, which no retry can change, so it is
    /// classified <c>contract_violation</c> and never retried (doc 10 §3, invariant 7).
    /// </remarks>
    public Type Resolve(string contractTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractTypeName);

        return contractTypes.Resolve(contractTypeName)
            ?? throw new ContractViolationException(contractTypeName, [$"Unknown contract type '{contractTypeName}'; the workload's contract set does not name it."]);
    }

    /// <inheritdoc />
    public IVersionedContract DeserializeAndValidate(string contractTypeName, string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var contract = Deserialize(contractTypeName, json);
        contract.Validate();
        return contract;
    }

    /// <summary>Deserializes <paramref name="json"/> as <paramref name="contractTypeName"/>'s CLR type, wrapping malformed JSON as <see cref="ContractViolationException"/>.</summary>
    internal IVersionedContract Deserialize(string contractTypeName, string json)
    {
        var type = Resolve(contractTypeName);

        try
        {
            return (IVersionedContract?)JsonSerializer.Deserialize(json, type, CanonicalProfile.Options)
                ?? throw new ContractViolationException(contractTypeName, ["payload deserialized to null."]);
        }
        catch (JsonException ex)
        {
            throw new ContractViolationException($"{contractTypeName}: payload was not valid JSON.", ex);
        }
    }
}
