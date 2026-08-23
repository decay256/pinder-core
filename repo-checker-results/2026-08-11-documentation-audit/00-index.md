# Documentation Audit Index

Audit date: 2026-08-11

Repositories checked:

- `pinder-core` at `50cb30599107a0eafe72e177757fd4927f1ba774` (primary)
- `pinder-web` at `8f4e9d33f67721dfc71955985193430854d62180` (cross-repo API and display verification)

## Scope

The audit compared current documentation in `pinder-core` with production code, tests, project files, prompt YAML, and configuration. It also checked `pinder-web` where core documentation claims GameApi transport, configuration, or visible UI behavior.

Excluded as non-current evidence: archived docs, sprint records, previous audit output, generated reports, vendor/dependency output, and issue notes that clearly identify themselves as historical. Existing generated Playwright changes in `pinder-web` were left untouched.

## Result

Remediation status: **all seven findings resolved in the working tree on 2026-08-11**. The detailed report remains the point-in-time evidence that motivated the changes.

| Severity | Count |
|---|---:|
| U1 - critical | 0 |
| U2 - important | 4 |
| U3 - minor | 3 |
| **Total** | **7** |

No findings were suppressed and no documented exceptions were supplied.

## Ranked Findings

1. **U2, resolved:** Stateful adapter/session ownership docs described removed APIs.
2. **U2, resolved:** GameSession docs described removed Read/Recover actions and an optional clock.
3. **U2, resolved:** Unity integration examples did not compile against current adapter and transport contracts.
4. **U2, resolved:** The purportedly authoritative prompt map omitted active runtime-loaded prompt YAML files.
5. **U3, resolved:** The overlay-transport follow-up status was stale; GameApi wiring is live.
6. **U3, resolved:** The README understated current `Pinder.LlmAdapters` dependencies.
7. **U3, resolved:** The defending-stat spec claimed a visible web display that the display path dropped; the option-roll summary now renders it.

Full evidence, impact, urgency rationale, and remediation guidance are in [doc-code-mismatches.md](doc-code-mismatches.md).

## Gameplay-State Overview Readiness

There is currently no single authoritative document mapping gameplay state from `pinder-core`, through GameApi DTO/SSE transport, to the exact `pinder-web` display surface. The full report records the recommended phase model and the field-by-field mapping work needed for that follow-up artifact.
