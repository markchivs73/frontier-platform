using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests;

public sealed class DynamicContextRefresherTests
{
    [Fact]
    public void Constructor_NullContextStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DynamicContextRefresher(null!, new XunitLogger()));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DynamicContextRefresher(new Phase1EngagementContextStore(), null!));
    }

    [Fact]
    public async Task RefreshDynamicAsync_SameContent_NoRefresh()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new XunitLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-1");
        var content = """{"data":"test"}""";

        // Seed initial content
        await store.UpsertDynamicContextAsync(engagementId, content, CancellationToken.None);

        // Refresh with same content
        var result = await refresher.RefreshDynamicAsync(engagementId, content, "test", CancellationToken.None);

        Assert.False(result.Refreshed);
        Assert.Equal(CanonicalProfile.Hash(content), result.ContentHash);
        Assert.Equal(0, result.Epoch);
    }

    [Fact]
    public async Task RefreshDynamicAsync_ChangedContent_Refreshes()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new XunitLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-2");
        var content1 = """{"v":1}""";
        var content2 = """{"v":2}""";

        // Seed initial content
        await store.UpsertDynamicContextAsync(engagementId, content1, CancellationToken.None);

        // Refresh with different content
        var result = await refresher.RefreshDynamicAsync(engagementId, content2, "test", CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(CanonicalProfile.Hash(content2), result.ContentHash);
        Assert.Equal(1, result.Epoch);

        // Verify content persisted
        var retrieved = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(content2, retrieved);
    }

    [Fact]
    public async Task RefreshDynamicAsync_EmptyStore_FirstWrite()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new XunitLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-3");
        var content = """{"initial":"data"}""";

        // No prior content
        var result = await refresher.RefreshDynamicAsync(engagementId, content, "first-write", CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(CanonicalProfile.Hash(content), result.ContentHash);
        Assert.Equal(0, result.Epoch);
    }

    [Fact]
    public async Task RefreshDynamicAsync_DifferentReason_AllowsMetrics()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new XunitLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-4");
        var content1 = """{"v":1}""";
        var content2 = """{"v":2}""";

        await store.UpsertDynamicContextAsync(engagementId, content1, CancellationToken.None);

        // Different refresh reasons should work without error
        var result1 = await refresher.RefreshDynamicAsync(engagementId, content2, "periodic", CancellationToken.None);
        Assert.True(result1.Refreshed);

        var content3 = """{"v":3}""";
        var result2 = await refresher.RefreshDynamicAsync(engagementId, content3, "signal-driven", CancellationToken.None);
        Assert.True(result2.Refreshed);
    }

    [Fact]
    public async Task RefreshDynamicAsync_SameContent_LoggerDisabled_SkipsLogging()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new DisabledLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-5");
        var content = """{"data":"no-log"}""";

        await store.UpsertDynamicContextAsync(engagementId, content, CancellationToken.None);

        var result = await refresher.RefreshDynamicAsync(engagementId, content, "test", CancellationToken.None);

        Assert.False(result.Refreshed);
    }

    [Fact]
    public async Task RefreshDynamicAsync_ChangedContent_LoggerDisabled_RefreshesWithoutLogging()
    {
        var store = new Phase1EngagementContextStore();
        var logger = new DisabledLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-6");
        var content1 = """{"v":1}""";
        var content2 = """{"v":2}""";

        await store.UpsertDynamicContextAsync(engagementId, content1, CancellationToken.None);

        var result = await refresher.RefreshDynamicAsync(engagementId, content2, "test", CancellationToken.None);

        Assert.True(result.Refreshed);
    }

    /// <summary>
    /// Minimal ILogger for testing (captures but doesn't output).
    /// </summary>
    private sealed class XunitLogger : ILogger<DynamicContextRefresher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    /// <summary>Logger that disables all log levels — exercises the <c>IsEnabled = false</c> branches.</summary>
    private sealed class DisabledLogger : ILogger<DynamicContextRefresher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
