namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 (S4-13) tests for <see cref="RoleIdFormat"/>: lowercase kebab-case, at most 64 characters, no <c>:</c>.</summary>
public sealed class RoleIdFormatTests
{
    [Theory]
    [InlineData("deep-reasoning")]
    [InlineData("fast")]
    [InlineData("structured-extraction")]
    [InlineData("embeddings")]
    [InlineData("gen-record-pass2")]
    [InlineData("a")]
    public void IsValid_KebabCase_IsAccepted(string roleId)
    {
        Assert.True(RoleIdFormat.IsValid(roleId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deep:reasoning")]
    [InlineData("Deep-Reasoning")]
    [InlineData("deep_reasoning")]
    [InlineData("-fast")]
    [InlineData("fast-")]
    [InlineData("fast--slow")]
    [InlineData("fast slow")]
    public void IsValid_NotKebabCase_IsRefused(string? roleId)
    {
        Assert.False(RoleIdFormat.IsValid(roleId));
    }

    [Fact]
    public void IsValid_SixtyFourCharacters_IsAccepted_ButSixtyFiveIsRefused()
    {
        Assert.True(RoleIdFormat.IsValid(new string('a', RoleIdFormat.MaxLength)));
        Assert.False(RoleIdFormat.IsValid(new string('a', RoleIdFormat.MaxLength + 1)));
    }
}
