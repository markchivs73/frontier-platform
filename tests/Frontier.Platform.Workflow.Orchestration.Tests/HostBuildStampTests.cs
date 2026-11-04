using System.Reflection;
using System.Reflection.Emit;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S13.182: the stamp names the assembly the consumer passes — the worker's build — not the platform's.</summary>
public sealed class HostBuildStampTests
{
    [Fact]
    public void FromAssembly_NullAssembly_Throws() =>
        Assert.Throws<ArgumentNullException>(() => HostBuildStamp.FromAssembly(null!));

    [Fact]
    public void FromAssembly_ReadsThePassedAssemblysInformationalVersion()
    {
        var worker = Emit("Worker", new Version(4, 5), "4.5.0+cafe123");

        Assert.Equal("4.5.0+cafe123", HostBuildStamp.FromAssembly(worker).Version);
    }

    [Fact]
    public void FromAssembly_WithoutInformationalVersion_FallsBackToTheAssemblyVersion()
    {
        var worker = Emit("WorkerNoInfo", new Version(4, 5, 6, 7), informationalVersion: null);

        Assert.Equal("4.5.6.7", HostBuildStamp.FromAssembly(worker).Version);
    }

    [Fact]
    public void FromAssembly_TwoAssemblies_StampDifferently()
    {
        // The S13.182 defect: the stamp was read from the platform assembly, so two worker builds on one
        // platform version could not be told apart. The stamp must follow the assembly passed in.
        var platform = HostBuildStamp.FromAssembly(typeof(HostBuildStamp).Assembly).Version;
        var worker = HostBuildStamp.FromAssembly(Emit("WorkerOther", new Version(1, 0), "1.0.0+beef456")).Version;

        Assert.NotEqual(platform, worker);
    }

    [Fact]
    public void FromAssembly_IsResolvedOnce()
    {
        var stamp = HostBuildStamp.FromAssembly(typeof(HostBuildStampTests).Assembly);

        Assert.Same(stamp.Version, stamp.Version);
    }

    [Fact]
    public void Resolve_NonBlankInformationalVersion_Wins() =>
        Assert.Equal("1.2.3+abc1234", HostBuildStamp.Resolve("1.2.3+abc1234", new Version(9, 9)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankInformationalVersion_FallsBackToAssemblyVersion(string? informational) =>
        Assert.Equal("9.9.0.0", HostBuildStamp.Resolve(informational, new Version(9, 9, 0, 0)));

    [Fact]
    public void Resolve_NothingAvailable_ReturnsUnknown() =>
        Assert.Equal("unknown", HostBuildStamp.Resolve(null, null));

    private static AssemblyBuilder Emit(string name, Version version, string? informationalVersion)
    {
        var attributes = informationalVersion is null
            ? []
            : new[] { new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]) };
        return AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name) { Version = version }, AssemblyBuilderAccess.Run, attributes);
    }
}
