using System.Net;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>
/// The Cosmos ledger's lost-race classification (doc 07 §6): a stale ETag or a beaten first create retries; throttling,
/// unavailability and a missing document do not.
/// </summary>
public sealed class BudgetLedgerWriteConflictTests
{
    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public void IsConcurrencyConflict_LostRaceStatus_IsAConflict(HttpStatusCode statusCode) =>
        Assert.True(BudgetLedgerWriteConflict.IsConcurrencyConflict(statusCode));

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void IsConcurrencyConflict_OtherStatus_IsNotAConflict(HttpStatusCode statusCode) =>
        Assert.False(BudgetLedgerWriteConflict.IsConcurrencyConflict(statusCode));

    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 50)]
    [InlineData(4, 200)]
    [InlineData(6, 800)]
    public void BackoffCeiling_AfterFailedAttempts_DoublesEachTime(int failedAttempts, int expectedMilliseconds) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), BudgetLedgerWriteConflict.BackoffCeiling(failedAttempts));

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void BackoffCeiling_ManyFailedAttempts_IsCapped(int failedAttempts) =>
        Assert.Equal(TimeSpan.FromMilliseconds(1000), BudgetLedgerWriteConflict.BackoffCeiling(failedAttempts));

    [Fact]
    public void BackoffCeiling_NoFailedAttempts_StillWaitsTheBase() =>
        Assert.Equal(TimeSpan.FromMilliseconds(25), BudgetLedgerWriteConflict.BackoffCeiling(0));
}
