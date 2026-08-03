---
name: owasp-code-security
description: OWASP Top 10 (2021) expressed as concrete code-level checks, with .NET-specific detection patterns and remediation guidance. Use when assessing code for security issues, reviewing a security finding, or fixing a logged security issue.
---

# OWASP code security checks

The OWASP Top 10 (2021) translated into things you can actually find in source code. Each category lists: what to look for (detection patterns), why it matters, and the standard remediation. Severity guidance is at the end.

## A01 — Broken Access Control

**Look for:**
- API endpoints / controllers / minimal-API routes without `[Authorize]` (or an equivalent policy) — especially mutation endpoints.
- Authorization checks done client-side only (UI hides a button, server accepts the call anyway).
- IDOR: handlers that load a resource by an id from the request without verifying the caller is entitled to it (e.g. any `engagementId` taken from the route/body and used directly in a query with no tenancy check).
- Missing method-level checks behind a "the caller already checked" assumption across library boundaries.
- Path traversal: user-supplied values concatenated into file paths (`Path.Combine` with unvalidated segments still traverses on `..`).
- CORS configured with `AllowAnyOrigin` + credentials, or wildcard origins in production config.

**Remediate:** deny by default; enforce authorization at the handler/service boundary, not the UI; scope every data query by the authenticated principal's tenancy; canonicalize and validate file paths against an allowlisted root.

## A02 — Cryptographic Failures

**Look for:**
- Secrets, connection strings, API keys, or certificates committed in source, `appsettings*.json`, launch profiles, test fixtures, or docker-compose files.
- Weak/broken primitives: `MD5`, `SHA1` (for security purposes), `DES`, `TripleDES`, `RC2`, ECB mode, `Random` used for anything security-sensitive (tokens, ids, salts) instead of `RandomNumberGenerator`.
- Home-rolled crypto, encoding used as "encryption" (Base64), hard-coded IVs/salts/keys.
- Passwords hashed with a fast hash instead of PBKDF2/bcrypt/Argon2.
- HTTP (not HTTPS) endpoints, `ServerCertificateCustomValidationCallback` returning `true`, `TrustServerCertificate=True` in connection strings.
- Sensitive data (PII, tokens) written to logs, exceptions, or telemetry.

**Remediate:** secrets to Key Vault / user-secrets / environment injection; modern primitives (AES-GCM, SHA-256+, `RandomNumberGenerator`); never disable certificate validation outside a clearly-marked local-dev path.

## A03 — Injection

**Look for:**
- SQL/Cosmos: string interpolation or concatenation building queries (`$"SELECT * FROM c WHERE c.id = '{id}'"`, `QueryDefinition` without `WithParameter`). EF `FromSqlRaw`/`ExecuteSqlRaw` with interpolated strings.
- Command injection: `Process.Start` / `ProcessStartInfo` with user-influenced arguments, especially `UseShellExecute = true` or arguments built by string concat.
- LDAP, XPath, regex (user input as a pattern → ReDoS), and NoSQL operator injection.
- **Prompt injection (LLM systems):** untrusted content (user input, retrieved documents, tool outputs) concatenated into system prompts or agent instructions without delimiting/typing; agent outputs trusted as structured data without validation.
- XSS: `MarkupString`, `@Html.Raw`, `innerHTML`-equivalent rendering of user data in Blazor/Razor.
- Deserialization of untrusted input with polymorphic/type-name handling (`TypeNameHandling.All`, `BinaryFormatter` — always a finding).

**Remediate:** parameterized queries everywhere; argument arrays not shell strings; encode on output; typed, validated contracts at every trust boundary (for LLM output: schema-validate, never execute); `BinaryFormatter` is banned — replace outright.

## A04 — Insecure Design

