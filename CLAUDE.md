# Claude Code instructions

Reusable platform libraries published as NuGet packages. .NET 10.

## The thing that makes this repo different

**Everything merged here becomes a versioned artifact somebody else resolves.** A public member
is a permanent compatibility obligation; a `PackageVersion` bump is a minimum version every
consumer inherits; a serialization change rewrites the meaning of stored bytes in every solution
downstream. Weigh changes accordingly — the blast radius is not this repo.

## Hard invariants (violations are bugs, no exceptions)

1. **No platform library references any `Frontier.Reason.*` assembly** (ADR-PA2). Two
   architecture tests enforce it, at the assembly and type level. This is what makes the repo
   severable; never weaken a rule to make a build pass.
2. **`Platform.Abstractions` has zero dependencies** (ADR-PA1) — no Frontier references, no
   third-party packages. Everything inherits from it.
3. **Canonical serialization is byte-stable.** One shared `JsonSerializerOptions`: omit-null,
   `[JsonPropertyOrder]` on every contract property, snake_case, ISO-8601-UTC-ms, string
   decimals, string enums, invariant culture. Hashing, cache hits and audit signing all depend
   on identical bytes.
4. **Every public member is tracked** in `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`. The
   build fails otherwise.
5. **Libraries reach outward through consumer-owned ports**, never through a reference. If you
   want a reference to a consuming assembly, you want a port (`IReferencedRolesSource` is the
   example).
6. **Stored bytes are evidential** — never batch-rewrite stored documents. Migration happens
   lazily at read time via `ContractMigrator`, keyed on the stored `schema_version` string.

## Code conventions

.NET 10, nullable enabled, warnings as errors, analyzers at `latest-all`. Clean Architecture:
the dependency rule points inward. Constructor injection only; each library exposes
`AddFrontierXxx()` and only the consumer's composition root calls it. No SDK types on public
surfaces. `sealed` by default, records for immutable data, `CancellationToken` on every public
async method. **No `private` methods** — extract helpers into testable `internal` classes.
Methods ≤10–15 lines. Doc comments on all public and internal surface. Match surrounding style;
comment only what the code cannot express.

Commits: `scope: Imperative summary`, `scope!:` for a public-surface break. CI validates it.

## Verifying

```bash
dotnet build FrontierPlatform.slnx -c Release   # warnings are errors
./tools/run-unit-tests.sh                       # mirrors CI exactly, incl. the coverage gate
```

Run `run-unit-tests.sh` before pushing. Integration tests need a Cosmos emulator (see README);
they provision their own databases.

**Beware iCloud duplicates.** `~/Documents` is iCloud-synced and spawns `"Foo 2.cs"` files and
`obj 2/` directories. `Directory.Build.targets` excludes them from the build, but wipe
`bin/`/`obj/` before a coverage run if numbers look wrong:
`find . -type d -name "* [0-9]*" -empty -delete`.

## Skills (load on demand — don't read these unless your task touches the area)

| Skill | Use when |
|---|---|
| `library-boundaries` | Adding references, creating projects, wiring DI, writing architecture tests, declaring a port |
| `canonical-serialization` | Adding/changing any contract, converter, hashing, or migration |
| `cosmos-conventions` | Touching containers, repositories, queries, TTL/archival |
| `engineering-standards` | Designing class/interface surfaces, creating types, smart enums, structural decisions |
| `testing-strategy` | Writing tests, deciding test scope, coverage questions, mock boundaries |
| `code-review` | Reviewing a PR or preparing one for review |
| `git-workflow` | Committing, branching, merging, releasing |
| `logging` | Adding or reviewing any `ILogger` usage |
| `owasp-code-security` | Assessing code for security issues or fixing a logged finding |

Settled decisions live in `docs/DECISIONS.md`. If something structural is unspecified, ask
rather than invent.
