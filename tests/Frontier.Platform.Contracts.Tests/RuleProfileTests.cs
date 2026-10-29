using Celly;

namespace Frontier.Platform.Contracts.Tests;

public sealed class RuleProfileTests
{
    [Theory]
    [InlineData("'a'.matches('a')", 1)]
    [InlineData("timestamp('2026-01-01T00:00:00Z').getFullYear('UTC') == 2026 && timestamp('2026-01-01T00:00:00Z').getMonth('UTC') == 0", 2)]
    [InlineData("timestamp('2026-01-01T00:00:00Z').getFullYear() == 2026", 0)]
    [InlineData("size('abc') == 3", 0)]
    public void Violations_NameEachCallOutsideTheProfile(string expression, int count) =>
        Assert.Equal(count, RuleProfile.Violations(CelEnv.Create(new CelEnvSettings()).Parse(expression).Ast!.Expr).Count());
}
