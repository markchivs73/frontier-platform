---
name: logging
description: Structured logging conventions — [LoggerMessage] source-generated delegates, CA1848, IsEnabled guards, log-level discipline. Use when adding or reviewing any ILogger usage.
---

# Logging

## The rule: always use `[LoggerMessage]` source-generated delegates

Never call `logger.LogInformation(...)`, `logger.LogWarning(...)`, etc. directly with format-string arguments. These extension methods allocate strings even when the log level is disabled (CA1848). The zero-overhead alternative is the source generator:

1. Make the class `partial`.
2. Add `static partial void` methods decorated with `[LoggerMessage]`.
3. Call those methods instead of the extension methods.
4. Never add `#pragma warning disable CA1848` — if you see one, fix the code.

```csharp
// Wrong — allocates even when Information is disabled
logger.LogInformation("User {UserId} signed in at {Time}", userId, DateTime.UtcNow);

// Wrong — IsEnabled guard reduces allocations but CA1848 still fires
if (logger.IsEnabled(LogLevel.Information))
    logger.LogInformation("User {UserId} signed in at {Time}", userId, DateTime.UtcNow);

// Correct — source generator emits the IsEnabled guard and avoids all allocation
[LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in at {Time}")]
private static partial void LogUserSignedIn(ILogger logger, string userId, DateTimeOffset time);
```

## Anatomy of a `[LoggerMessage]` method

```csharp
internal sealed partial class MyService : IMyService
{
    private readonly ILogger logger;

    public MyService(ILogger<MyService> logger) => this.logger = logger;

    public void DoWork(string id)
    {
        LogWorkStarted(logger, id);
        // ...
        LogWorkCompleted(logger, id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Work started for {Id}")]
    private static partial void LogWorkStarted(ILogger logger, string id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Work completed for {Id}")]
    private static partial void LogWorkCompleted(ILogger logger, string id);
}
```

Rules:
- The method **must** be `static partial void` (never `async`, never returns a value).
- The first parameter **must** be `ILogger` (not `ILogger<T>`).
- Parameter names in the `Message` template **must** match method parameter names exactly (case-insensitive match, but use matching case).
- `EventId` is optional; omit it unless you need a stable numeric ID for alerting.
- Group log methods at the bottom of the class, after all non-logging members.

## Type safety in log parameters

Any type works as a parameter — the generator calls `ToString()` by default. For custom types, ensure `ToString()` returns a meaningful value (not the default record `{ Value = ... }` form). Example: `EngagementId` overrides `ToString()` to return its raw string value, making it safe to pass directly.

Do **not** pass SDK objects (Cosmos documents, HTTP responses) — extract only the fields you need.

## Log-level discipline

| Level | When |
|---|---|
| `Trace` | High-frequency internal state useful only during local debugging; never in production builds by default. |
| `Debug` | Per-request diagnostics; off in production but flippable without redeploy. |
| `Information` | Coarse lifecycle events: service started, record written, epoch advanced, refresh triggered. |
| `Warning` | Recoverable unexpected states: hash collision, stale cache hit, retry scheduled. |
| `Error` | Failures the caller cannot recover from; always include the `Exception` parameter. |
| `Critical` | Data-loss or service-down conditions requiring immediate operator action. |

Prefer `Information` for normal-path events and `Warning` for expected-but-unusual outcomes. Do not emit `Information` for every iteration of a loop — one event per meaningful state transition.

## Exception logging

Always pass the exception as the last `Exception? exception` parameter — never embed it in the message string:

```csharp
// Wrong — stack trace is lost or double-serialised
logger.LogError("Operation failed: {Message}", ex.Message);

// Correct
[LoggerMessage(Level = LogLevel.Error, Message = "Operation failed for {Id}")]
private static partial void LogOperationFailed(ILogger logger, string id, Exception exception);
```

## What not to log

- Secrets, PII, or tokens — even at Trace. The emitted message is unredacted in structured sinks.
- Raw JSON blobs or full context packages — log a hash or byte-count instead.
- Diagnostic strings constructed with string interpolation outside `[LoggerMessage]` — that defeats the purpose.

## IsEnabled guards

Do **not** add manual `if (logger.IsEnabled(...))` guards around `[LoggerMessage]` calls. The source generator emits that check inside the generated method body. Adding a guard outside it is dead code.

The only valid reason for a manual `IsEnabled` guard is when you have expensive parameter *construction* that should be skipped entirely:

```csharp
// Acceptable: building the summary string is expensive; guard the build, not the log call
if (logger.IsEnabled(LogLevel.Debug))
    LogSummary(logger, BuildExpensiveSummary(data));
```

Even then, prefer making `BuildExpensiveSummary` cheap or lazy before reaching for a guard.
