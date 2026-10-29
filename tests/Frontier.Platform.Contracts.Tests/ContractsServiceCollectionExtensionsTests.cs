using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts.Tests;

public sealed class ContractsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFrontierContracts_RegistersTheValidatorAndTheCheck_OverOneEngine()
    {
        using var provider = Provider([]);

        var validator = provider.GetRequiredService<IContractValidator>();
        var check = provider.GetRequiredService<IContractRegistrationCheck>();

        var snapshot = check.Check(new ContractRegistrationRequest { ContractRef = "frontier/a/1.0", Schema = Json.Parse(Json.LegibleSchema), Provenance = ContractProvenance.Authored }, []).Snapshot!;
        Assert.True(validator.Validate(snapshot, Json.Parse("""{ "total": 1 }""")).IsValid);
    }

    [Fact]
    public void AddFrontierContracts_BindsTheContractsSection()
    {
        using var provider = Provider(new() { ["Contracts:MaxPayloadDepth"] = "8", ["Contracts:MaxViolations"] = "5", ["Contracts:RegexMatchTimeoutMilliseconds"] = "50" });

        var options = provider.GetRequiredService<IOptions<ContractsOptions>>().Value;

        Assert.Equal((8, 5, 50), (options.MaxPayloadDepth, options.MaxViolations, options.RegexMatchTimeoutMilliseconds));
    }

    [Theory]
    [InlineData("Contracts:MaxPayloadDepth", "0")]
    [InlineData("Contracts:MaxViolations", "0")]
    [InlineData("Contracts:RegexMatchTimeoutMilliseconds", "0")]
    public void AddFrontierContracts_RefusesAnOutOfRangeLimit(string key, string value)
    {
        using var provider = Provider(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ContractsOptions>>().Value);
    }

    [Fact]
    public void AddFrontierContracts_NullConfiguration_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierContracts(null!));

    private static ServiceProvider Provider(Dictionary<string, string?> values) =>
        new ServiceCollection()
            .AddFrontierContracts(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
}
