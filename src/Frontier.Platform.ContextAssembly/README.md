# Frontier.Platform.ContextAssembly

Tiered context assembly. An agent is handed a `ContextPackage` and never retrieves its own
context — that rule is the reason this library exists, and everything in it follows from it.

Context is assembled in three tiers, ordered least-volatile to most-volatile:

| Tier | Scope | Changes |
|---|---|---|
| **Baseline** | Fleet-wide, shared | Slowly, on a governed catalogue release |
| **Dynamic** | Per engagement | On signal, epoch-versioned |
| **Real-time** | Per invocation | Every call, and only when the request asks for it |

That order is not a stylistic choice: it is simultaneously the correct prompt-cache layout, so the
two tier boundaries are exactly where a provider's cache breakpoints go. A stable prefix that
never moves is what makes a cache hit possible at all.

## Install

```bash
dotnet add package Frontier.Platform.ContextAssembly
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierContextAssembly();                       // no database required
services.AddFrontierCosmosEngagementContext(configuration);   // durable dynamic context
```

Only the composition root should call these.

The first call registers the assembler, the dynamic-context refresher, the validator, the
debugger, the caching-strategy registry (Anthropic for `claude-*`, OpenAI as a provider-wide
default, no-op as fallback), and the compiled-in Phase 1 stores. It deliberately needs **no**
Cosmos configuration, so context assembly can be composed in a test or a tool without a database.

**A solution that runs real engagements must also make the second call.** It binds the `Cosmos`
section and *replaces* — not appends — the compiled-in engagement-context store with the durable
one, so the resolved implementation never depends on registration order. Without it, every
engagement outside the compiled-in catalogue resolves to no dynamic context at all, silently
(ADR-PA14). The `CosmosClient` is registered with `TryAdd`, so a consumer already sharing one
keeps it.

`ContextAssemblyOptions` configures the baseline catalogue id, the per-tier token ceilings, the
dynamic refresh interval, and whether caching is on.

## What it contains

**The package** — `ContextPackage`, `BaselineTier`, `DynamicTier`, `RealTimeTier` and `CacheHint`
live in `Frontier.Platform.Serialization`, because the shape crosses every subsystem boundary and
is hashed. This library produces them.

**Assembly**

| Interface | What it does |
|---|---|
| `IContextAssembler` | Composes the three tiers plus caching metadata into a `ContextPackage` |
| `IDynamicContextRefresher` | Writes a new dynamic-context epoch for an engagement and reports what changed |
| `IContextValidator` | Checks a `ContextRequest` against the baseline catalogue — an unknown component is caught here, not at the provider |
| `IContextDebugger` | Dumps an assembled package and its provider layout, and structurally diffs two packages |

**Provider caching** — `ICachingStrategy` and `ICachingStrategyRegistry` turn an assembled package
into a `ProviderMessageLayout` with that provider's cache directives applied, and read cache
metrics back out of the provider's response. `AnthropicCachingStrategy`, `OpenAiCachingStrategy`
and `NoCachingStrategy` ship here; the registry resolves by `(provider, model pattern, version
pattern)` with a fallback, so an unrecognised model degrades to no caching rather than failing.

**Stores**

| Interface | Implementations |
|---|---|
| `IBaselineCatalogueStore` | `Phase1BaselineCatalogueStore` (compiled-in) |
| `IEngagementContextStore` | `Phase1EngagementContextStore` (compiled-in) or `CosmosEngagementContextStore` (durable) |

**Contracts** — `ContextTier`, `ContextPackageMetadata`, `ProviderCacheDirective`,
`CacheHitMetrics`, `CachingMetadata`, `ContextComparisonResult`, `DynamicContextRefreshResult`,
and the durable pair `EngagementContextEpoch` / `EngagementContextPointer`.

## Dynamic context is epoch-versioned

The Cosmos store writes an immutable `EngagementContextEpoch` per refresh — content plus its hash
— and repoints an `EngagementContextPointer` at the current epoch. A refresh is therefore an
append and a pointer move, not an overwrite: what an execution actually saw stays recoverable, and
the content hash is what tells you whether a cache prefix is still valid.

## Key invariants

- **Agents never self-retrieve.** Context arrives assembled. A tool that fetches context inside an
  agent turn defeats the cache layout and makes what the agent saw unreproducible.
- **Cache breakpoints sit at tier boundaries and nowhere else.** Moving one invalidates every
  cached prefix behind it.
- **Tier order is fixed** — baseline, dynamic, real-time. Reordering is a prompt change for every
  workflow at once.
- **Real-time is opt-in**, present only when the request set `RequiresRealTime`.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
