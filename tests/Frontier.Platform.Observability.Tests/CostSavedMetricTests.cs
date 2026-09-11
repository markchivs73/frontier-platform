namespace Frontier.Platform.Observability.Tests;

/// <summary>ADR-PA21: the cache cost-saving metric names no currency; the currency is an attribute, so series never sum across currencies.</summary>
public sealed class CostSavedMetricTests
{
    [Fact]
    public void CostSaved_HasStaticUnitAndCurrencyAttribute()
    {
        var metric = Phase1MetricCatalogue.BuildCatalogue().Single(m => m.Name == "context.cost.saved");

        Assert.Equal("{cost}", metric.Unit);
        Assert.Equal(["engagement_type", "currency"], metric.Dimensions);
    }

    [Fact]
    public void Catalogue_NoMetricNamesACurrency()
    {
        var names = Phase1MetricCatalogue.BuildCatalogue().Select(m => m.Name);

        Assert.DoesNotContain(names, name => name.EndsWith("_gbp", StringComparison.Ordinal) || name.EndsWith("_usd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCacheEconomicsAsync_NoRows_HasNoCurrency()
    {
        var result = await new Phase1EmpiricalQueryService().GetCacheEconomicsAsync(new EmpiricalScope(null, null, null, null, null), CancellationToken.None);

        Assert.Null(result.Currency);
    }
}
