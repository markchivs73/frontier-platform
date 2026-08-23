using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S4.2 tests for <see cref="ContractTypeRegistry"/>; extended at S9.28 when the registry
/// moved from a hardcoded four-entry dictionary to reflection over every
/// <see cref="IVersionedContract"/> in Abstractions.
/// </summary>
public sealed class ContractTypeRegistryTests
{
    private readonly ContractTypeRegistry registry = new(TestContractSet.Instance);

    [Theory]
    [InlineData(nameof(BriefArtifact), typeof(BriefArtifact))]
    [InlineData(nameof(SummaryArtifact), typeof(SummaryArtifact))]
    [InlineData(nameof(PlanArtifact), typeof(PlanArtifact))]
    [InlineData(nameof(RateCardArtifact), typeof(RateCardArtifact))]
    // S9.28: none of these four were hardcoded into the registry — proves discovery is
    // genuinely reflection-based, not a relabelled allowlist.
    [InlineData(nameof(LookupResult), typeof(LookupResult))]
    [InlineData(nameof(ScoredMatch), typeof(ScoredMatch))]
    [InlineData(nameof(UpdateResult), typeof(UpdateResult))]
    public void Resolve_KnownContractType_ReturnsClrType(string contractTypeName, Type expected)
    {
        var resolved = registry.Resolve(contractTypeName);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_DiscoversEveryIVersionedContractInAbstractions_WithNoManualRegistration()
    {
        // Regression guard for the S9.28 rewrite's whole point: adding a new contract type
        // to Abstractions must never require a registry edit. Cross-checks the registry
        // against an independent reflection pass over the same assembly.
        //
        // S13.12d: anchored on a workload contract. WorkflowDefinition would still compile here
        // and would quietly reflect over the *model package* instead — the assembly this test
        // exists to police is the workload's.
        var expectedNames = typeof(SummaryArtifact).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IVersionedContract).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var resolvedNames = expectedNames.Select(n => registry.Resolve(n).Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(expectedNames, resolvedNames);
    }

    [Fact]
    public void Resolve_UnknownContractType_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => registry.Resolve("UnknownArtifact"));

        Assert.Contains("UnknownArtifact", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhitespaceContractType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => registry.Resolve(" "));
    }

    [Fact]
    public void DeserializeAndValidate_ValidJson_ReturnsValidatedContract()
    {
        var json = """{"schema_version":"1.0","title":"Scope","objectives":["objective"]}""";

        var contract = registry.DeserializeAndValidate(nameof(SummaryArtifact), json);

        var scope = Assert.IsType<SummaryArtifact>(contract);
        Assert.Equal("Scope", scope.Title);
    }

    [Fact]
    public void DeserializeAndValidate_NullJson_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => registry.DeserializeAndValidate(nameof(SummaryArtifact), null!));
    }

    [Fact]
    public void DeserializeAndValidate_MalformedJson_ThrowsContractViolationException()
    {
        var exception = Assert.Throws<ContractViolationException>(() => registry.DeserializeAndValidate(nameof(SummaryArtifact), "not json"));

        Assert.Contains("payload was not valid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeAndValidate_JsonNullLiteral_ThrowsContractViolationException()
    {
        var exception = Assert.Throws<ContractViolationException>(() => registry.DeserializeAndValidate(nameof(SummaryArtifact), "null"));

        Assert.Contains("payload deserialized to null.", exception.Violations);
    }

    [Fact]
    public void DeserializeAndValidate_ValidJsonFailingValidation_ThrowsContractViolationException()
    {
        var json = """{"schema_version":"1.0","title":"","objectives":[]}""";

        var exception = Assert.Throws<ContractViolationException>(() => registry.DeserializeAndValidate(nameof(SummaryArtifact), json));

        Assert.Equal(nameof(SummaryArtifact), exception.ContractType);
    }
}
