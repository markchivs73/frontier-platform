# Frontier Platform

Reusable platform libraries for the Frontier workflow orchestration platform, published as
NuGet packages to GitHub Packages.

These nine libraries are a **severable sub-graph**: no platform assembly references any
`Frontier.Reason.*` assembly, at either the assembly-reference or the type-dependency level.
That guarantee is what allowed them to be extracted from the `frontier-workflow` repository
into this one, and it is enforced here by architecture tests rather than left to convention
(see [`docs/DECISIONS.md`](docs/DECISIONS.md), ADR-PA2).

## Packages

| Package | Owns |
|---|---|
| `Frontier.Platform.Abstractions` | The platform contract kernel: `SmartEnum<T>`, `IVersionedContract`, `ContractMigrator`, the permanent-failure exceptions, and the shared kernel contracts. Zero dependencies (ADR-PA1). |
| `Frontier.Platform.Serialization` | The canonical JSON profile. Byte-stable output — definition hashing, cache hits and audit signing all depend on it. |
| `Frontier.Platform.ContextAssembly` | Tiered context assembly (baseline → dynamic → real-time) with provider cache breakpoints at tier boundaries. |
| `Frontier.Platform.Audit` | Audit record contracts, hash chaining and verification, Cosmos-backed stores, and change-feed archival export. |
| `Frontier.Platform.Hitl` | Human-in-the-loop approval store, queries and rollback. Solution-agnostic: callers drive it from their own orchestration activity shells. |
| `Frontier.Platform.ModelRoleConfig` | Model role registry and resolution: capability requirements, fleet/canary mappings, mapping governance. |
| `Frontier.Platform.Resilience` | Retry, circuit-breaker and bulkhead policy specifications with failure classification, translated to named Polly v8 profiles. |
| `Frontier.Platform.Observability` | Telemetry contracts and metric catalogue. |
| `Frontier.Platform.Guardrails` | Admission control and budget enforcement over a Cosmos-backed ledger. |

## Consuming the packages

GitHub Packages **requires authentication even for public packages**, so a personal access
token with the `read:packages` scope is needed regardless of repository visibility.

Add a `nuget.config` beside your solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="frontier" value="https://nuget.pkg.github.com/markchivs73/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <frontier>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </frontier>
  </packageSourceCredentials>
</configuration>
```

Then set `GITHUB_USERNAME` and `GITHUB_TOKEN` in your environment and reference the packages
normally. Do not commit the token.

## Versioning

Versions are derived from git tags by [MinVer](https://github.com/adamralph/minver): tagging
`v1.2.3` on `main` publishes `1.2.3`. Untagged builds get a height-suffixed prerelease off the
last tag.

All nine packages version in **lockstep** from a single tag — a change to one library
republishes all nine at the new version. That is a deliberate trade: one tag and one changelog
instead of nine of each. See `docs/DECISIONS.md`.

While the major version is `0`, breaking changes may appear in a minor release.

## Build and test

```bash
dotnet build FrontierPlatform.slnx -c Release   # warnings are errors
./tools/run-unit-tests.sh                       # unit + architecture tests, coverage gate
```

`run-unit-tests.sh` mirrors the CI `test` job exactly, including the ≥95% per-assembly
line-and-branch coverage gate. Run it before pushing.

The integration tests need a Cosmos emulator. They provision and tear down their own
databases, so no setup script is required:

```bash
docker run -d --name cosmos-emulator -p 8081:8081 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview --protocol http

dotnet test FrontierPlatform.slnx -c Release --filter "Category=Integration"
```

Set `COSMOS_EMULATOR_ENDPOINT` if the emulator is not on `http://localhost:8081`.

## Contributing

Conventions live in [`.claude/skills/`](.claude/skills/) and are summarised in
[`CLAUDE.md`](CLAUDE.md). The short version:

- Every public member must appear in that library's `PublicAPI.Shipped.txt` or
  `PublicAPI.Unshipped.txt`; the build fails otherwise.
- Commit subjects are `scope: Imperative summary`, with `scope!:` marking a break to a
  package's public surface. CI validates this.
- Architecture tests encode the severability guarantee. Never weaken one to make a build pass.
