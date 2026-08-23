# Frontier.Platform.Workflow.Orchestration

The **workflow interpreter**: the durable orchestrator that walks a compiled DAG, the activity
shells it schedules, the agent and tool invocation pipelines, and the audit consolidation that
turns a finished run into signed evidence.

This is the engine tier. It composes the governance libraries — HITL, audit, context assembly,
model-role config, guardrails, resilience — through their interfaces, which is what an
interpreter does. **Nothing in the governance tier depends on this package**, so a solution that
wants audit or approvals without an interpreter still gets them on their own (ADR-PA5, enforced
by `GovernanceLibrary_DoesNotDependOnTheEngine`).

## Install

```bash
dotnet add package Frontier.Platform.Workflow.Orchestration
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## It is vendor-neutral, and that is load-bearing

There is no reference here to a model provider or a tool transport — no Anthropic SDK, no
`Microsoft.Agents.AI`, no Model Context Protocol client. The interpreter reaches those through
**consumer-owned ports**, and the consumer supplies the adapters:

| Port | What the consumer supplies |
|---|---|
| `IAgentInvoker` | Invoking an agent with a prompt and typed contracts |
| `IMcpToolCatalog` / `IMcpEndpointResolver` | Resolving and calling tools |
| `IEntryPayloadBuilder` | Mapping assembled context to the workload's entry contract |
| `IInstructionsResolver` | Where agent instructions come from |
| `IMcpWriteClassifier` | Whether a tool mutates state, for sandbox write-fencing |
| `IExecutionSnapshotReader` | Reading the execution projection |
| `IContractTypeSet` (from `…Workflow.Model`) | This deployment's contract types |

The alternative — the engine holding a provider reference — would make adopting the interpreter
mean adopting a vendor. It also has a subtler cost: a hardcoded classification is invisible to
architecture tests, because it lives in string literals rather than signatures.
`IMcpWriteClassifier` exists because exactly that had happened: the write/read split for two
demo connectors was compiled into the orchestrator.

## Determinism

Three of the ports are consumed **from inside the orchestrator body**, where the durable
substrate replays every decision: `IResiliencePolicyProvider`, `IRollbackPlanner` and
`IMcpWriteClassifier`. Implementations of those three must be **pure** — a total function of
their arguments, with no I/O, no clock, and the same answer on every replay. Everything
non-deterministic belongs in an activity, which is where the rest of the surface lives.

Activity names are identity, not labels. They are matched against recorded history on replay, so
renaming one is a breaking change to in-flight executions and no worker-side alias can soften it.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`, so a compatibility break is a reviewable
text diff rather than a surprise at a consumer's build.
