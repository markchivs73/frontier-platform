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
| `IAgentRoleCatalog` / `IApproverRoleCatalog` | Which agent and approver roles exist here. Since v0.45.0 an agent role also says its `Backing` (ADR-PA40): a node on an agent-backed role may omit `instructions_ref`, and `agent.instructions-resolve` reads this catalogue to know which |
| `IInstructionCatalog` | Which agent instructions exist, and does this ref resolve |
| `IContextComponentCatalog` | Which baseline components and dynamic fields exist |
| `IRetryProfileCatalog` | Which named retry profiles are configured |
| `IDesignerToolCatalog` | Which tools the design agent may offer |
| `IExecutableNodeTypeCatalog` | Which node types this runtime will actually run |
| `IContractTypeCatalog` | Which CLR data contracts this deployment declares |
| `IContractSnapshotCatalog` | Which registry contracts exist, whether each is retired (ADR-E27), and which a new publish may name (ADR-PA41). In `Frontier.Platform.Abstractions` since v0.45.0 (ADR-PA40) |
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

## The designer copies what the registry holds for an MCP tool

An `mcp_tool` node's `input_contract_type`, `output_contract_type` and `writes` are the registry's
values for its tool, never the designer's or the agent's (ADR-PA41). `McpToolRegistryStamp.Apply`
overwrites them from the tool's `DesignerToolDescriptor`, nulls included, and leaves a node whose
`tool_ref` does not resolve for `mcp.tool-resolves` to report. The chat designer applies it to
every proposal the agent returns, the first attempt and the repair alike, before the proposal is
validated, diffed or merged.

Publish does not apply it. Publish validates, and a registry change after review shows up as an
`mcp.tool-resolves` finding rather than a silent edit. A plain draft save does not apply it
either. The function is public so you can apply the same rule wherever else you build or rewrite
a definition, such as a migration of existing definitions.

## The design agent reads what each contract says

The design agent's system prompt lists every contract `IContractSnapshotCatalog.ListAsync` returns
(the registered contracts that are not retired) beside the CLR contract names, and for each one
shows its ref, its contract-level description, each property's location and description, its
examples, and each rule's field path and message (ADR-PA41). That is the text your contract panel
shows, so the agent and the human read the same annotations. Contracts are listed in ordinal ref
order, so the block changes only when a contract version is registered or retired. Nothing is
capped: a catalogue too large for the prompt would need a cap or retrieval, which is not built.

A contract an MCP server supplied may lack descriptions or examples, because registration only
warns for it. The prompt marks such a contract with each gap `ContractLegibility.Gaps` reports,
which is the registration lint itself, so the prompt and registration never disagree about what
a gap is. The prompt also says what the tool and role lists' contract refs mean: a tool with no
`output_contract_ref` returns any JSON and is named as such, an `agent_task` bound to a
contract-bearing role names that role's refs (`model-role.contracts-match`), and an `mcp_tool`
node's refs and `writes` are copied by the designer, so the agent need not author them.

`ListAsync` is new in v0.45.0: your adapter over the registry must implement it.

## Publish pins the contracts

Publish and test runs both call `IDefinitionContractEmbedder`, which embeds every registry
contract the definition names into `WorkflowDefinition.Contracts` (ADR-E27 D15). An engine-owned
contract, such as `frontier/gate-decision/1.0`, comes from the engine; everything else comes from
`IContractSnapshotCatalog`. A ref that is not registered, is retired, or comes back under a
different ref stops the publish before the proposal is approved. A definition that names only CLR
contracts embeds nothing.

## A note on the definition hash

The hash is SHA-256 over the RFC 8785 (JCS) form of the definition's canonical-profile bytes,
with its own `definition_hash` field blank. The profile fixes the typed shell's order, and JCS
fixes the order of what the profile cannot: the embedded contract schemas. Nothing recomputes a
stored hash, so versions published before the switch keep the hash they were recorded with.

`ComputeDefinitionHash` produces lower-case hex. The hash is stored on every published
definition, pinned by running executions and used as a cache key. Changing its case would
silently invalidate every stored hash and every pin.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
