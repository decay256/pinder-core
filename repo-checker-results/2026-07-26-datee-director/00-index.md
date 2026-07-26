# DATEE Director Changed-Code Audit

> Scope: Core #1341 changed files only. Topics: Eigentakt LLM-dirt cluster
> plus external-call robustness.

## Result

- Remaining U1: 0
- Fixed U1: 2
- U2: 2, filed as #1352 and #1353
- U3: 10 recorded

## U1 Resolutions

- Duplicate generated JSON properties now fail closed through
  `DuplicatePropertyNameHandling.Error`.
- Native structured and fallback director responses now share the same 64 KiB
  pre-parse input bound.

Both fixes have focused regressions and independent zero-U1 re-check evidence.

## U2 Follow-Ups

- #1352: standardize in-payload schema versioning for emotional-director
  structured output.
- #1353: derive assembly-version tests from the canonical package version.

## Triage Notes

- The unwired director operation is intentional: #1341 establishes the callable
  contract, while #1342 owns director/performance orchestration and #1343 owns
  performance-prompt integration.
- U3 findings cover localized duplication, documentation drift, lexical
  validation maintenance risk, a shadowed temperature fallback, and nullable
  prompt-entry suppressions. None blocks #1341.

## Reports

- `dry-violations.md`: 3 U3
- `doc-code-mismatches.md`: 3 U3
- `unwired-code.md`: 1 U3, intentional dependency boundary
- `anti-patterns.md`: 1 U3
- `trivial-tests.md`: no findings
- `prompt-hardcoding.md`: no findings
- `silent-fallbacks.md`: 1 U1, fixed
- `model-id-drift.md`: 1 U3
- `migration-integrity.md`: 2 U2, ticketed
- `type-safety-erosion.md`: 1 U3
- `external-call-robustness.md`: 1 U1, fixed
