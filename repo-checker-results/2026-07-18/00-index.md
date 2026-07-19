# Repo-Checker Audit Index - 2026-07-18

## Audit Scope

- Primary repository: `A:\Data\ClaudeCodex\pinder-core` at `e96a75f4c4fb7b8c008f8c61403aae6327eb6ca2`
- Dependent repository: `A:\Data\ClaudeCodex\pinder-web` at `1a7acb382c7daab1976a23bf2b738b0a35b8c4ab`
- `pinder-web` embedded `pinder-core` submodule: `a0c59c8cdd111bb0bf44bafe50722c5bd4df5e09`
- Primary reports: `A:\Data\ClaudeCodex\pinder-core\repo-checker-results\2026-07-18`
- Mirror reports: `A:\Data\Obsidian\Eigen\Pinder Design\Audit\2026-07-18`
- Scope audited: full multi-repository Pinder system across the 26 canonical repo-checker topics.

## Approved Exceptions

These approved patterns were loaded and preserved as suppression notes where relevant:

1. Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses
2. Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints
3. Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users
4. Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads

## Topic Counts

| # | Topic | U1 | U2 | U3 | Total | Report |
|---:|---|---:|---:|---:|---:|---|
| 1 | dry-violations | 0 | 2 | 7 | 9 | [dry-violations.md](dry-violations.md) |
| 2 | doc-code-mismatches | 0 | 1 | 6 | 7 | [doc-code-mismatches.md](doc-code-mismatches.md) |
| 3 | unwired-code | 0 | 2 | 7 | 9 | [unwired-code.md](unwired-code.md) |
| 4 | backend-frontend-gaps | 0 | 2 | 0 | 2 | [backend-frontend-gaps.md](backend-frontend-gaps.md) |
| 5 | soc-violations | 0 | 1 | 4 | 5 | [soc-violations.md](soc-violations.md) |
| 6 | anti-patterns | 2 | 2 | 3 | 7 | [anti-patterns.md](anti-patterns.md) |
| 7 | trivial-tests | 0 | 3 | 0 | 3 | [trivial-tests.md](trivial-tests.md) |
| 8 | bad-test-coverage | 0 | 2 | 0 | 2 | [bad-test-coverage.md](bad-test-coverage.md) |
| 9 | i18n-violations | 0 | 0 | 9 | 9 | [i18n-violations.md](i18n-violations.md) |
| 10 | prompt-hardcoding | 0 | 0 | 8 | 8 | [prompt-hardcoding.md](prompt-hardcoding.md) |
| 11 | silent-fallbacks | 3 | 1 | 0 | 4 | [silent-fallbacks.md](silent-fallbacks.md) |
| 12 | model-id-drift | 0 | 0 | 5 | 5 | [model-id-drift.md](model-id-drift.md) |
| 13 | unclosed-resources | 1 | 3 | 0 | 4 | [unclosed-resources.md](unclosed-resources.md) |
| 14 | sync-blocking | 2 | 4 | 0 | 6 | [sync-blocking.md](sync-blocking.md) |
| 15 | validation-gaps | 6 | 2 | 0 | 8 | [validation-gaps.md](validation-gaps.md) |
| 16 | internal-logging-gaps | 0 | 4 | 0 | 4 | [internal-logging-gaps.md](internal-logging-gaps.md) |
| 17 | error-handling-and-backtracking | 2 | 2 | 0 | 4 | [error-handling-and-backtracking.md](error-handling-and-backtracking.md) |
| 18 | secrets-exposure | 1 | 0 | 0 | 1 | [secrets-exposure.md](secrets-exposure.md) |
| 19 | external-call-robustness | 7 | 2 | 0 | 9 | [external-call-robustness.md](external-call-robustness.md) |
| 20 | concurrency-idempotency | 4 | 1 | 0 | 5 | [concurrency-idempotency.md](concurrency-idempotency.md) |
| 21 | migration-integrity | 1 | 0 | 0 | 1 | [migration-integrity.md](migration-integrity.md) |
| 22 | performance-pathologies | 0 | 3 | 0 | 3 | [performance-pathologies.md](performance-pathologies.md) |
| 23 | pii-in-logs | 5 | 0 | 0 | 5 | [pii-in-logs.md](pii-in-logs.md) |
| 24 | dependency-health | 0 | 4 | 3 | 7 | [dependency-health.md](dependency-health.md) |
| 25 | type-safety-erosion | 0 | 1 | 7 | 8 | [type-safety-erosion.md](type-safety-erosion.md) |
| 26 | architecture-conformance | 0 | 1 | 0 | 1 | [architecture-conformance.md](architecture-conformance.md) |

## Aggregate Totals

- U1: 34
- U2: 43
- U3: 59
- Total findings: 136

## Highest-Priority Themes

- External-call robustness is the largest U1 cluster: LLM transports, streaming paths, remote asset reads, media proxying, SSE proxying, and admin exports need explicit timeouts, bounded bodies, and retry or cancellation contracts.
- Validation and fail-fast behavior are recurring production risks, especially in catalog loaders, request DTOs, replay payload mapping, GameApi health/readiness, and production rule/default loading.
- Concurrency and idempotency issues affect durable gameplay and admin state: turn resolution retries, progression settlement, skill-point spending, shared admin content writes, and first share-token creation.
- Sensitive data exposure is concentrated in logs and persisted diagnostics: full LLM prompt/response audit records, raw client error telemetry, persistent auth subjects, GameApi progression logs, and remote asset exception bodies.
- Deployment and system-shape drift remains visible through Alembic metadata gaps, health checks that pass degraded dependencies, and `pinder-web` building from an older `pinder-core` submodule than the sibling core repo under audit.

## Deduplication Notes

- Duplicate findings removed in final orchestrator pass: 0.
- Reports changed during deduplication: none.
- Overlapping code citations were retained where the root defect or remediation differed. For example, `MediaController.cs` findings separately cover missing FastAPI proxy wiring, controller ownership of the Eigencore protocol, missing regression coverage, undisposed HTTP responses, missing structured external-call logs, raw upstream failure-body exposure, and missing timeout/body caps.
- Existing approved-exception suppression notes were preserved in the topic reports. No topic report was rewritten solely to reduce counts.

## Verification Status

- All 26 canonical topic reports are present and non-empty.
- Zero-finding canonical reports are represented by non-empty report files; this run ended with findings in all 26 canonical topics.
- `00-index.md` was created in both the primary and mirror locations.
- Primary and mirror files were verified SHA256-identical for all 26 topic reports and both index files after the mirror copy.
