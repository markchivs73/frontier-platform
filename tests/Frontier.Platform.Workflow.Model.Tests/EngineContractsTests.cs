using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model.Tests.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>
/// ADR-E27 D13: the engine-owned contracts go through the same registration check and validator as
/// any registry contract, so the hand-written schema cannot drift from the CLR type it describes.
/// </summary>
public sealed class EngineContractsTests
{
    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddFrontierContracts(new ConfigurationBuilder().Build())
        .BuildServiceProvider();

    [Fact]
    public void GateDecision_HashIsTheContractHashOfItsSchema()
    {
        var snapshot = EngineContracts.GateDecision;

        Assert.Equal(ContractHash.Compute(snapshot.Schema, snapshot.Rules), snapshot.Hash);
    }

    [Fact]
    public void GateDecision_PassesRegistrationAsAnAuthoredContract()
    {
        var snapshot = EngineContracts.GateDecision;
        var check = Services.GetRequiredService<IContractRegistrationCheck>();

        var result = check.Check(
            new ContractRegistrationRequest { ContractRef = snapshot.ContractRef, Schema = snapshot.Schema, Provenance = ContractProvenance.Authored },
            []);

        Assert.Empty(result.Findings);
        Assert.Equal(snapshot.Hash, result.Snapshot!.Hash);
    }

    [Theory]
    [MemberData(nameof(Decisions))]
    public void GateDecision_EveryDecisionTheEngineWritesSatisfiesTheSchema(GateDecision decision)
    {
        var validator = Services.GetRequiredService<IContractValidator>();

        var result = validator.Validate(EngineContracts.GateDecision, CanonicalProfile.SerializeCanonical(decision));

        Assert.True(result.IsValid, string.Join("; ", result.Violations.Select(v => $"{v.Location} {v.Message}")));
    }

    public static TheoryData<GateDecision> Decisions() => new()
    {
        ContractSamples.GateDecision(),
        ContractSamples.GateDecision() with { Decision = DecisionKind.Approve, Notes = null, RollbackToNodeId = null },
        ContractSamples.GateDecision() with { Decision = DecisionKind.Escalate, RollbackToNodeId = null },
    };

    [Fact]
    public void GateDecision_RefsMatch()
    {
        Assert.Equal(GateDecision.ContractRef, EngineContracts.GateDecision.ContractRef);
        Assert.Same(EngineContracts.GateDecision, EngineContracts.Find(GateDecision.ContractRef));
        Assert.Equal([EngineContracts.GateDecision], EngineContracts.All);
    }

    [Fact]
    public void Find_ReturnsNullForARefTheEngineDoesNotOwn()
    {
        Assert.Null(EngineContracts.Find("acme/thing/1.0"));
    }
}
