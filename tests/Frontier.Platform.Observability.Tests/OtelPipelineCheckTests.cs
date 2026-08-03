using Microsoft.Extensions.Configuration;

namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="OtelPipelineCheck"/> (doc 12 §6/§7).</summary>
public sealed class OtelPipelineCheckTests
{
    [Fact]
    public void Name_ReturnsOtelPipeline()
    {
        var check = new OtelPipelineCheck(BuildConfiguration(endpoint: null));

        Assert.Equal("OtelPipeline", check.Name);
    }

    [Fact]
    public async Task CheckAsync_EndpointUnset_ReturnsPass()
    {
        var check = new OtelPipelineCheck(BuildConfiguration(endpoint: null));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_EndpointIsValidAbsoluteUri_ReturnsPass()
    {
        var check = new OtelPipelineCheck(BuildConfiguration(endpoint: "https://collector.internal:4317"));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_EndpointIsMalformed_ReturnsFail()
    {
        var check = new OtelPipelineCheck(BuildConfiguration(endpoint: "not-a-uri"));

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains(OtelPipelineCheck.OtlpEndpointConfigurationKey, result.FailureReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_UnsetOrBlankEndpoint_ReturnsPass(string? endpoint)
    {
        var result = OtelPipelineCheck.Evaluate(endpoint);

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Evaluate_WellFormedAbsoluteUri_ReturnsPass()
    {
        var result = OtelPipelineCheck.Evaluate("https://collector.internal:4317");

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_RelativeOrMalformedUri_ReturnsFail()
    {
        var result = OtelPipelineCheck.Evaluate("not a uri");

        Assert.False(result.Passed);
        Assert.Contains("not a valid absolute URI", result.FailureReason, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(string? endpoint)
    {
        var values = new Dictionary<string, string?>();

        if (endpoint is not null)
        {
            values[OtelPipelineCheck.OtlpEndpointConfigurationKey] = endpoint;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
