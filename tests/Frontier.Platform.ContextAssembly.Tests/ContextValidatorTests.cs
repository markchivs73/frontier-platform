using Frontier.Platform.Abstractions;
using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests;

public sealed class ContextValidatorTests
{
    [Fact]
    public void ValidateRequest_KnownComponents_ReturnsEmpty()
    {
        var validator = new ContextValidator();
        var catalogue = new BaselineCatalogue(
            CatalogueId: "2026.06.1",
            Components: new Dictionary<string, string>
            {
                { "component-a", "content-a" },
                { "component-b", "content-b" },
            });

        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = new[] { "component-a", "component-b" },
            DynamicFields = [],
            RequiresRealTime = false,
        };

        var errors = validator.ValidateRequest(request, catalogue);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRequest_UnknownComponent_ReturnsError()
    {
        var validator = new ContextValidator();
        var catalogue = new BaselineCatalogue(
            CatalogueId: "2026.06.1",
            Components: new Dictionary<string, string>
            {
                { "component-a", "content-a" },
            });

        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = new[] { "component-a", "unknown-component" },
            DynamicFields = [],
            RequiresRealTime = false,
        };

        var errors = validator.ValidateRequest(request, catalogue);

        Assert.Single(errors);
        Assert.Contains("unknown-component", errors[0], StringComparison.Ordinal);
        Assert.Contains("not found", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRequest_MultipleUnknown_ReturnsMultipleErrors()
    {
        var validator = new ContextValidator();
        var catalogue = new BaselineCatalogue(
            CatalogueId: "2026.06.1",
            Components: new Dictionary<string, string>
            {
                { "known", "content" },
            });

        var request = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = new[] { "known", "bad-1", "bad-2" },
            DynamicFields = [],
            RequiresRealTime = false,
        };

        var errors = validator.ValidateRequest(request, catalogue);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("not found", e, StringComparison.Ordinal));
    }
}
