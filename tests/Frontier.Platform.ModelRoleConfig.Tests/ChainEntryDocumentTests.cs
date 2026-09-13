using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA27 tests for <see cref="ChainEntryDocument"/>: both entry kinds, the absent-target migration, and model byte stability.</summary>
public sealed class ChainEntryDocumentTests
{
    private static readonly AgentEntry EchoAgent = new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/echo",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    /// <summary>The exact bytes a Phase-1 model entry was written with before ADR-PA27.</summary>
    private const string PrePa27ModelEntryJson =
        """{"provider":"anthropic","model_id":"claude-opus-4-8","input_cost_per_1k":"0.0050","output_cost_per_1k":"0.0250","cache_read_cost_per_1k":"0.0005","currency":"USD","context_window":200000,"max_output_tokens":16000}""";

    [Fact]
    public void FromDomain_ModelEntry_WritesExactlyThePrePa27Bytes()
    {
        var document = ChainEntryDocument.FromDomain(Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0]);

        Assert.Equal(PrePa27ModelEntryJson, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(document)));
    }

    [Fact]
    public void ToDomain_StoredEntryWithoutTarget_ReadsAsModel()
    {
        // The migration adapter: every mapping written before ADR-PA27 has no target.
        var entry = Deserialize(PrePa27ModelEntryJson).ToDomain();

        Assert.Equal(Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0], entry);
    }

    [Fact]
    public void ToDomain_ExplicitModelTarget_ReadsAsModel()
    {
        var entry = (Deserialize(PrePa27ModelEntryJson) with { Target = ChainEntryDocument.ModelTarget }).ToDomain();

        Assert.IsType<ModelEntry>(entry);
    }

    [Fact]
    public void RoundTrip_AzureOpenAiModel_KeepsEndpointAndDeployment()
    {
        var azure = (ModelEntry)Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0] with
        {
            Provider = "azure-openai",
            ModelId = "gpt-4o",
            Endpoint = new Uri("https://agentic-workflow-agents.openai.azure.com/"),
            Deployment = "gpt-4o-prod",
        };

        var roundTripped = Deserialize(Serialize(ChainEntryDocument.FromDomain(azure))).ToDomain();

        Assert.Equal(azure, roundTripped);
    }

    [Fact]
    public void RoundTrip_AgentEntry_WritesTargetAndOnlyAgentFields()
    {
        var json = Serialize(ChainEntryDocument.FromDomain(EchoAgent));

        Assert.Equal(
            """{"provider":"a2a","currency":"USD","target":"agent","resource_name":"com.azure.foundry/echo","resource_version":"1.0","cost_per_invocation":"0.0200"}""",
            json);
        Assert.Equal(EchoAgent, Deserialize(json).ToDomain());
    }

    [Fact]
    public void ToDomain_UnknownTarget_IsAContractViolation()
    {
        var document = ChainEntryDocument.FromDomain(EchoAgent) with { Target = "robot" };

        var exception = Assert.Throws<ContractViolationException>(() => document.ToDomain());

        Assert.Contains("target 'robot'", Assert.Single(exception.Violations), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("model_id")]
    [InlineData("input_cost_per_1k")]
    [InlineData("output_cost_per_1k")]
    [InlineData("cache_read_cost_per_1k")]
    [InlineData("context_window")]
    [InlineData("max_output_tokens")]
    public void ToDomain_ModelMissingAField_NamesTheField(string field)
    {
        var complete = ChainEntryDocument.FromDomain(Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0]);
        var document = field switch
        {
            "model_id" => complete with { ModelId = null },
            "input_cost_per_1k" => complete with { InputCostPer1k = null },
            "output_cost_per_1k" => complete with { OutputCostPer1k = null },
            "cache_read_cost_per_1k" => complete with { CacheReadCostPer1k = null },
            "context_window" => complete with { ContextWindow = null },
            _ => complete with { MaxOutputTokens = null },
        };

        var exception = Assert.Throws<ContractViolationException>(() => document.ToDomain());

        Assert.StartsWith(field, Assert.Single(exception.Violations), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("resource_name")]
    [InlineData("resource_version")]
    [InlineData("cost_per_invocation")]
    public void ToDomain_AgentMissingAField_NamesTheField(string field)
    {
        var complete = ChainEntryDocument.FromDomain(EchoAgent);
        var document = field switch
        {
            "resource_name" => complete with { ResourceName = null },
            "resource_version" => complete with { ResourceVersion = null },
            _ => complete with { CostPerInvocation = null },
        };

        var exception = Assert.Throws<ContractViolationException>(() => document.ToDomain());

        Assert.StartsWith(field, Assert.Single(exception.Violations), StringComparison.Ordinal);
    }

    private static string Serialize(ChainEntryDocument document) => Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(document));

    private static ChainEntryDocument Deserialize(string json) => JsonSerializer.Deserialize<ChainEntryDocument>(json, CanonicalProfile.Options)!;
}
