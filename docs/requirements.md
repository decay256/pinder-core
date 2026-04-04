# Pinder.Core — Functional Requirements

## Sprint: Rules DSL + Rule Engine

### Overview
This sprint introduces a rules DSL pipeline and hybrid rule engine to externalize game constants from C# code into version-controlled YAML, enabling balancing without recompilation.

### Issues

#### #443 — Rules DSL: fix remaining round-trip diffs
**Goal**: Round-trip conversion (Markdown → YAML → Markdown) produces < 50 diff lines per document.
**Acceptance Criteria**: Paragraph ordering preserved, table column widths preserved, all 9 docs pass roundtrip_test.sh with < 50 lines diff each.

#### #444 — Rules DSL: enrich all 9 YAML files with condition/outcome fields
**Goal**: All 9 extracted YAML docs have structured `condition`/`outcome` fields on entries with numeric thresholds or named effects.
**Acceptance Criteria**: All 9 docs enriched, 0 INACCURATE findings in accuracy check, total enriched entry count reported.

#### #445 — Rules DSL: integrate generated test stubs into pinder-core test suite
**Goal**: 54 auto-generated xUnit tests from enriched YAML compile and run in CI.
**Acceptance Criteria**: 54 tests in `tests/Pinder.Core.Tests/RulesSpec/`, all compile, 17 NotImplemented stubs remain as skipped, all 2238 existing tests still pass.

#### #446 — Hybrid rule engine: RuleBook + RuleEngine
**Goal**: Generic rule engine evaluates YAML conditions against GameState, replacing hardcoded constants for §5 (failure tiers) and §6 (interest states) at minimum.
**Acceptance Criteria**: `RuleBook`, `ConditionEvaluator`, `OutcomeDispatcher`, `IEffectHandler` implemented; GameSession uses engine for §5 and §6; all existing tests pass; no numeric constants remain hardcoded in FailureScale.cs or InterestMeter.cs.
**Dependencies**: #443, #444.

### Pre-flight Summary

| Issue | AC | Description | Role | Maturity | Status |
|-------|-----|-------------|------|----------|--------|
| #443 | ✅ 5 items | ✅ Clear | ✅ Added | ✅ Added | Improved |
| #444 | ✅ 5 items | ✅ Clear | ✅ Added | ✅ Added | Improved |
| #445 | ✅ 5 items | ✅ Clear | ✅ Added | ✅ Added | Improved |
| #446 | ✅ 6 items | ✅ Detailed | ✅ Added | ✅ Added | Improved |

All four issues had well-formed acceptance criteria and descriptions. All four were missing **Role** and **Maturity** fields, which were added as `backend-engineer` / `prototype`.
