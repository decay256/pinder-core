# DATEE Wave B Changed-Code Audit

Scope: files changed for pinder-core #1338 and #1339, plus the dependent
Pinder GameApi synthesis tests.

## Gate Result

The changed-code LLM-dirt gate passes with zero unresolved U1 findings.

- Raw findings: 4 U1, 1 U2, 14 U3.
- U1 disposition: one code fix; three accepted contract decisions with
  regression evidence.
- U2 disposition: filed as pinder-core #1351.
- U3 disposition: recorded only, per Eigentakt policy.

## U1 Disposition

- Silent-fallback finding 1: accepted canonical generated-output boundary;
  generated extras are discarded while legacy string extras remain loadable.
- Silent-fallback finding 2: accepted configuration-validation boundary;
  structural runtime validation and authored-content regression tests remain
  separate concerns.
- Silent-fallback finding 3: fixed by deriving diagnosis prompt validation from
  `TherapistDiagnosisContract.RequiredFields`.
- Migration-integrity finding 1: resolved through the existing diagnosis-stage
  regeneration path and an atomic legacy-map replacement regression.

See `silent-fallbacks-resolved.md` and `migration-integrity-resolved.md`.

## Reports

- `dry-violations.md`: 0 U1, 0 U2, 4 U3.
- `doc-code-mismatches.md`: 0 U1, 0 U2, 2 U3.
- `unwired-code.md`: 0 U1, 0 U2, 2 U3.
- `anti-patterns.md`: 0 U1, 0 U2, 3 U3.
- `trivial-tests.md`: 0 U1, 1 U2, 0 U3.
- `prompt-hardcoding.md`: no findings.
- `silent-fallbacks.md`: 3 U1, all resolved or accepted above.
- `model-id-drift.md`: no findings.
- `migration-integrity.md`: 1 U1, resolved above.
- `type-safety-erosion.md`: 0 U1, 0 U2, 3 U3.
