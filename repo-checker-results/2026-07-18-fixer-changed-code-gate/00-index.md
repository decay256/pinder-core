# Fixer Changed-Code Gate

> Scope: pinder-core `7962415f750de354b53d4f9b953eaa3e37b3575b..5dd4274b82e0e2d1c78471909341323eafb84856`; pinder-web `c7a465bb67fb86ee6b5ab1105955b7c0717eddca..3350d6692a4f38c1ebeb6df272135c4a4d4fc9fe`. LLM-dirt topics only, after the U1 fixer and re-audit loop.

| Topic | Findings | U1 | U2 | U3 |
|---|---:|---:|---:|---:|
| anti-patterns | 6 | 0 | 5 | 1 |
| doc-code-mismatches | 9 | 0 | 2 | 7 |
| dry-violations | 6 | 0 | 1 | 5 |
| migration-integrity | 0 | 0 | 0 | 0 |
| model-id-drift | 4 | 0 | 2 | 2 |
| prompt-hardcoding | 1 | 0 | 1 | 0 |
| silent-fallbacks | 2 | 0 | 2 | 0 |
| trivial-tests | 3 | 0 | 3 | 0 |
| type-safety-erosion | 1 | 0 | 0 | 1 |
| unwired-code | 1 | 0 | 0 | 1 |
| **Total** | **33** | **0** | **16** | **17** |

## Handling

- All six original U1 findings, plus one replay-schema U1 exposed by the first re-audit, were fixed. Re-runs of anti-patterns, silent-fallbacks, and migration-integrity report zero U1 findings.
- U2 findings are consolidated into GitHub follow-up issues: pinder-core [#1323](https://github.com/decay256/pinder-core/issues/1323), [#1324](https://github.com/decay256/pinder-core/issues/1324), [#1325](https://github.com/decay256/pinder-core/issues/1325); pinder-web [#1174](https://github.com/decay256/pinder-web/issues/1174), [#1175](https://github.com/decay256/pinder-web/issues/1175), [#1176](https://github.com/decay256/pinder-web/issues/1176), [#1177](https://github.com/decay256/pinder-web/issues/1177).
- U3 findings are recorded for review and are not release blockers.
- Approved exception patterns were supplied to every topic worker. No suppressed would-be-U1 finding was reported.

The final dedupe pass found no duplicate finding requiring removal. Related findings that share a file or remediation path remain separated only when they describe distinct behavior.

## Final Review Addendum

- Independent review after this gate found one release-blocking speculative-branch operation lifecycle defect outside the audit commit range. It was fixed in pinder-web `db23e9bb` and `e2c339c8`: clone/setup failure now terminates the operation instead of leaving it active, with a focused regression test.
- Staging deployment of pinder-web `e2c339c8` with pinder-core `5dd4274` passed version, homepage, container-health, authentication-boundary, session-sheet route, and character-randomize route smoke checks.
- Staging startup logs retain one non-blocking data-quality issue for follow-up: character `28929416-c8b3-4ab8-898d-892815677fb8` references unknown item `Hair Style 2` and is skipped; 12 valid characters load.
