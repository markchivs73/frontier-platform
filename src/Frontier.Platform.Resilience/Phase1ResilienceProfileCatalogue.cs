namespace Frontier.Platform.Resilience;

/// <summary>
/// The compiled-in Phase 1 resilience profiles (doc 10 §4, §9 "fall back to
/// compiled-in defaults"). S4.4 scope is the two profiles
/// <see cref="GraphOrchestratorSteps"/>-equivalent wiring needs:
/// <see cref="LlmDefault"/> (the agent-invocation activity) and
/// <see cref="SnapshotPersistence"/> (the snapshot-write activity, ADR-S3). The
/// remaining Phase 1 names (<c>llm-interactive</c>, <c>mcp-read</c>, <c>mcp-write</c>,
/// <c>storage</c>, <c>none</c> — doc 10 §4) are added when their consumers arrive.
/// <c>tools/dev-setup/cosmos-init.py</c> seeds the <c>resilience-profiles</c> container
/// from these values field-for-value, mirroring the <c>model-role-config</c> pattern
/// (S4.3); <see cref="ResiliencePolicyProvider"/> reads this catalogue directly rather
/// than the store, so a cold start with the store down still has a working pipeline.
/// </summary>
public static class Phase1ResilienceProfileCatalogue
{
    /// <summary>
    /// The default profile for agent (LLM provider) invocation activities (doc 10 §4
    /// worked example): 5 inner attempts with decorrelated-jitter backoff up to 30s,
    /// a 90s per-attempt timeout, a (provider, modelId)-scoped breaker, a
    /// provider-scoped bulkhead of 24 concurrent / 48 queued, and 2 outer DTF re-runs.
    /// </summary>
    public static readonly ResilienceProfile LlmDefault = new()
    {
        ProfileId = "llm-default",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 5,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 1_000,
            MaxDelayMs = 30_000,
        },
        TimeoutMs = 90_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 24,
            MaxQueue = 48,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 2,
        },
    };

    /// <summary>
    /// The profile for Cosmos snapshot-write activities (ADR-S3: exponential backoff
    /// with decorrelated jitter, 10 attempts, 5-minute max delay). The outer DTF loop
    /// makes a single attempt — the inner Polly pipeline already exhausts the full
    /// retry budget per doc 02 §5 ("Failures retry via the <c>snapshot-persistence</c>
    /// Resilience profile... Exhaustion → step failure → <c>paused_on_failure</c>").
    /// Circuit breaker and bulkhead use the same shape as <see cref="LlmDefault"/> with
    /// generous thresholds (PoC-grade — Cosmos availability is not the failure mode
    /// ADR-S3 targets); per-attempt timeout is 5s for a single document write.
    /// </summary>
    public static readonly ResilienceProfile SnapshotPersistence = new()
    {
        ProfileId = "snapshot-persistence",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 10,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 1_000,
            MaxDelayMs = 300_000,
        },
        TimeoutMs = 5_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 48,
            MaxQueue = 96,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 1,
        },
    };

    /// <summary>
    /// Chat-designer profile (doc 10 §4: "2 attempts, 20s timeout — a human is waiting"):
    /// minimal retry to keep latency predictable; no outer DTF re-run.
    /// </summary>
    public static readonly ResilienceProfile LlmInteractive = new()
    {
        ProfileId = "llm-interactive",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 2,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 1_000,
            MaxDelayMs = 10_000,
        },
        TimeoutMs = 20_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 24,
            MaxQueue = 48,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 1,
        },
    };

    /// <summary>
    /// MCP read profile (doc 10 §4: "3 attempts, 10s"): short timeout for read operations
    /// where MCP connectors need quick response; 2 outer DTF re-runs.
    /// </summary>
    public static readonly ResilienceProfile McpRead = new()
    {
        ProfileId = "mcp-read",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 3,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 500,
            MaxDelayMs = 5_000,
        },
        TimeoutMs = 10_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 24,
            MaxQueue = 48,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 2,
        },
    };

    /// <summary>
    /// MCP write profile (doc 10 §4: "3 attempts, idempotency key required"): structural
    /// idempotency enforcement (pipeline refuses a write call lacking a key) is Stage 7-8
    /// once MCP connectors land; this profile carries the retry parameters.
    /// </summary>
    public static readonly ResilienceProfile McpWrite = new()
    {
        ProfileId = "mcp-write",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 3,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 500,
            MaxDelayMs = 5_000,
        },
        TimeoutMs = 10_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 12,
            MaxQueue = 24,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 2,
        },
    };

    /// <summary>
    /// Storage profile (doc 10 §4: "defer to SDK retry, thin wrapper"): inner retry set to
    /// 1 so Polly does not double-retry on top of the Cosmos SDK's own retry budget;
    /// bulkhead is permissive because SDK-managed I/O queues natively.
    /// </summary>
    public static readonly ResilienceProfile Storage = new()
    {
        ProfileId = "storage",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 1,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 100,
            MaxDelayMs = 100,
        },
        TimeoutMs = 10_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.8,
            MinThroughput = 20,
            SamplingWindowSeconds = 60,
            BreakDurationSeconds = 30,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 100,
            MaxQueue = 200,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 1,
        },
    };

    /// <summary>
    /// No-retry profile (doc 10 §4: "pure activities: cascade evaluation gets none"):
    /// single attempt, effectively-unlimited timeout, very loose breaker and bulkhead —
    /// for in-process deterministic work that cannot be retried or timed out externally.
    /// </summary>
    public static readonly ResilienceProfile None = new()
    {
        ProfileId = "none",
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = 1,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 100,
            MaxDelayMs = 100,
        },
        TimeoutMs = 300_000,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 1.0,
            MinThroughput = 1_000_000,
            SamplingWindowSeconds = 3_600,
            BreakDurationSeconds = 1,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "none",
            MaxConcurrent = 1_000,
            MaxQueue = 10_000,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 1,
        },
    };

    /// <summary>The catalogue keyed by <see cref="ResilienceProfile.ProfileId"/>, consumed by <see cref="ResiliencePolicyProvider"/>.</summary>
    public static readonly IReadOnlyDictionary<string, ResilienceProfile> ByProfileId =
        new Dictionary<string, ResilienceProfile>(StringComparer.Ordinal)
        {
            [LlmDefault.ProfileId] = LlmDefault,
            [SnapshotPersistence.ProfileId] = SnapshotPersistence,
            [LlmInteractive.ProfileId] = LlmInteractive,
            [McpRead.ProfileId] = McpRead,
            [McpWrite.ProfileId] = McpWrite,
            [Storage.ProfileId] = Storage,
            [None.ProfileId] = None,
        };
}
