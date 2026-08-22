# Frontier.Platform.Workflow.Model

The workflow **definition and execution model**: the typed DAG a conversationally-designed
workflow compiles to, and the vocabulary its execution is recorded in.

This package is deliberately inert. It declares shape — nodes, edges, modes, predicates, the
snapshot — and executes nothing. The interpreter that walks it, the compiler that validates it
and the stores that persist it all live elsewhere and depend on this.

**It carries no domain contracts of its own.** A workflow's actual payload types belong to the
workload that runs it; this package never names one. That is the property that lets a second
workload adopt the engine without inheriting the first one's vocabulary, and it is enforced in
the consuming solution by an architecture test rather than left to good intentions.

## Install

```bash
dotnet add package Frontier.Platform.Workflow.Model
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

There is no `AddFrontier…()` call: this package is types only, with nothing to register.

### If you read the XML documentation at runtime, opt in

```xml
<CopyDocumentationFilesFromPackages>true</CopyDocumentationFilesFromPackages>
```

NuGet does not copy a package's XML documentation into the output directory — it leaves it in
the package folder for the IDE. That is fine for most packages and **not** fine for this one:
the design-language schema generator reads these summaries at runtime and turns them into the
node and field descriptions handed to the design agent.

Getting it wrong fails silently. The assembly still loads, nothing throws, and every
description is simply empty. If you consume the model for that purpose, set the property and
keep a test that asserts the doc file resolves — the consumer smoke test in this repo does
exactly that.

## What it contains

**The definition**

- `WorkflowDefinition` — the published, immutable DAG. An edit is a new version; running
  executions stay pinned to the one they started on.
- `WorkflowNode` and its eight subtypes (`AgentTaskNode`, `HumanGateNode`, `DecisionNode`,
  `McpToolNode`, `ParallelNode`, `LoopNode`, `CascadeCheckNode`, `ContextInjectionNode`),
  discriminated on `NodeType`.
- `WorkflowEdge` and `EdgeKind` — control edges drive scheduling, data edges carry typed
  payloads between nodes.
- `ConditionalPredicate` with `ComparisonOp`/`LogicalOp` — the branch conditions a decision
  node evaluates.
- `RetryPolicySpec` — retry behaviour as **data**, not code, so a policy change is a
  definition change rather than a deployment.
- `ExecutionMode` — how an execution is driven.

**The execution record**

- `ExecutionSnapshot` — the read-optimised projection of an execution's state, with
  `ArtifactStatus` per artifact. The durable history remains the source of truth; a snapshot
  that disagrees with it is rebuilt, never trusted.
- `TypedPayload` and `PayloadRef` — a payload travels inline or by reference, so large
  outputs never inflate orchestration state.
- `StepCompletion`, `ExternalEvent`, `WorkItem` — the units a run advances through.
- `WorkflowActivityNames` — the activity names the durable substrate matches replay against.
  These are **identity, not labels**: renaming one is a breaking change to in-flight
  executions, because the orchestrator schedules by name from code and the substrate matches
  that against recorded history. No worker-side alias can soften it.

**Registration and migration**

- `IContractTypeSet` / `ContractTypeSet` — the workload's contract types, supplied by the
  consumer's composition root. The engine consumes the set; it never discovers it, because a
  package cannot know which assembly holds its consumer's contracts.
- `ArtifactVocabularyMigration` — the 1.0 → 2.0 adapters for the artifact vocabulary rename.
  Migration keys on the stored `schema_version` string, never on a CLR type name, which is why
  this model could move assemblies without touching a single stored byte.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`.
Every public member here is tracked in `PublicAPI.Shipped.txt`, so a compatibility break is a
reviewable text diff rather than a surprise at a consumer's build.