**Look for:**
- Missing rate limiting / throttling on expensive or authentication-adjacent endpoints.
- Unbounded resource consumption: request bodies without size limits, unpaginated queries returning entire containers, user-controlled loop counts, fan-out without a cap (e.g. dispatcher spawning unbounded children).
- Trust-boundary confusion: internal services assuming callers are trusted with no authentication between services.
- Missing idempotency on money/state-changing operations that can be retried.
- Security decisions based on client-supplied data (roles, prices, ids in hidden fields or JWT claims that aren't validated).

**Remediate:** design-level fixes — caps, quotas, pagination, server-side authority for every security decision. Log these findings even when the fix is architectural; flag them `severity: high` if exploitable.

## A05 — Security Misconfiguration

**Look for:**
- Detailed errors to clients: `UseDeveloperExceptionPage` outside an environment check, stack traces or exception messages returned in API responses.
- Debug/diagnostic endpoints, Swagger UI, or verbose logging enabled unconditionally.
- Default or permissive settings: `Access-Control-Allow-Origin: *`, missing security headers (HSTS, X-Content-Type-Options, CSP) on user-facing hosts.
- XML parsing with DTD/external entities enabled (`XmlReaderSettings.DtdProcessing = Parse`, `XmlResolver` set) → XXE.
- Overly broad permissions in IaC/config (Cosmos keys instead of RBAC/managed identity, storage account shared keys where AAD would do).
- `#pragma warning disable` or suppressed analyzer warnings around security-relevant code.

**Remediate:** environment-gate all diagnostics; explicit hardened configuration checked into source; managed identity over keys.

## A06 — Vulnerable and Outdated Components

**Look for:**
- Package references with known CVEs: run `dotnet list package --vulnerable --include-transitive` (needs network; record "not checked" in state if unavailable).
- Pinned prerelease/ancient packages, `BinaryFormatter`-era libraries, unmaintained dependencies doing security-critical work.
- Frameworks past end-of-support.

**Remediate:** upgrade path per package; note the CVE ids in the finding so the fixer can verify the patched version.

## A07 — Identification and Authentication Failures

**Look for:**
- JWT validation gaps: `ValidateIssuer/ValidateAudience/ValidateLifetime = false`, `RequireSignedTokens = false`, accepting `alg: none`, symmetric keys in config.
- Session tokens in URLs, tokens without expiry, refresh tokens stored client-side unprotected.
- Credential handling: passwords compared with `==` (timing), no lockout/throttle on login paths, security questions.
- Missing anti-forgery on state-changing form posts (Blazor Server largely handles this; API + cookie auth does not).

**Remediate:** full token validation on; short-lived tokens; constant-time comparison (`CryptographicOperations.FixedTimeEquals`); throttle authentication endpoints.

## A08 — Software and Data Integrity Failures

**Look for:**
- Unsigned/unverified artifacts: definitions, plugins, or config loaded from writable storage and executed/trusted without hash or signature verification.
- CI/CD: scripts piping remote content to a shell (`curl | bash`), unpinned GitHub Actions (`@main` instead of a SHA), secrets echoed in build logs.
- Cache/queue poisoning: consumers trusting message contents without schema validation or origin checks.
- Audit trails that can be silently mutated (audit records in the same store with the same write credentials as the app's hot path, no append-only/immutability guarantee).

**Remediate:** verify integrity (hash/signature) before trusting stored executables/definitions; pin action SHAs; validate every message at the consumer.

## A09 — Security Logging and Monitoring Failures

**Look for:**
- Authentication decisions, authorization denials, and validation failures that are swallowed silently (`catch { }` or `catch (Exception) { return null; }` around security checks).
- No audit record for sensitive operations (publish, delete, permission change).
- Logs that include secrets/tokens/PII (also an A02 finding) or that log user input unencoded (log injection/forging via embedded newlines).
- Missing correlation ids across service boundaries making incidents untraceable.

**Remediate:** log security events at Warning+ with structured fields; sanitize user input in log messages; never swallow security exceptions.

## A10 — Server-Side Request Forgery (SSRF)

**Look for:**
- `HttpClient`/`WebRequest` calls whose target URL is influenced by user input (webhooks, "fetch this URL" features, redirect followers).
- Missing allowlist validation; blocklist-only validation (bypassable via redirects, DNS rebinding, decimal IPs, `[::1]`).
- Requests that can reach cloud metadata endpoints (`169.254.169.254`) or internal services from user-supplied URLs.

**Remediate:** allowlist target hosts; resolve-and-validate; disable redirects on user-influenced requests; egress restrictions.

## Severity guidance

| Severity | Meaning |
|---|---|
| `critical` | Remotely exploitable now with real impact (RCE, auth bypass, secret exposure in a deployed artifact) |
| `high` | Exploitable with modest preconditions, or a critical primitive one config change away |
| `medium` | Real weakness needing specific circumstances; defense-in-depth gap on a sensitive path |
| `low` | Hardening/best-practice deviation with no plausible near-term exploit |
| `info` | Observation worth recording; no action strictly required |

Severity reflects **exploitability in this system's actual deployment context**, not the worst theoretical case — say which in the finding. When code is PoC-stage by declared policy (see project instructions), still log the finding but note the PoC context; the fixer decides timing, not the assessor.

## What is NOT a finding

- Test-only code using weak crypto to test the weak-crypto rejection path.
- Local-dev emulator connection strings that are publicly documented well-known keys (e.g. the Cosmos emulator key) — unless they can leak into production config.
- Vulnerabilities in code paths that are provably unreachable — log as `info` with the reachability argument.
