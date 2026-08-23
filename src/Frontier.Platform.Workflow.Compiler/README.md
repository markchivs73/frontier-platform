# Frontier.Platform.Workflow.Compiler

The **workflow compiler**: it decides whether a designed DAG may be published, describes the
design language the agent works from, and runs the publish lifecycle that follows.

Three things live here:

- **Structural validation.** A rule set — graph shape, data-edge contract agreement, HITL
  rollback targets, determinism of predicates, resilience overrides, versioning, retention —
  evaluated in tiers, from pure structural checks to ones that need a catalogue lookup.
- **The design language.** The schema handed to the design agent, generated from the model's
  own types and their XML documentation, so the thing the agent designs against cannot drift
  from the thing the interpreter executes.
- **The publish lifecycle.** Draft → validate → propose → approve → publish, with version
  pinning, proposal merge and diff, test runs and retirement monitoring, over Cosmos.
- **The design agent.** The conversational protocol that turns a designer's plain-language
  request into a proposed definition, validates it, and repairs it when validation fails.

## Install

```bash
dotnet add package Frontier.Platform.Workflow.Compiler
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

```csharp
services.AddFrontierWorkflowCompiler();
```

That registers the compiler's own internals — the validator, the structural rules, schema
generation and the lifecycle. It does **not** register any catalogue, because every catalogue
is a statement about a particular deployment.

## What the consumer supplies

| Port | The question it answers |
|---|---|
| `IAgentRoleCatalog` / `IApproverRoleCatalog` | Which agent and approver roles exist here |
| `IInstructionCatalog` | Which agent instructions exist, and does this ref resolve |
| `IContextComponentCatalog` | Which baseline components and dynamic fields exist |
| `IRetryProfileCatalog` | Which named retry profiles are configured |
| `IDesignerToolCatalog` | Which tools the design agent may offer |
| `IExecutableNodeTypeCatalog` | Which node types this runtime will actually run |
| `IContractTypeCatalog` | Which data contracts this deployment declares |
| `ICascadeGraphChecker` | Cascade-graph checks at publish |
| `ITestRunExecutor` and friends | How a test run is executed and read back |
| `IEntryContractCatalog` | What the entry node is handed, and from which dynamic field |
| `IChatClient` / `IDesignerModelProvider` | Which model the design agent runs on, and how to call it |

**Workload rule packs register alongside the structural set.** A rule is just an
`IDefinitionValidationRule` registration, so a deployment adds its own policy without forking
anything. The set shipped here is deliberately structural only — it encodes what makes a
workflow *executable*, never what makes one *acceptable to a particular business*.

## The design agent names no contract of yours

`IEntryContractCatalog` exists because the agent's system prompt used to state one deployment's
entry contract and dynamic field **as string literals**. That is invisible to any architecture
test working at the type level, and it would have shipped a workload's vocabulary inside this
package.

The engine now states the rule — the entry node is handed context rather than an upstream data
payload — and you supply the nouns.

Whatever answers `IEntryContractCatalog` **must agree with whatever builds the entry payload at
runtime** (`IEntryPayloadBuilder`, in the orchestration package). The agent is told to request a
field the runtime then reads; if they ever name different things, every workflow the agent
designs will validate and then fail live. Derive both from one constant rather than stating it
twice.

## A note on the definition hash

`ComputeDefinitionHash` produces lower-case hex and suppresses CA1308 to do it. That is
deliberate: the hash is stored on every published definition, pinned by running executions and
used as a cache key. Changing its case would silently invalidate every stored hash and every
pin. Do not "fix" the analyzer warning.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
